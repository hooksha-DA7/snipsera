"""Prepare only the hash-pinned GIF converter shipped with this Windows build."""
import concurrent.futures
import hashlib
import json
import time
import zipfile
from pathlib import Path, PurePosixPath
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    with path.open("rb") as stream:
        result = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
        return result.hexdigest()


def prepare():
    lock = json.loads((ROOT / "recording-dependencies.json").read_text(encoding="utf-8"))["encoder"]
    cache = ROOT / ".tools/recording-encoder"
    runtime = cache / "runtime"
    runtime.mkdir(parents=True, exist_ok=True)
    expected = lock["runtime_files"]
    if any(PurePosixPath(name).name != name or ":" in name for name in expected):
        raise ValueError("Invalid encoder inventory")
    if all((runtime / name).is_file() and digest(runtime / name) == value for name, value in expected.items()):
        print("Verified the pinned recording converter")
        return
    archive = cache / "ffmpeg-8.1-lgpl-verified.zip"
    if not archive.exists() or digest(archive) != lock["sha256"]:
        parts = cache / "parts" / lock["sha256"]
        parts.mkdir(parents=True, exist_ok=True)
        step = 1024 * 1024

        def download(index):
            start = index * step
            end = min(lock["bytes"] - 1, start + step - 1)
            part = parts / f"{index:04}.part"
            if part.is_file() and part.stat().st_size == end - start + 1:
                return part
            for attempt in range(3):
                try:
                    request = Request(lock["url"], headers={"User-Agent": "Framelet-build", "Range": f"bytes={start}-{end}"})
                    with urlopen(request, timeout=30) as response:
                        if response.status != 206 or response.headers.get("Content-Range") != f"bytes {start}-{end}/{lock['bytes']}":
                            raise ValueError("The reviewed encoder download has changed")
                        content = response.read()
                    if len(content) != end - start + 1:
                        raise ValueError("Incomplete encoder download")
                    part.write_bytes(content)
                    return part
                except (OSError, ValueError):
                    if attempt == 2:
                        raise
                    time.sleep(1)

        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool, archive.open("wb") as stream:
            for part in pool.map(download, range((lock["bytes"] + step - 1) // step)):
                stream.write(part.read_bytes())
        if archive.stat().st_size != lock["bytes"] or digest(archive) != lock["sha256"]:
            raise ValueError("Encoder SHA256 differs from the dependency lock; nothing will execute")
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise ValueError("Encoder ZIP CRC failure")
        members = {}
        for item in bundle.infolist():
            path = PurePosixPath(item.filename.replace("\\", "/"))
            if path.is_absolute() or ".." in path.parts or ":" in str(path):
                raise ValueError("Unsafe encoder archive path")
            if not item.is_dir():
                key = "FFmpeg-LICENSE.txt" if path.name == "LICENSE.txt" else path.name if path.parent.name == "bin" else ""
                if key in expected:
                    if key in members:
                        raise ValueError("Ambiguous encoder archive member")
                    members[key] = item
        for name, value in expected.items():
            if name == "ScreenRecorderLib-LICENSE.txt":
                content = (ROOT / ".tools/nuget/screenrecorderlib/7.0.1/LICENSE").read_bytes()
            elif name in members:
                content = bundle.read(members[name])
            else:
                raise ValueError("Missing encoder file: " + name)
            if hashlib.sha256(content).hexdigest() != value:
                raise ValueError("Encoder component hash differs: " + name)
            (runtime / name).write_bytes(content)
    print("Prepared and verified the recording converter")


if __name__ == "__main__":
    prepare()
