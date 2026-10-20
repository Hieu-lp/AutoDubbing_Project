"""Manual Gemini probe. Standard library only; SELECT only; no API integration."""
import base64
import datetime as dt
import io
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import urllib.error
import urllib.request
import wave
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TEXT_MODEL = "gemini-3.1-flash-lite"
TTS_MODEL = "gemini-3.1-flash-tts-preview"
BASE = "https://generativelanguage.googleapis.com/v1beta/models/"
MAX_RESPONSE = 24 * 1024 * 1024


class ProbeError(Exception):
    """Only static, non-sensitive codes may be used as messages."""


class HttpFailure(ProbeError):
    def __init__(self, status):
        self.status = status
        super().__init__({401: "HTTP_401_UNAUTHENTICATED", 403: "HTTP_403_FORBIDDEN",
                          404: "HTTP_404_MODEL_NOT_FOUND", 429: "HTTP_429_QUOTA_OR_RATE_LIMIT",
                          500: "HTTP_500_PROVIDER_ERROR", 503: "HTTP_503_SERVICE_UNAVAILABLE"}
                         .get(status, "HTTP_REQUEST_FAILED"))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    # Never forward an API key to a redirect target.
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def read_key():
    project = ET.parse(ROOT / "AutoDubbing.API/AutoDubbing.API.csproj")
    secret_id = project.findtext(".//UserSecretsId")
    if not secret_id or Path(secret_id).name != secret_id:
        raise ProbeError("INVALID_USER_SECRETS_ID")
    path = Path(os.environ["APPDATA"]) / "Microsoft/UserSecrets" / secret_id / "secrets.json"
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    flat = {name.lower(): value for name, value in data.items()}
    key = flat.get("gemini:apikey")
    if key is None and isinstance(flat.get("gemini"), dict):
        key = {name.lower(): value for name, value in flat["gemini"].items()}.get("apikey")
    if not isinstance(key, str) or not key.strip():
        raise ProbeError("GEMINI_KEY_EMPTY_OR_MISSING")
    if key != key.strip() or not key.isascii() or any(ord(c) < 33 or ord(c) > 126 for c in key):
        raise ProbeError("GEMINI_KEY_INVALID_CHARACTERS")
    return key


def read_poi():
    # EncodedCommand preserves Vietnamese on Windows PowerShell 5.1; contains no secret.
    script = r"""
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $connection = New-Object System.Data.SqlClient.SqlConnection('Server=.;Database=AutoDubbingDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;ApplicationIntent=ReadOnly;Application Name=AutoDubbingGeminiSmokeReadOnly;')
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandTimeout = 10
            $command.CommandText = 'SELECT id,ten,mo_ta_goc,ngon_ngu_goc,trang_thai FROM dbo.POI WHERE ten=@PoiName;'
            [void]$command.Parameters.Add('@PoiName', [System.Data.SqlDbType]::NVarChar, 200)
            $command.Parameters['@PoiName'].Value = 'Nhà thờ Đức Bà'
            $reader = $command.ExecuteReader()
            $rows = New-Object System.Collections.Generic.List[object]
            while ($reader.Read()) {
                $rows.Add([pscustomobject]@{Id=$reader.GetInt32(0); Name=$reader.GetString(1); OriginalDescription=$reader.GetValue(2); OriginalLanguage=$reader.GetValue(3); Status=$reader.GetValue(4)})
            }
            $reader.Close()
            ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 4 -Compress
        } catch { [Console]::Error.WriteLine('SQL_READ_FAILED'); exit 2 }
        finally { $connection.Dispose() }
    """
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                            capture_output=True, timeout=25)
    if result.returncode:
        raise ProbeError("SQL_READ_FAILED")
    rows = json.loads(result.stdout.decode("utf-8-sig"))
    if len(rows) != 1:
        raise ProbeError("POI_NOT_FOUND" if not rows else "POI_NAME_AMBIGUOUS")
    poi = rows[0]
    if poi["Name"] != "Nhà thờ Đức Bà" or poi["OriginalLanguage"] != "vi":
        raise ProbeError("UNEXPECTED_POI_OR_LANGUAGE")
    return poi


def excerpt(description):
    if not isinstance(description, str) or not description.strip():
        raise ProbeError("EMPTY_POI_DESCRIPTION")
    # Use the exact prefix, including its punctuation; no rewriting.
    ends = list(re.finditer(r"[.!?](?=\s|$)", description))
    if not ends:
        raise ProbeError("SENTENCE_BOUNDARY_NOT_FOUND")
    text = description[:ends[min(1, len(ends) - 1)].end()]
    if len(text) > 1000:
        raise ProbeError("EXCERPT_TOO_LONG_REVIEW_MANUALLY")
    return text


def request(key, model, payload=None):
    url = BASE + model + (":generateContent" if payload is not None else "")
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(url, data=body,
                                 headers={"x-goog-api-key": key, "Content-Type": "application/json"})
    opener = urllib.request.build_opener(NoRedirect)
    try:
        with opener.open(req, timeout=55) as response:
            raw = response.read(MAX_RESPONSE + 1)
        if len(raw) > MAX_RESPONSE:
            raise ProbeError("RESPONSE_TOO_LARGE")
        return json.loads(raw)
    except urllib.error.HTTPError as error:
        # Provider body / URL / exception text can contain sensitive details. Never persist them.
        error.close()
        raise HttpFailure(error.code) from None
    except (TimeoutError, socket.timeout):
        raise ProbeError("HTTP_TIMEOUT") from None
    except urllib.error.URLError:
        raise ProbeError("HTTP_NETWORK_OR_TLS_ERROR") from None


def parts(response):
    candidates = response.get("candidates", [])
    if len(candidates) != 1 or candidates[0].get("finishReason") != "STOP":
        raise ProbeError("GENERATION_BLOCKED_OR_INCOMPLETE")
    return candidates[0].get("content", {}).get("parts", [])


def make_wav(data, mime):
    fields = [field.strip().lower() for field in mime.split(";")]
    if fields[0] in ("audio/wav", "audio/x-wav", "audio/wave"):
        encoded = data
    elif fields[0] in ("audio/l16", "audio/pcm"):
        attrs = dict(field.split("=", 1) for field in fields[1:] if "=" in field)
        rate = attrs.get("rate", "")
        if not rate.isdigit() or not 8000 <= int(rate) <= 192000:
            raise ProbeError("PCM_SAMPLE_RATE_METADATA_MISSING_OR_INVALID")
        if attrs.get("channels", "1") != "1" or attrs.get("codec", "pcm") != "pcm":
            raise ProbeError("UNSUPPORTED_PCM_METADATA")
        # Gemini 3.1 TTS contract: little-endian signed 16-bit mono PCM.
        if not data or len(data) % 2:
            raise ProbeError("PCM_EMPTY_OR_UNALIGNED")
        stream = io.BytesIO()
        with wave.open(stream, "wb") as audio:
            audio.setnchannels(1)
            audio.setsampwidth(2)
            audio.setframerate(int(rate))
            audio.writeframes(data)
        encoded = stream.getvalue()
    else:
        raise ProbeError("UNSUPPORTED_AUDIO_MIME")
    try:
        with wave.open(io.BytesIO(encoded), "rb") as audio:
            frames, rate = audio.getnframes(), audio.getframerate()
            channels, width = audio.getnchannels(), audio.getsampwidth()
            actual = audio.readframes(frames)
            if not frames or not rate or len(actual) != frames * channels * width:
                raise ProbeError("WAV_EMPTY_OR_TRUNCATED")
            info = {"format": "WAV PCM", "sampleRateHz": rate, "channels": channels,
                    "bitsPerSample": width * 8, "durationSeconds": frames / rate,
                    "bytes": len(encoded), "providerMime": mime}
    except (wave.Error, EOFError):
        raise ProbeError("INVALID_WAV") from None
    return encoded, info


def audio_result(response):
    audio = [part["inlineData"] for part in parts(response) if "inlineData" in part]
    if len(audio) != 1:
        raise ProbeError("EXPECTED_ONE_AUDIO_PART")
    try:
        data = base64.b64decode(audio[0]["data"], validate=True)
    except (ValueError, KeyError):
        raise ProbeError("INVALID_AUDIO_BASE64") from None
    return make_wav(data, audio[0].get("mimeType", ""))


def tts_payload(text, language):
    return {"contents": [{"role": "user", "parts": [{"text":
            f"Synthesize speech in {language}. Read only the exact transcript below. "
            "Do not translate, paraphrase, add content, or speak these instructions.\n"
            f"<transcript>\n{text}\n</transcript>"}]}],
            "generationConfig": {"responseModalities": ["AUDIO"], "speechConfig": {
                "voiceConfig": {"prebuiltVoiceConfig": {"voiceName": "Kore"}}}}}


def check_translation(text, original, poi_name):
    if not text or len(text) > 4000:
        raise ProbeError("EMPTY_OR_OVERSIZED_TRANSLATION")
    full_name = "Nhà thờ Đức Bà Sài Gòn"
    required_name = full_name if full_name in original else poi_name
    if required_name in original and required_name not in text:
        raise ProbeError("PROPER_NAME_NOT_PRESERVED_REQUIRES_REVIEW")
    for year in re.findall(r"\b\d{4}\b", original):
        if not re.search(r"\b" + year + r"\b", text):
            raise ProbeError("YEAR_NOT_PRESERVED_REQUIRES_REVIEW")
    for name in ("Roman", "Gothic"):
        if re.search(r"\b" + name + r"\b", original) and not re.search(r"\b" + name + r"\b", text):
            raise ProbeError("ARCHITECTURE_NAME_NOT_PRESERVED_REQUIRES_REVIEW")


def main():
    key = read_key()
    poi = read_poi()
    original = excerpt(poi["OriginalDescription"])
    folder = ROOT / "artifacts/gemini-smoke-test" / dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    folder.mkdir(parents=True, exist_ok=False)
    report = {"poiId": poi["Id"], "models": {}, "stages": {}, "pronunciationReviewed": False}

    def save_text(name, content):
        if key in content:
            raise ProbeError("SENSITIVE_OUTPUT_REJECTED")
        (folder / name).write_text(content, encoding="utf-8")

    def stage(name, action):
        try:
            result = action()
            report["stages"][name] = {"status": "success"}
            print(name + ": success", flush=True)
            return result
        except ProbeError as error:
            report["stages"][name] = {"status": "failed", "code": str(error)}
            if isinstance(error, HttpFailure):
                report["stages"][name]["httpStatus"] = error.status
            print(name + ": " + str(error), flush=True)
            # Stop on authentication / authorization / quota failure; no automatic retry/fallback.
            if isinstance(error, HttpFailure) and error.status in (401, 403, 429):
                raise
            return None

    save_text("source-vi.txt", original)
    save_text("poi-source.json", json.dumps(poi, ensure_ascii=False, indent=2))
    try:
        available = {}
        for model in (TEXT_MODEL, TTS_MODEL):
            metadata = stage("model:" + model, lambda m=model: request(key, m))
            available[model] = bool(metadata and "generateContent" in metadata.get("supportedGenerationMethods", []))
            report["models"][model] = {"generateContentSupported": available[model]}
        translation = None
        if available[TEXT_MODEL]:
            def translate():
                response = request(key, TEXT_MODEL, {
                    "systemInstruction": {"parts": [{"text": "Translate Vietnamese into English faithfully. "
                        "Preserve all proper names exactly as written, meaning and facts. Do not add any "
                        "introduction, new information, explanation or Markdown. "
                        "Mandatory literal proper name: Nhà thờ Đức Bà Sài Gòn. "
                        "Keep this entire Vietnamese name verbatim with diacritics inside the English sentence; "
                        "do not replace it with Notre Dame Cathedral of Saigon. "
                        "Preserve the architecture names Roman and Gothic as written in the source; "
                        "do not silently correct Roman into Romanesque. Output only the translation."}]},
                    "contents": [{"role": "user", "parts": [{"text": original}]}],
                    "generationConfig": {"temperature": 0, "maxOutputTokens": 2048}})
                text = "".join(p.get("text", "") for p in parts(response) if not p.get("thought")).strip()
                check_translation(text, original, poi["Name"])
                save_text("translation-en.txt", text)
                return text
            translation = stage("translation", translate)
        if available[TTS_MODEL]:
            for language, text, code in (("Vietnamese", original, "vi"), ("English", translation, "en")):
                if not text:
                    report["stages"]["audio-" + code] = {"status": "skipped", "code": "NO_TRANSLATION"}
                    continue
                def speak(t=text, lang=language, c=code):
                    encoded, info = audio_result(request(key, TTS_MODEL, tts_payload(t, lang)))
                    filename = "audio-" + c + ".wav"
                    (folder / filename).write_bytes(encoded)
                    report["audio-" + c] = dict(info, file=filename)
                stage("audio-" + code, speak)
    except HttpFailure:
        report["stoppedEarly"] = "AUTHORIZATION_OR_QUOTA_FAILURE_NO_RETRY"
    finally:
        save_text("report.json", json.dumps(report, ensure_ascii=False, indent=2))
        print("Output: " + str(folder), flush=True)
    return 0 if all(report["stages"].get(s, {}).get("status") == "success"
                    for s in ("translation", "audio-vi", "audio-en")) else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except ProbeError as error:
        print("Stopped: " + str(error), file=sys.stderr)
        sys.exit(2)
    except Exception:
        # Do not expose request/headers/secrets through tracebacks.
        print("Stopped: UNEXPECTED_LOCAL_OR_RESPONSE_ERROR (sensitive details omitted)", file=sys.stderr)
        sys.exit(2)
