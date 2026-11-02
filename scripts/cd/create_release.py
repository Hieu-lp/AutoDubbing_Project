"""Build the atomic release descriptor after BOTH application images are published."""
import argparse
import json
import re
import subprocess
from pathlib import Path

from updater import CdError, validate_release


def migration_ids(root):
    result = []
    for path in (root / "AutoDubbing.Data/Migrations").glob("*.Designer.cs"):
        matches = re.findall(r'\[Migration\("([^"]+)"\)\]', path.read_text(encoding="utf-8-sig"))
        if len(matches) != 1:
            raise ValueError("Each migration designer must declare exactly one Migration ID")
        result.extend(matches)
    if not result or len(set(result)) != len(result):
        raise ValueError("Missing or duplicate migration IDs")
    return sorted(result)


def release_sequence(run_number, run_attempt):
    if type(run_number) is not int or run_number < 1 or type(run_attempt) is not int or not 1 <= run_attempt < 1000:
        raise ValueError("Expected a positive workflow run number and attempt between 1 and 999")
    # Rerunning a workflow may rebuild different digests. Never reuse its sequence.
    return run_number * 1000 + run_attempt


def source_revision(root):
    result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True, timeout=20)
    if result.returncode or not re.fullmatch(r"[a-f0-9]{40}", result.stdout.strip()):
        raise ValueError("Cannot establish the checked-out source commit")
    return result.stdout.strip()


def build_release(root, revision, run_number, run_attempt, api_image, web_image):
    if revision != source_revision(root):
        raise ValueError("Release revision differs from the checked-out source commit")
    release = {
        "format": 1, "channel": "develop", "revision": revision,
        "sequence": release_sequence(run_number, run_attempt),
        "images": {"api": api_image, "user-web": web_image},
        "migrations": migration_ids(root),
    }
    # Publisher and updater share the exact same schema/prefix/digest validation.
    prefix = api_image.rsplit("/", 1)[0]
    if not re.fullmatch(r"ghcr\.io/[a-z0-9_.-]+", prefix):
        raise ValueError("Expected a GHCR owner prefix")
    try:
        return validate_release(release, prefix)
    except CdError as error:
        raise ValueError(str(error)) from None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--run-number", required=True, type=int)
    parser.add_argument("--run-attempt", required=True, type=int)
    parser.add_argument("--api-image", required=True)
    parser.add_argument("--web-image", required=True)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    try:
        release = build_release(Path(__file__).resolve().parents[2], args.revision,
                                args.run_number, args.run_attempt, args.api_image, args.web_image)
    except (ValueError, OSError, subprocess.TimeoutExpired) as error:
        parser.error(str(error) if isinstance(error, ValueError) else "Cannot read source revision/migrations")
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "release.json").write_text(json.dumps(release, indent=2) + "\n", encoding="utf-8")
    (args.out / "Dockerfile").write_text(
        'FROM scratch\nCOPY release.json /release.json\nCMD ["/release.json"]\n', encoding="utf-8")
    print("Prepared release " + args.revision + "; both images pinned by digest.")


if __name__ == "__main__":
    main()
