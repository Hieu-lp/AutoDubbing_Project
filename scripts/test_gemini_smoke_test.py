"""Offline checks only. No SQL connection and no real Gemini request."""
import io
import unittest
import urllib.error
from unittest.mock import patch
import wave

import gemini_smoke_test as probe


class SmokeTestTests(unittest.TestCase):
    def test_pcm_wrapped_with_actual_sample_rate_and_duration(self):
        encoded, info = probe.make_wav(b"\x00\x00" * 24000, "audio/L16;codec=pcm;rate=24000")
        self.assertEqual(info["durationSeconds"], 1)
        self.assertEqual(info["sampleRateHz"], 24000)
        self.assertEqual(info["bytes"], 48044)
        with wave.open(io.BytesIO(encoded), "rb") as audio:
            self.assertEqual((audio.getnchannels(), audio.getsampwidth()), (1, 2))
            self.assertEqual(audio.readframes(24000), b"\x00\x00" * 24000)

    def test_existing_wav_not_wrapped_twice(self):
        encoded, _ = probe.make_wav(b"\x00\x00" * 12000, "audio/L16;rate=24000")
        same, info = probe.make_wav(encoded, "audio/wav")
        self.assertEqual(same, encoded)
        self.assertEqual(info["durationSeconds"], 0.5)

    def test_missing_metadata_or_invalid_pcm_rejected(self):
        for data, mime in ((b"xx", "audio/pcm"), (b"x", "audio/L16;rate=24000"),
                           (b"", "audio/L16;rate=24000"), (b"xx", "audio/mpeg"),
                           (b"xx", "audio/L16;rate=24000;channels=2")):
            with self.subTest(mime=mime, size=len(data)):
                with self.assertRaises(probe.ProbeError):
                    probe.make_wav(data, mime)

    def test_truncated_wav_rejected(self):
        encoded, _ = probe.make_wav(b"\x00\x00" * 24000, "audio/L16;rate=24000")
        with self.assertRaises(probe.ProbeError):
            probe.make_wav(encoded[:-2], "audio/wav")

    def test_original_excerpt_is_exact_prefix(self):
        original = "Chào bạn. Được khánh thành năm 1880! Câu thứ ba."
        self.assertEqual(probe.excerpt(original), "Chào bạn. Được khánh thành năm 1880!")
        self.assertTrue(original.startswith(probe.excerpt(original)))

    def test_provider_errors_redacted_and_never_retried(self):
        for status in (401, 403, 404, 429):
            with self.subTest(status=status), patch.object(probe.urllib.request, "build_opener") as factory:
                factory.return_value.open.side_effect = urllib.error.HTTPError(
                    "https://example.invalid?secret=DO_NOT_EXPOSE", status,
                    "DO_NOT_EXPOSE", {}, io.BytesIO(b'{"error":{"message":"DO_NOT_EXPOSE"}}'))
                with self.assertRaises(probe.HttpFailure) as caught:
                    probe.request("DO_NOT_EXPOSE", probe.TEXT_MODEL)
                self.assertNotIn("DO_NOT_EXPOSE", str(caught.exception))
                self.assertEqual(caught.exception.status, status)
                self.assertEqual(factory.return_value.open.call_count, 1)

    def test_timeout_is_sanitized(self):
        with patch.object(probe.urllib.request, "build_opener") as factory:
            factory.return_value.open.side_effect = TimeoutError("DO_NOT_EXPOSE")
            with self.assertRaisesRegex(probe.ProbeError, "^HTTP_TIMEOUT$"):
                probe.request("DO_NOT_EXPOSE", probe.TTS_MODEL)
            self.assertEqual(factory.return_value.open.call_count, 1)

    def test_tts_prompt_contains_exact_transcript(self):
        original = "Được khánh thành vào năm 1880."
        payload = probe.tts_payload(original, "Vietnamese")
        self.assertIn("<transcript>\n" + original + "\n</transcript>", payload["contents"][0]["parts"][0]["text"])
        self.assertEqual(payload["generationConfig"]["responseModalities"], ["AUDIO"])

    def test_translated_proper_name_or_missing_year_requires_review(self):
        original = "Nhà thờ Đức Bà Sài Gòn khánh thành năm 1880."
        for text in ("Notre Dame Cathedral opened in 1880.", "Nhà thờ Đức Bà Sài Gòn opened."):
            with self.assertRaises(probe.ProbeError):
                probe.check_translation(text, original, "Nhà thờ Đức Bà")
        probe.check_translation("Nhà thờ Đức Bà Sài Gòn opened in 1880.", original, "Nhà thờ Đức Bà")


if __name__ == "__main__":
    unittest.main()
