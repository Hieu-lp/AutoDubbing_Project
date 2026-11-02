"""Per-machine Docker updater. Standard library only; never initializes/migrates SQL."""
import argparse
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import time
import urllib.parse
import urllib.request

SERVICES = ("api", "user-web")
ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONFIG = ROOT / "artifacts/cd/host.json"


class CdError(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise CdError(message)


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def write_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def run(args, *, input=None, timeout=60):
    # Docker errors can echo resolved configuration. Never relay raw stdout/stderr.
    try:
        result = subprocess.run(args, input=input, capture_output=True, timeout=timeout)
    except (OSError, subprocess.TimeoutExpired):
        raise CdError("Command unavailable or timed out: " + " ".join(args[:2])) from None
    if result.returncode:
        if args[:2] == ["docker", "pull"]:
            error = result.stderr.decode("utf-8", errors="replace").lower()
            if any(term in error for term in ("denied", "unauthorized", "forbidden", "authentication required")):
                raise CdError("GHCR denied access, or the release package is not available to this Docker user. Publish the release and authenticate with docker login ghcr.io if private.")
            if any(term in error for term in ("manifest unknown", "not found", "no such manifest")):
                raise CdError("The release image/tag has not been published or is unavailable; existing API/web was kept.")
        raise CdError("Command failed: " + " ".join(args[:2]) + "; check Docker, registry access and local configuration.")
    return result.stdout


def inspect(name):
    return json.loads(run(["docker", "inspect", name]))[0]


def image_info(name):
    return json.loads(run(["docker", "image", "inspect", name]))[0]


def wait_for_docker(timeout=180):
    """WSL can invoke the updater while its enabled Docker service is still starting."""
    deadline = time.monotonic() + timeout
    while True:
        try:
            run(["docker", "info", "--format", "{{.ServerVersion}}"], timeout=10)
            return
        except CdError:
            if time.monotonic() >= deadline:
                raise CdError("Docker did not become ready within the startup wait.") from None
            time.sleep(3)


def platform_content_identity(info):
    # Docker's reported .Id may be a config ID or an index/manifest ID, depending
    # on its image store. Compare resolved platform contents/config at one level.
    require(info.get("RootFS", {}).get("Layers") and info.get("Config") is not None,
            "Docker did not expose platform filesystem/config identity.")
    return digest({key: info.get(key) for key in ("Os", "Architecture", "Variant", "RootFS", "Config")})


def running_images_match(images, containers):
    return all(platform_content_identity(image_info(images[name]))
               == platform_content_identity(image_info(containers[name]["Image"])) for name in SERVICES)


def env_map(container):
    return dict(item.split("=", 1) for item in container["Config"].get("Env", []) if "=" in item)


def connection_parts(value):
    """Split a SqlClient string while respecting quoted/escaped semicolons."""
    fields, part, quote, index = [], "", None, 0
    while index < len(value):
        char = value[index]
        if quote:
            if char == quote and index + 1 < len(value) and value[index + 1] == quote:
                part += char + char
                index += 2
                continue
            if char == quote:
                quote = None
        elif char in "\"'":
            quote = char
        elif char == ";":
            fields.append(part)
            part = ""
            index += 1
            continue
        part += char
        index += 1
    require(quote is None, "Invalid quoted database connection configuration.")
    fields.append(part)
    return {key.strip().lower(): val.strip().strip("\"'")
            for field in fields if "=" in field for key, val in [field.split("=", 1)]}


def validate_release(release, prefix):
    require(isinstance(release, dict), "Release manifest must be a JSON object.")
    require(release.get("format") == 1 and release.get("channel") == "develop", "Unsupported release format/channel.")
    require(bool(re.fullmatch(r"[a-f0-9]{40}", str(release.get("revision", "")))), "Invalid release revision.")
    require(type(release.get("sequence")) is int and release["sequence"] > 0, "Invalid release sequence.")
    images = release.get("images", {})
    require(isinstance(images, dict) and set(images) == set(SERVICES), "Release must contain the API and web together.")
    for service, repository in (("api", "autodubbing-api"), ("user-web", "autodubbing-user-web")):
        pattern = re.escape(prefix + "/" + repository) + r"@sha256:[a-f0-9]{64}"
        require(isinstance(images[service], str) and bool(re.fullmatch(pattern, images[service])),
                "Release image is outside the configured registry or is not pinned by digest.")
    migrations = release.get("migrations")
    require(isinstance(migrations, list) and bool(migrations)
            and all(isinstance(x, str) and re.fullmatch(r"\d{14}_[A-Za-z0-9_]+", x) for x in migrations)
            and migrations == sorted(set(migrations)), "Invalid migration manifest.")
    return release


@contextlib.contextmanager
def host_lock(directory):
    directory.mkdir(parents=True, exist_ok=True)
    with (directory / "updater.lock").open("a+b") as stream:
        try:
            if os.name == "nt":
                import msvcrt
                stream.seek(0)
                stream.write(b"0")
                stream.flush()
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            raise CdError("Another updater is already running.") from None
        yield


class Host:
    def __init__(self, config_path):
        self.config_path = Path(config_path).resolve()
        self.directory = self.config_path.parent
        self.config = read_json(self.config_path)
        self.state_path = self.directory / "state.json"
        self.active_path = self.directory / "active.compose.json"
        self.state = read_json(self.state_path)

    def event(self, status, **safe_fields):
        record = {"time": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "status": status, **safe_fields}
        write_json(self.directory / "status.json", record)
        log = self.directory / "events.jsonl"
        if log.exists() and log.stat().st_size > 5 * 1024 * 1024:
            os.replace(log, self.directory / "events.previous.jsonl")
        with log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(record, ensure_ascii=False) + "\n")
        print(json.dumps(record, ensure_ascii=False), flush=True)

    def save(self):
        write_json(self.state_path, self.state)

    def compose(self, override=None):
        c = self.config
        result = ["docker", "compose", "--project-name", c["project"],
                  "--project-directory", c["project_directory"], "--env-file", c["env_file"]]
        for path in c["compose_files"]:
            result += ["-f", path]
        return result + ["-f", str(override or self.active_path)]

    def containers(self):
        result = {}
        for service in (*SERVICES, "sqlserver"):
            ids = run(["docker", "ps", "-aq", "--filter", "label=com.docker.compose.project=" + self.config["project"],
                       "--filter", "label=com.docker.compose.service=" + service]).decode().split()
            require(len(ids) == 1, "Expected exactly one existing container for service " + service)
            result[service] = inspect(ids[0])
        return result

    def sql_json(self, query):
        output = run(["docker", "exec", "-i", self.config["sql_id"], "sh", "-c",
                      'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd '
                      '-S tcp:127.0.0.1,1433 -U sa -C -b -I -l 5 -t 20 -y 0 -w 65535 -r 1 -d "$1"',
                      "cd-read-only", self.config["database"]],
                     input=("SET NOCOUNT ON;\n" + query + "\nGO\n").encode(), timeout=30).decode("utf-8-sig")
        try:
            return json.loads(output.strip())
        except ValueError:
            raise CdError("SQL read did not return the expected JSON; no SQL changes were attempted.") from None

    def database(self):
        return self.sql_json("""
SELECT DB_NAME() AS database_name,
  CONVERT(varchar(40), SERVERPROPERTY('ProductVersion')) AS sql_version,
  (SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH) AS migrations,
  (SELECT id, ten AS name FROM dbo.POI WHERE trang_thai=N'Published' ORDER BY id FOR JSON PATH) AS pois,
  (SELECT COUNT_BIG(*) FROM dbo.NGUOI_DUNG) AS accounts,
  (SELECT COUNT_BIG(*) FROM dbo.BAN_DICH WHERE trang_thai=N'Ready') AS ready_translations,
  (SELECT COUNT_BIG(*) FROM dbo.AUDIO WHERE trang_thai=N'Ready') AS ready_audio,
  (SELECT TOP (1) a.poi_id AS poi_id, a.ngon_ngu AS language, a.id AS audio_id
   FROM dbo.AUDIO a JOIN dbo.POI p ON p.id=a.poi_id
   WHERE p.trang_thai=N'Published' AND a.trang_thai=N'Ready'
     AND (a.ngon_ngu=p.ngon_ngu_goc OR EXISTS
       (SELECT 1 FROM dbo.BAN_DICH b WHERE b.poi_id=p.id AND b.ngon_ngu=a.ngon_ngu AND b.trang_thai=N'Ready'))
   ORDER BY a.poi_id, a.id FOR JSON PATH) AS sample
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
""")

    def wait_database(self, timeout=600):
        deadline = time.monotonic() + timeout
        while True:
            try:
                return self.database()
            except CdError:
                if time.monotonic() >= deadline:
                    raise CdError("Existing database did not become ready; no initialization, repair or migration was attempted.") from None
                time.sleep(5)

    def guard(self, *, check_app_env=True):
        containers = self.containers()
        sql = containers["sqlserver"]
        require(sql["Id"] == self.config["sql_id"] and sql["State"]["Running"], "Protected SQL container changed or is stopped.")
        mounts = [(m.get("Name"), m["Destination"]) for m in sql["Mounts"] if m["Type"] == "volume"]
        require([self.config["sql_volume"], "/var/opt/mssql"] in [list(x) for x in mounts], "Protected SQL volume changed.")
        if check_app_env:
            actual = env_map(containers["api"])
            configured = {key: actual.get(key) for key in self.config["api_env_keys"]}
            require(digest(configured) == self.config["api_env_digest"], "API environment differs from the adopted configuration; review it before updating.")
        return containers

    def resolved(self, images):
        candidate = self.directory / "candidate.compose.json"
        write_json(candidate, self.override(images))
        try:
            resolved = json.loads(run(self.compose(candidate) + ["config", "--format", "json"], timeout=60))
        finally:
            candidate.unlink(missing_ok=True)
        api_env = resolved["services"]["api"]["environment"]
        require(digest(api_env) == self.config["api_env_digest"], "Compose/env configuration changed; API update was blocked.")
        require(str(api_env.get("ContentPreparation__AcceptNewGeneration")).lower() == "true", "AcceptNewGeneration must remain true.")
        return resolved

    @staticmethod
    def override(images):
        return {"services": {name: {"image": images[name], "pull_policy": "never"} for name in SERVICES}}

    def up(self, images):
        write_json(self.active_path, self.override(images))
        # Deliberately never call `down`, `up sqlserver`, seed, create-admin or migration commands.
        run(self.compose() + ["up", "-d", "--no-deps", "--no-build", "--pull", "never", "--force-recreate", *SERVICES], timeout=120)

    def get_json(self, path):
        with urllib.request.urlopen(self.config["web_url"] + path, timeout=12) as response:
            return json.load(response)

    def probe(self):
        deadline = time.monotonic() + self.config.get("health_timeout", 180)
        while True:
            try:
                containers = self.guard()
                require(all(containers[x]["State"]["Running"] for x in SERVICES), "API/web is not running.")
                data = self.database()
                require(data["database_name"] == self.config["database"], "Unexpected SQL database.")
                pois = self.get_json("/api/pois")
                require(isinstance(pois, list) and bool(pois), "API must read existing published application POIs.")
                require(sorted((p["id"], p["name"]) for p in pois)
                        == sorted((p["id"], p["name"]) for p in data["pois"]), "API POIs differ from the protected SQL database.")
                detail = self.get_json("/api/pois/" + str(pois[0]["id"]))
                require(detail["id"] == pois[0]["id"] and detail["name"] == pois[0]["name"], "Real POI detail check failed.")
                if data["sample"]:
                    sample = data["sample"][0]
                    content = self.get_json("/api/pois/" + str(sample["poi_id"]) + "/content?language=" + urllib.parse.quote(sample["language"]))
                    require(content.get("text") and (content.get("audio") or {}).get("id") == sample["audio_id"], "Existing Ready content/audio check failed.")
                for route in ("/", "/admin"):
                    with urllib.request.urlopen(self.config["web_url"] + route, timeout=12) as response:
                        require("text/html" in response.headers.get("Content-Type", ""), "Web/Admin did not return HTML.")
                hostname = containers["api"]["Config"]["Hostname"].replace("'", "''")
                sessions = self.sql_json("SELECT COUNT_BIG(*) AS n FROM sys.dm_exec_sessions WHERE is_user_process=1 "
                                         "AND database_id=DB_ID() AND host_name=N'" + hostname + "' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;")
                require(sessions["n"] > 0, "API has no connection to the protected database.")
                return {"poi_count": len(pois), "accounts": data["accounts"], "ready_translations": data["ready_translations"],
                        "ready_audio": data["ready_audio"], "database": data["database_name"], "sql_volume": self.config["sql_volume"],
                        "api_database_session_verified": True, "generation_requests_sent": 0}
            except (CdError, OSError, ValueError, KeyError, TypeError):
                if time.monotonic() >= deadline:
                    raise CdError("Application/data verification timed out; no generation request was sent.") from None
                time.sleep(3)

    def channel_release(self):
        channel = self.config["registry_prefix"] + "/autodubbing-release:develop"
        run(["docker", "pull", channel], timeout=180)
        # Pin the pulled descriptor image before reading, even if another client moves the tag.
        image = image_info(channel)["Id"]
        container = run(["docker", "create", "--network", "none", "--read-only", image]).decode().strip()
        try:
            archive = run(["docker", "cp", container + ":/release.json", "-"], timeout=30)
            with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
                members = [m for m in tar if m.isfile() and Path(m.name).name == "release.json"]
                require(len(members) == 1 and members[0].size <= 65536, "Invalid release descriptor payload.")
                return validate_release(json.load(tar.extractfile(members[0])), self.config["registry_prefix"])
        finally:
            run(["docker", "rm", container])

    def recover(self):
        pending = self.state.get("pending")
        if pending:
            self.guard(check_app_env=False)
            self.resolved(pending["previous_images"])
            self.up(pending["previous_images"])
            proof = self.probe()
            self.state.pop("pending")
            self.state["current_images"] = pending.get("previous_refs", pending["previous_images"])
            self.state["rejected"] = pending.get("release_id")
            self.save()
            self.event("recovered_interrupted_update", **proof)

    def deploy(self, release, *, exercise=False):
        self.guard()
        data = self.database()
        actual_migrations = [x["MigrationId"] for x in data["migrations"]]
        require(actual_migrations == release["migrations"], "Migration mismatch: back up this machine's database and apply the official migration separately before updating. No migration was executed.")
        previous_images = {key: value["Image"] for key, value in self.containers().items() if key in SERVICES}
        self.resolved(release["images"])
        for name in SERVICES:
            if not exercise:
                run(["docker", "pull", release["images"][name]], timeout=300)
            info = image_info(release["images"][name])
            require(info["Config"].get("Labels", {}).get("org.opencontainers.image.revision") == release["revision"],
                    "Image revision differs from the release manifest.")
        # Verify the baseline BEFORE changing either container.
        self.probe()
        release_id = digest(release)
        previous_refs = dict(self.state["current_images"])
        self.state["pending"] = {"previous_images": previous_images, "previous_refs": previous_refs,
                                 "release_id": None if exercise else release_id}
        self.save()
        self.event("deploying", revision=release["revision"], exercise=exercise)
        try:
            self.up(release["images"])
            proof = self.probe()
            require(running_images_match(release["images"], self.guard()), "Running images differ from the selected release.")
            if exercise:
                raise CdError("Intentional verification failure for the rollback exercise.")
        except Exception:
            try:
                self.guard(check_app_env=False)
                self.resolved(previous_images)
                self.up(previous_images)
                proof = self.probe()
                self.state.pop("pending")
                self.state["current_images"] = previous_refs
                if not exercise:
                    self.state["rejected"] = release_id
                self.save()
                self.event("rollback_exercise_passed" if exercise else "rolled_back", **proof)
            except Exception:
                self.event("needs_attention", message="Rollback did not pass verification; automatic updates remain blocked until recovery succeeds.")
                raise CdError("Rollback needs operator attention; SQL was not changed.") from None
            if not exercise:
                raise CdError("New release failed verification; previous API/web restored.") from None
            return
        self.state.update(current_images=release["images"], previous_images=previous_images,
                          revision=release["revision"], sequence=release["sequence"], rejected=None)
        self.state.pop("pending")
        self.save()
        self.event("deployed", revision=release["revision"], sequence=release["sequence"], **proof)

    def check(self, *, retry_failed=False):
        self.recover()
        self.guard()
        self.wait_database()
        release = self.channel_release()
        release_id = digest(release)
        require(release["sequence"] >= self.state.get("sequence", 0), "Older release was refused.")
        if release["sequence"] == self.state.get("sequence"):
            require(release["revision"] == self.state.get("revision") and release["images"] == self.state["current_images"],
                    "A published release sequence was reused with different images; publish a new workflow run.")
            require(running_images_match(self.state["current_images"], self.containers()),
                    "Running API/web was changed outside CD; review it or use the start command to resume the saved release.")
            self.event("up_to_date", revision=release["revision"])
            return
        if self.state.get("rejected") == release_id and not retry_failed:
            self.event("waiting_for_new_release", message="Previously failed release is not retried automatically.")
            return
        self.deploy(release)

    def start(self):
        sql = inspect(self.config["sql_id"])
        require(any(m.get("Name") == self.config["sql_volume"] and m["Destination"] == "/var/opt/mssql" for m in sql["Mounts"]),
                "Protected SQL volume changed; existing SQL was not started.")
        if not sql["State"]["Running"]:
            run(["docker", "start", self.config["sql_id"]])
        self.wait_database()
        self.recover()
        self.guard(check_app_env=False)
        self.resolved(self.state["current_images"])
        self.up(self.state["current_images"])
        self.event("started_saved_release", **self.probe())


def initialize(args):
    path = args.config.resolve()
    require(not path.exists(), "Host configuration already exists; use status/check instead of reinitializing.")
    require(bool(re.fullmatch(r"ghcr\.io/[a-z0-9_.-]+", args.registry_prefix)), "Expected a GHCR owner prefix.")
    require(args.web_url.startswith(("http://127.0.0.1:", "http://localhost:")), "Use the local web URL, not a public tunnel URL.")
    ids = run(["docker", "ps", "-q", "--filter", "label=com.docker.compose.project=" + args.project]).decode().split()
    items = [inspect(item) for item in ids]
    containers = {x["Config"]["Labels"].get("com.docker.compose.service"): x for x in items}
    require(all(name in containers for name in (*SERVICES, "sqlserver")), "Start the existing API/web/SQL before adopting this host.")
    labels = containers["api"]["Config"]["Labels"]
    files = labels["com.docker.compose.project.config_files"].split(",")
    project_directory = labels["com.docker.compose.project.working_dir"]
    require(all(Path(file).is_file() for file in files), "Existing Compose files must be accessible from this Docker client.")
    env_file = (args.env_file or Path(project_directory) / ".env.team").resolve()
    require(env_file.is_file(), "Specify the existing Compose --env-file; no env file will be created.")
    sql = containers["sqlserver"]
    volumes = [m["Name"] for m in sql["Mounts"] if m["Type"] == "volume" and m["Destination"] == "/var/opt/mssql"]
    require(len(volumes) == 1, "Expected the existing named SQL volume; no new volume will be created.")
    api_env = env_map(containers["api"])
    connection = connection_parts(api_env.get("ConnectionStrings__DefaultConnection", ""))
    database = connection.get("database", connection.get("initial catalog"))
    server = connection.get("server", connection.get("data source", "")).lower().removeprefix("tcp:")
    require(database == "AutoDubbingDb" and server in ("sqlserver", "sqlserver,1433"), "API must already point to the existing Compose sqlserver / AutoDubbingDb.")
    directory = path.parent
    directory.mkdir(parents=True, exist_ok=True)
    current_images = {name: containers[name]["Image"] for name in SERVICES}
    active = directory / "active.compose.json"
    write_json(active, Host.override(current_images))
    config = {"format": 1, "project": args.project, "project_directory": project_directory,
              "compose_files": files, "env_file": str(env_file), "registry_prefix": args.registry_prefix,
              "sql_id": sql["Id"], "sql_volume": volumes[0], "database": database,
              "web_url": args.web_url.rstrip("/"), "health_timeout": 180}
    # Resolve using exactly the existing Compose stack; output stays in memory.
    command = ["docker", "compose", "--project-name", args.project, "--project-directory", project_directory, "--env-file", str(env_file)]
    for file in files:
        command += ["-f", file]
    resolved = json.loads(run(command + ["-f", str(active), "config", "--format", "json"]))
    configured = resolved["services"]["api"]["environment"]
    require(all(str(api_env.get(key)) == str(value) for key, value in configured.items()), "Resolved Compose environment differs from the running API; reconcile it before adoption.")
    require(str(configured.get("ContentPreparation__AcceptNewGeneration")).lower() == "true", "AcceptNewGeneration must already be true.")
    config.update(api_env_keys=sorted(configured), api_env_digest=digest(configured))
    state = {"format": 1, "sequence": 0, "revision": None, "current_images": current_images, "rejected": None}
    write_json(path, config)
    write_json(directory / "state.json", state)
    try:
        host = Host(path)
        proof = host.probe()
        host.event("initialized", project=args.project, **proof)
    except Exception:
        path.unlink(missing_ok=True)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("init", "check", "verify", "status", "release", "exercise", "start"))
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--project", default="autodubbing-team")
    parser.add_argument("--env-file", type=Path)
    parser.add_argument("--registry-prefix", default="ghcr.io/hieu-lp")
    parser.add_argument("--web-url", default="http://127.0.0.1:18081")
    parser.add_argument("--retry-failed", action="store_true")
    args = parser.parse_args()
    host = None
    try:
        with host_lock(args.config.resolve().parent):
            if args.command != "status":
                wait_for_docker()
            if args.command == "init":
                initialize(args)
                return 0
            require(args.config.exists(), "Initialize this existing Docker stack before enabling updates.")
            host = Host(args.config)
            if args.command == "status":
                print(json.dumps({"project": host.config["project"], "database": host.config["database"],
                                  "sql_volume": host.config["sql_volume"], "revision": host.state.get("revision"),
                                  "sequence": host.state.get("sequence"), "pending_recovery": bool(host.state.get("pending")),
                                  "last_event": read_json(host.directory / "status.json")}, indent=2, ensure_ascii=False))
            elif args.command == "verify":
                host.guard()
                host.wait_database()
                host.event("verified", **host.probe())
            elif args.command == "release":
                # Inspect the published pair without installing it or touching SQL.
                release = host.channel_release()
                identities = {}
                for name, reference in release["images"].items():
                    run(["docker", "pull", reference], timeout=300)
                    info = image_info(reference)
                    labels = info["Config"].get("Labels") or {}
                    require(labels.get("org.opencontainers.image.revision") == release["revision"],
                            "Published image revision differs from the descriptor.")
                    source = labels.get("org.opencontainers.image.source", "")
                    require(bool(re.fullmatch(r"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", source)),
                            "Published image source label is missing or invalid.")
                    identities[name] = {"registry_reference": reference, "engine_image_identifier": info["Id"],
                                        "platform": {key: info.get(key) for key in ("Os", "Architecture", "Variant")},
                                        "platform_content_fingerprint": platform_content_identity(info),
                                        "revision": release["revision"], "source": source}
                require(len({item["source"] for item in identities.values()}) == 1,
                        "Published API/web source labels differ.")
                print(json.dumps({"release": release, "images": identities, "deployment_attempted": False}, indent=2))
            elif args.command == "start":
                host.start()
            elif args.command == "exercise":
                host.recover()
                containers = host.guard()
                revisions = [image_info(containers[x]["Image"])["Config"].get("Labels", {}).get("org.opencontainers.image.revision") for x in SERVICES]
                require(revisions[0] and revisions[0] == revisions[1], "Exercise needs the same revision label on both existing images.")
                release = {"revision": revisions[0], "sequence": 0,
                           "images": {name: containers[name]["Image"] for name in SERVICES},
                           "migrations": [x["MigrationId"] for x in host.database()["migrations"]]}
                host.deploy(release, exercise=True)
            else:
                host.check(retry_failed=args.retry_failed)
        return 0
    except (CdError, OSError, ValueError, KeyError) as error:
        # No raw external error details, credentials, environment or SQL output.
        message = str(error) if isinstance(error, CdError) else "Local updater configuration is invalid or inaccessible."
        if host is not None:
            try:
                host.event("blocked", message=message)
            except OSError:
                pass
        print(json.dumps({"status": "blocked", "message": message}), file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    sys.exit(main())
