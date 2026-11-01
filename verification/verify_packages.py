"""Verify release ZIP integrity, allowlists and parity before a relocated smoke test."""
import argparse
import hashlib
import json
import re
import posixpath
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--version", default="0.4.1")
parser.add_argument("--run", default="windows-fluent-package-01")
ARGS = parser.parse_args()
if not re.fullmatch(r"\d+\.\d+\.\d+", ARGS.version) or not re.fullmatch(r"[A-Za-z0-9_-]+", ARGS.run):
    parser.error("Use a numeric release version and a plain run folder name")
RUN = ROOT / "verification/runs" / ARGS.run


def digest(data):
    return hashlib.sha256(data).hexdigest()


def expected_source():
    files = {"README.md", "CHANGELOG.md", "Directory.Build.props", "NuGet.Config",
             "build.ps1", "test.ps1", "publish.ps1", ".gitignore", "verification/verify_packages.py"}
    files |= {p.relative_to(ROOT).as_posix() for p in (ROOT / "docs").rglob("*") if p.is_file()}
    for tree in ("src", "tests"):
        files |= {p.relative_to(ROOT).as_posix() for p in (ROOT / tree).rglob("*")
                  if p.is_file() and not {"bin", "obj"}.intersection(p.parts)
                  and p.suffix in {".cs", ".csproj", ".manifest"}}
    return files


def verify(flavor):
    stem = f"Framelet-{ARGS.version}-{flavor}"
    archive = ROOT / "dist" / f"{stem}.zip"
    runtime = {"Framelet.exe", "Framelet.dll", "Framelet.deps.json", "Framelet.runtimeconfig.json"}
    docs = {p.relative_to(ROOT).as_posix() for p in (ROOT / "docs").rglob("*") if p.is_file()}
    expected = expected_source() if flavor == "source" else runtime | docs | {"README.md", "CHANGELOG.md"}
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None, "ZIP CRC failure"
        names = [entry.filename.replace("\\", "/") for entry in bundle.infolist()]
        assert len(names) == len(set(n.casefold() for n in names)), "Duplicate ZIP path"
        for name in names:
            path = PurePosixPath(name)
            assert not path.is_absolute() and ".." not in path.parts and ":" not in name, "Unsafe ZIP path"
            assert path.parts[0] == stem, "Unexpected top-level folder"
        data = {n[len(stem) + 1:]: bundle.read(entry)
                for n, entry in zip(names, bundle.infolist()) if not entry.is_dir()}
        assert set(data) == expected | {"manifest-sha256.json"}, "Unexpected package contents"
        manifest = json.loads(data["manifest-sha256.json"].decode("utf-8-sig"))
        assert len(manifest) == len(expected), "Manifest count mismatch"
        assert {entry["path"] for entry in manifest} == expected, "Manifest paths mismatch"
        for entry in manifest:
            content = data[entry["path"]]
            assert digest(content) == entry["sha256"] and len(content) == entry["bytes"], "Manifest hash mismatch"
            original = (ROOT / "src/Framelet/bin/Release/net10.0-windows" / entry["path"]
                        if flavor == "win-x64" and entry["path"] in runtime else ROOT / entry["path"])
            assert content == original.read_bytes(), f"Source/build parity failed: {entry['path']}"
        for name in expected:
            if name.endswith(".md"):
                for link in re.findall(r"\]\(([^)]+)\)", data[name].decode()):
                    if "://" not in link and not link.startswith("#"):
                        target = posixpath.normpath(str(PurePosixPath(name).parent / link.split('#')[0]))
                        assert target in data, f"Broken packaged documentation link: {link}"
        destination = RUN / "extracted"
        bundle.extractall(destination)
    return {"archive": archive.name, "sha256": digest(archive.read_bytes()),
            "bytes": archive.stat().st_size, "files": len(expected) + 1,
            "crc_safe_paths_allowlist_hashes_and_parity": "passed"}


if __name__ == "__main__":
    assert not RUN.exists(), "Use a fresh package verification directory"
    RUN.mkdir(parents=True)
    results = {"archives": [verify("win-x64"), verify("source")], "status": "passed",
               "relocated_launch": "pending"}
    receipt = ROOT / f"verification/package-results-{ARGS.version}.json"
    receipt.write_text(json.dumps(results, indent=2) + "\n")
    print(json.dumps(results, indent=2))
