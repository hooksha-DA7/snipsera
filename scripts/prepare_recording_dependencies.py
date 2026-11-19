"""Prepare only the hash-pinned GIF converter shipped with this Windows build."""
import concurrent.futures
import hashlib
import json
import time
import zipfile
from pathlib import Path, PurePosixPath
from urllib.error import HTTPError
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    with path.open("rb") as stream:
        result = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
        return result.hexdigest()


def verify_archive(archive, source):
    if archive.stat().st_size != source["bytes"] or digest(archive) != source["sha256"]:
        raise ValueError("Encoder SHA256 or size differs from the dependency lock; nothing will execute")


def download_archive(source, archive):
    if not archive.is_file() or archive.stat().st_size != source["bytes"] or digest(archive) != source["sha256"]:
        parts = archive.parent / "parts" / source["sha256"]
        parts.mkdir(parents=True, exist_ok=True)
        step = 1024 * 1024

        def download(index):
            start = index * step
            end = min(source["bytes"] - 1, start + step - 1)
            part = parts / f"{index:04}.part"
            if part.is_file() and part.stat().st_size == end - start + 1:
                return part
            for attempt in range(3):
                try:
                    request = Request(source["url"], headers={"User-Agent": "Framelet-build", "Range": f"bytes={start}-{end}"})
                    with urlopen(request, timeout=30) as response:
                        if response.status != 206 or response.headers.get("Content-Range") != f"bytes {start}-{end}/{source['bytes']}":
                            raise ValueError("The reviewed encoder download has changed")
                        content = response.read()
                    if len(content) != end - start + 1:
                        raise ValueError("Incomplete encoder download")
                    part.write_bytes(content)
                    return part
                except (OSError, ValueError) as error:
                    if isinstance(error, HTTPError):
                        error.close()
                    if (isinstance(error, HTTPError) and error.code == 404) or attempt == 2:
                        raise
                    time.sleep(1)

        download(0)
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool, archive.open("wb") as stream:
            for part in pool.map(download, range((source["bytes"] + step - 1) // step)):
                stream.write(part.read_bytes())
    verify_archive(archive, source)


def read_runtime(archive, source, expected):
    verify_archive(archive, source)
    prefix = source.get("member_prefix")
    with zipfile.ZipFile(archive) as bundle:
        members = {}
        for item in bundle.infolist():
            path = PurePosixPath(item.filename.replace("\\", "/"))
            if path.is_absolute() or ".." in path.parts or ":" in str(path):
                raise ValueError("Unsafe encoder archive path")
            if not item.is_dir():
                if prefix is not None:
                    key = path.name if item.filename == f"{prefix}/{path.name}" else ""
                else:
                    key = "FFmpeg-LICENSE.txt" if path.name == "LICENSE.txt" else path.name if path.parent.name == "bin" else ""
                if key in expected:
                    if key in members:
                        raise ValueError("Ambiguous encoder archive member")
                    members[key] = item
        if bundle.testzip() is not None:
            raise ValueError("Encoder ZIP CRC failure")
        contents = {}
        for name, value in expected.items():
            if prefix is None and name == "ScreenRecorderLib-LICENSE.txt":
                content = (ROOT / ".tools/nuget/screenrecorderlib/7.0.1/LICENSE").read_bytes()
            elif name in members:
                content = bundle.read(members[name])
            else:
                raise ValueError("Missing encoder file: " + name)
            if hashlib.sha256(content).hexdigest() != value:
                raise ValueError("Encoder component hash differs: " + name)
            contents[name] = content
        return contents


def prepare():
    lock = json.loads((ROOT / "recording-dependencies.json").read_text(encoding="utf-8"))["encoder"]
    cache = ROOT / ".tools/recording-encoder"
    runtime = cache / "runtime"
    runtime.mkdir(parents=True, exist_ok=True)
    expected = lock["runtime_files"]
    if not expected or any(not name or PurePosixPath(name).name != name or "\\" in name or ":" in name or name in {".", ".."} for name in expected):
        raise ValueError("Invalid encoder inventory")
    if all((runtime / name).is_file() and digest(runtime / name) == value for name, value in expected.items()):
        print("Verified the pinned recording converter")
        return
    source = lock
    archive = cache / "ffmpeg-8.1-lgpl-verified.zip"
    try:
        download_archive(source, archive)
    except HTTPError as error:
        if error.code != 404 or "fallback" not in lock:
            raise
        source = lock["fallback"]
        archive = cache / "ffmpeg-release-fallback-verified.zip"
        print("Original encoder archive unavailable; verifying the pinned prior release")
        download_archive(source, archive)
    contents = read_runtime(archive, source, expected)
    for name, content in contents.items():
        (runtime / name).write_bytes(content)
    print("Prepared and verified the recording converter")


if __name__ == "__main__":
    prepare()
