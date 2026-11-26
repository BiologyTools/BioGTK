"""Stage the verified BioGTK 6.8.1 files on the existing draft release."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from urllib.parse import urlencode
from urllib.request import Request, urlopen


REPO = "BiologyTools/BioGTK"
RELEASE = 454733498
TAG = "Release-6.8.1"
COMMIT = "f19fcdf120a0a2b3433cb260fcbfd39abe6a7938"
ORIGINALS = {
    "BioGTK-macos-arm64.dmg": (792650901, "sha256:aa313112b39db18ce31dc2ac291fac83f2fd715dfa52f7c759efaf757912298e"),
    "BioGTK-macos-x64.dmg": (792651043, "sha256:1819f1b72bf506beef0d4255f0f26b6eaafe4985ee3174f932c9105990b53933"),
}
FILENAMES = (
    "BioGTK-macos-arm64.dmg", "BioGTK-macos-x64.dmg",
    "BioGTK-win-x64-Setup.exe", "BioGTK-win-x64.zip",
    "BioGTKApp.6.8.1.linux-arm64.tar.gz", "BioGTKApp.6.8.1.linux-x64.tar.gz",
)
TOKEN = os.environ["GH_TOKEN"]
HEADERS = {"Authorization": f"Bearer {TOKEN}", "Accept": "application/vnd.github+json",
           "X-GitHub-Api-Version": "2022-11-28"}


def api(path, method="GET", data=None):
    request = Request(f"https://api.github.com/repos/{REPO}/{path}",
                      json.dumps(data).encode() if data is not None else None,
                      {**HEADERS, "Content-Type": "application/json"}, method=method)
    with urlopen(request, timeout=30) as response:
        return json.load(response) if response.status != 204 else None


def draft():
    release = api(f"releases/{RELEASE}")
    if not release["draft"] or release["tag_name"] != TAG or release["target_commitish"] not in ("main", COMMIT):
        raise RuntimeError("The expected BioGTK 6.8.1 draft changed.")
    return release


def assets():
    return {item["name"]: item for item in draft()["assets"]}


def digest(file):
    with file.open("rb") as stream:
        return "sha256:" + hashlib.file_digest(stream, "sha256").hexdigest()


def matches(asset, expected):
    return asset is not None and asset["state"] == "uploaded" and asset["digest"] == expected


def upload(file, staged_name, expected):
    endpoint = f"https://uploads.github.com/repos/{REPO}/releases/{RELEASE}/assets?{urlencode({'name': staged_name})}"
    with tempfile.TemporaryDirectory() as scratch:
        response = Path(scratch) / "upload.json"
        for attempt in range(4):
            existing = assets().get(staged_name)
            if matches(existing, expected):
                return existing
            if existing:
                if existing["state"] != "starter" or existing["size"] != 0:
                    raise ValueError(f"Unexpected staged asset {staged_name}")
                api(f"releases/assets/{existing['id']}", "DELETE")
            result = subprocess.run(
                ["curl", "--silent", "--show-error", "--connect-timeout", "20", "--max-time", "240",
                 "--output", response, "--write-out", "%{http_code}", "--request", "POST",
                 "--header", f"Authorization: Bearer {TOKEN}",
                 "--header", "Content-Type: application/octet-stream",
                 "--data-binary", f"@{file}", endpoint],
                text=True, stdout=subprocess.PIPE,
            )
            status = result.stdout.strip()
            if status == "201":
                created = json.loads(response.read_text())
                if matches(created, expected):
                    print(f"Uploaded and verified {file.name}", flush=True)
                    return created
            if status not in {"", "000", "201", "408", "422", "429", "500", "502", "503", "504"}:
                raise RuntimeError(f"{file.name} upload returned HTTP {status}")
            time.sleep(3 * (attempt + 1))
    existing = assets().get(staged_name)
    if matches(existing, expected):
        return existing
    raise RuntimeError(f"Could not verify uploaded asset {file.name}")


def rename(asset, name):
    draft()
    return api(f"releases/assets/{asset['id']}", "PATCH", {"name": name})


def stage(folder):
    files = [folder / name for name in FILENAMES]
    hashes = {file.name: digest(file) for file in files}
    for name in ORIGINALS:
        expected, filename = (folder / f"{name}.sha256").read_text().split()
        if filename != name or f"sha256:{expected}" != hashes[name]:
            raise ValueError(f"DMG does not match the checksum from the macOS verifier: {name}")

    checksum = folder / "SHA256SUMS.txt"
    checksum.write_text("".join(f"{hashes[name][7:]}  {name}\n" for name in FILENAMES))
    files.append(checksum)
    hashes[checksum.name] = digest(checksum)

    before = assets()
    for name, expected in hashes.items():
        current = before.get(name)
        if current and not matches(current, expected):
            if (current["id"], current["digest"]) != ORIGINALS.get(name):
                raise ValueError(f"The draft contains a different asset named {name}")

    staged = {}
    for file in files:
        if not matches(before.get(file.name), hashes[file.name]):
            pending = f"{file.name}.staged-{hashes[file.name][7:19]}"
            staged[file.name] = upload(file, pending, hashes[file.name])

    for name, asset in staged.items():
        current = assets().get(name)
        if matches(current, hashes[name]):
            continue
        old = None
        if current:
            if (current["id"], current["digest"]) != ORIGINALS.get(name):
                raise ValueError(f"The draft asset changed during staging: {name}")
            old = rename(current, f"{name}.previous-{current['id']}")
        try:
            if not matches(rename(asset, name), hashes[name]):
                raise ValueError(f"Could not verify {name} after attaching it")
        except Exception:
            if old and name not in assets():
                rename(old, name)
            raise

    current = assets()
    if any(not matches(current.get(name), expected) for name, expected in hashes.items()):
        raise RuntimeError("The final draft asset verification failed")

    release = draft()
    if release["target_commitish"] == "main":
        if api("branches/main")["commit"]["sha"] != COMMIT:
            raise RuntimeError("Main moved before the draft could be pinned to the tested revision")
        pinned = api(f"releases/{RELEASE}", "PATCH", {"target_commitish": COMMIT})
        if not pinned["draft"] or pinned["target_commitish"] != COMMIT:
            raise RuntimeError("Could not pin the draft to the tested revision")

    for name, (asset_id, expected) in ORIGINALS.items():
        archived = assets().get(f"{name}.previous-{asset_id}")
        if archived and archived["id"] == asset_id and matches(archived, expected):
            api(f"releases/assets/{asset_id}", "DELETE")

    final = assets()
    if any(not matches(final.get(name), expected) for name, expected in hashes.items()):
        raise RuntimeError("The final draft changed")
    for name, expected in hashes.items():
        print(f"Verified draft asset {name} {expected}", flush=True)


if __name__ == "__main__":
    stage(Path(sys.argv[1]).resolve())
