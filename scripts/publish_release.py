"""Plan or publish one verified catalog entry using authenticated GitHub CLI.

No build, Git push, source rewrite, credential persistence or automatic native acceptance.
"""
import argparse
import concurrent.futures
import hashlib
import json
import re
import shutil
import subprocess
import time
import zipfile
from pathlib import Path, PurePosixPath
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def public_download_digest(url, size):
    """Verify large public downloads in bounded, retryable anonymous ranges."""
    step = 1024 * 1024
    ranged = size > 8 * step

    def read_part(start):
        end = min(size - 1, start + step - 1) if ranged else size - 1
        for attempt in range(3):
            try:
                headers = {"User-Agent": "Framelet-release-verifier"}
                if ranged:
                    headers["Range"] = f"bytes={start}-{end}"
                with urlopen(Request(url, headers=headers), timeout=30) as response:
                    if ranged and (response.status != 206 or response.headers.get("Content-Range") != f"bytes {start}-{end}/{size}"):
                        raise ValueError("Public download range differs")
                    content = response.read(end - start + 2)
                if len(content) != end - start + 1:
                    raise ValueError("Public download length differs")
                return content
            except OSError:
                if attempt == 2:
                    raise
                time.sleep(attempt + 1)

    result = hashlib.sha256()
    count = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=4 if ranged else 1) as pool:
        for content in pool.map(read_part, range(0, size, step) if ranged else [0]):
            result.update(content)
            count += len(content)
    return result.hexdigest(), count


def validate_archive(path, expected):
    data = path.read_bytes()
    if path.name != expected["name"] or digest(data) != expected["sha256"] or len(data) != expected["bytes"]:
        raise ValueError("Archive differs from the reviewed catalog: " + path.name)
    with zipfile.ZipFile(path) as bundle:
        if bundle.testzip() is not None:
            raise ValueError("ZIP CRC failure: " + path.name)
        names = [item.filename.replace("\\", "/") for item in bundle.infolist()]
        if len(names) != len(set(name.casefold() for name in names)):
            raise ValueError("Duplicate ZIP path")
        for name in names:
            part = PurePosixPath(name)
            if part.is_absolute() or ".." in part.parts or ":" in name:
                raise ValueError("Unsafe ZIP path")
        tops = {PurePosixPath(name).parts[0] for name in names}
        if len(tops) != 1:
            raise ValueError("ZIP requires one top-level folder")
        top = next(iter(tops))
        contents = {name[len(top) + 1:]: bundle.read(item) for name, item in zip(names, bundle.infolist()) if not item.is_dir()}
        manifest = json.loads(contents["manifest-sha256.json"].decode("utf-8-sig"))
        if {item["path"] for item in manifest} != set(contents) - {"manifest-sha256.json"}:
            raise ValueError("Manifest paths differ")
        for item in manifest:
            content = contents[item["path"]]
            if digest(content) != item["sha256"] or len(content) != item["bytes"]:
                raise ValueError("Manifest content differs: " + item["path"])


def github(executable, arguments, *, missing_ok=False):
    for attempt in range(3):
        result = subprocess.run([str(executable), *arguments], capture_output=True, text=True, encoding="utf-8", errors="replace")
        transient = any(part in result.stderr.lower() for part in ("connection attempt failed", "connection reset", "i/o timeout", "tls handshake timeout", "wsarecv", "unexpected eof"))
        # API calls here are GETs. Never blindly retry create/upload/edit after an uncertain write result.
        read_only = arguments[0] == "api" and "--method" not in arguments and "-X" not in arguments
        if not result.returncode or not (read_only and transient) or attempt == 2:
            break
        time.sleep(attempt + 1)
    if result.returncode:
        if missing_ok and re.search(r"\bHTTP 404\b", result.stderr):
            return None
        raise RuntimeError("GitHub CLI failed: " + result.stderr.strip())
    return result.stdout


def release_info(executable, repo, tag, *, missing_ok=False):
    text = github(executable, ["api", f"repos/{repo}/releases/tags/{tag}"], missing_ok=True)
    if text is not None:
        return json.loads(text)
    # Drafts may not resolve through the published-release tag endpoint. Owner listings include them.
    pages = json.loads(github(executable, ["api", f"repos/{repo}/releases?per_page=100", "--paginate", "--slurp"]))
    matches = [item for page in pages for item in page if item["tag_name"] == tag]
    if len(matches) > 1:
        raise ValueError("Multiple releases use this tag; inspect them before continuing")
    if matches:
        return matches[0]
    if missing_ok:
        return None
    raise ValueError("Release was not found: " + tag)


def verify_public(entry, release):
    if release["draft"] or release["prerelease"] != entry["prerelease"]:
        raise ValueError("Remote release status differs from the catalog")
    expected = {item["name"]: item for item in entry["assets"]}
    sums = "".join(item["sha256"] + "  " + item["name"] + "\n" for item in entry["assets"]).encode()
    expected["SHA256SUMS.txt"] = {"sha256": digest(sums), "bytes": len(sums)}
    assets = {item["name"]: item for item in release["assets"]}
    if set(assets) != set(expected):
        raise ValueError("Remote release has missing or unexpected assets")
    checked = []
    for name, item in expected.items():
        asset = assets[name]
        # Deliberately anonymous: this proves public downloads work without the maintainer's token.
        downloaded_hash, downloaded_bytes = public_download_digest(asset["browser_download_url"], item["bytes"])
        if downloaded_hash != item["sha256"] or downloaded_bytes != item["bytes"]:
            raise ValueError("Public download hash differs: " + name)
        checked.append({"name": name, "sha256": downloaded_hash, "bytes": downloaded_bytes, "url": asset["browser_download_url"]})
    return {"tag": entry["tag"], "url": release["html_url"], "prerelease": release["prerelease"], "public_downloads": checked}


def publish(args):
    catalog = json.loads((ROOT / "releases/catalog.json").read_text(encoding="utf-8"))
    entry = next((item for item in catalog["releases"] if item["version"] == args.version), None)
    if entry is None or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?", args.version):
        raise ValueError("Add a valid reviewed version to releases/catalog.json first")
    if entry["latest"] and entry["prerelease"]:
        raise ValueError("A prerelease cannot become the verified baseline")
    if not args.verify_only:
        if not args.windows or not args.source:
            raise ValueError("Provide --windows and --source ZIP paths")
        paths = [Path(args.windows).resolve(), Path(args.source).resolve()]
        for path, expected in zip(paths, entry["assets"]):
            validate_archive(path, expected)
    else:
        paths = []
    if not args.publish and not args.verify_only:
        print(json.dumps({"mode": "plan", "repository": catalog["repository"], "release": entry}, indent=2))
        return None
    executable = args.gh or shutil.which("gh") or ROOT / ".tools/github-publication/gh.exe"
    owner = catalog["repository"].split("/")[0]
    identity = json.loads(github(executable, ["api", "user"]))
    if identity["login"].casefold() != owner.casefold():
        raise ValueError("The authenticated GitHub user does not own the personal repository")
    repo = catalog["repository"]
    remote = json.loads(github(executable, ["api", f"repos/{repo}"]))
    if remote["private"] or remote["owner"]["type"] != "User":
        raise ValueError("Expected the public personal repository")
    release = release_info(executable, repo, entry["tag"], missing_ok=True)
    if args.verify_only:
        if release is None:
            raise ValueError("Release has not been published")
        return verify_public(entry, release)
    if release is not None and not release["draft"]:
        return verify_public(entry, release)
    if release is not None and release["author"]["login"].casefold() != owner.casefold():
        raise ValueError("The existing draft was created by a different author; inspect it before resuming")
    if release is not None and not args.resume:
        raise ValueError("A draft already exists; inspect it, then use --resume")
    if release is None:
        command = ["release", "create", entry["tag"], "--repo", repo, "--verify-tag", "--draft", "--latest=false",
                   "--title", f"Framelet {entry['version']} — {entry['title']}", "--notes-file", str(ROOT / entry["notes"])]
        if entry["prerelease"]:
            command.append("--prerelease")
        github(executable, command)
        release = release_info(executable, repo, entry["tag"])
    work = ROOT / ".tools/release-work" / entry["tag"]
    work.mkdir(parents=True, exist_ok=True)
    sums_path = work / "SHA256SUMS.txt"
    sums_path.write_text("".join(item["sha256"] + "  " + item["name"] + "\n" for item in entry["assets"]), encoding="utf-8", newline="\n")
    files = [*paths, sums_path]
    uploaded = {asset["name"]: asset for asset in release["assets"]}
    for path in files:
        expected_hash = digest(path.read_bytes())
        if path.name in uploaded:
            if uploaded[path.name].get("digest") != "sha256:" + expected_hash:
                raise ValueError("Existing draft asset differs; never overwrite it: " + path.name)
        else:
            github(executable, ["release", "upload", entry["tag"], str(path), "--repo", repo])
    for attempt in range(10):
        release = release_info(executable, repo, entry["tag"])
        uploaded = {asset["name"]: asset for asset in release["assets"]}
        if set(uploaded) == {path.name for path in files} and all(uploaded[path.name].get("digest") == "sha256:" + digest(path.read_bytes()) for path in files):
            break
        if attempt == 9:
            raise ValueError("Uploaded draft asset digests are not verified; draft left unpublished")
        time.sleep(1)
    github(executable, ["release", "edit", entry["tag"], "--repo", repo, "--draft=false", "--latest=" + str(entry["latest"]).lower()])
    return verify_public(entry, release_info(executable, repo, entry["tag"]))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--windows")
    parser.add_argument("--source")
    parser.add_argument("--gh")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--publish", action="store_true")
    mode.add_argument("--verify-only", action="store_true")
    parser.add_argument("--resume", action="store_true", help="Resume an inspected matching draft; never overwrite assets")
    result = publish(parser.parse_args())
    if result is not None:
        print(json.dumps(result, indent=2))
