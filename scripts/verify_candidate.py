"""Verify candidate archives, source parity, MSIX block hashes and optional signatures."""
import argparse
import base64
import hashlib
import json
import posixpath
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path, PurePosixPath
from urllib.parse import unquote
from package_release import ROOT, source_files, store_images, run_makepri, validate_resource_dump


def require(condition, message):
    if not condition: raise ValueError(message)


def safe_names(bundle):
    require(bundle.testzip() is None, "ZIP CRC failure")
    names = [item.orig_filename for item in bundle.infolist()]
    require(len(names) == len({n.casefold() for n in names}), "Duplicate archive path")
    for name in names:
        path = PurePosixPath(name)
        require(not path.is_absolute() and ".." not in path.parts and ":" not in name and "\\" not in name and "\0" not in name, "Unsafe archive path")
    return names


def verify(candidate, require_signed=False, expected_publisher=None):
    receipt = json.loads((candidate / "package-results.json").read_text(encoding="utf-8"))
    version = receipt["version"]
    require(re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*)?", version), "Invalid candidate version")
    if receipt.get("source_commit"):
        head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        require(receipt["source_commit"] == head, "Candidate belongs to a different source commit")
    for artifact in receipt["artifacts"]:
        require(Path(artifact["name"]).name == artifact["name"], "Unsafe artifact filename")
        data = (candidate / artifact["name"]).read_bytes()
        require(len(data) == artifact["bytes"] and hashlib.sha256(data).hexdigest() == artifact["sha256"], "Artifact size/hash differs")
    for flavor in ("source", "win-x64"):
        stem = f"Snipsera-{version}-{flavor}/"
        with zipfile.ZipFile(candidate / (stem[:-1] + ".zip")) as bundle:
            names = safe_names(bundle)
            require(all(n.startswith(stem) for n in names), "Unexpected top-level archive folder")
            files = {n[len(stem):]: bundle.read(n) for n in names if not n.endswith("/")}
            manifest = json.loads(files["manifest-sha256.json"])
            require({a["path"] for a in manifest} == set(files) - {"manifest-sha256.json"} and len(manifest) == len(files) - 1, "Manifest inventory differs")
            for item in manifest:
                data = files[item["path"]]
                require(len(data) == item["bytes"] and hashlib.sha256(data).hexdigest() == item["sha256"], "Manifest content differs")
                if flavor == "source": require(data == (ROOT / item["path"]).read_bytes(), "Source byte parity differs: " + item["path"])
            if flavor == "source": require(set(files) - {"manifest-sha256.json"} == {p.relative_to(ROOT).as_posix() for p in source_files()}, "Source allowlist differs")
            for name, data in files.items():
                if not name.endswith(".md"): continue
                for link in re.findall(r"\]\(([^)]+)\)", data.decode("utf-8-sig")):
                    if "://" in link or link.startswith(("#", "mailto:")): continue
                    target = posixpath.normpath(posixpath.join(posixpath.dirname(name), unquote(link.split("#")[0])))
                    require(target in files, "Broken packaged documentation link: " + link)
    msix = [candidate / a["name"] for a in receipt["artifacts"] if a["name"].endswith(".msix")]
    require(len(msix) == 1, "Expected exactly one MSIX")
    runtime = candidate / f"Snipsera-{version}-win-x64"
    with zipfile.ZipFile(msix[0]) as bundle:
        names = safe_names(bundle)
        require("AppxSignature.p7x" not in names, "Store submission verification expects an unsigned MSIX")
        manifest = ET.fromstring(bundle.read("AppxManifest.xml"))
        identity = manifest.find("{http://schemas.microsoft.com/appx/manifest/foundation/windows10}Identity").attrib
        require(identity == {"Name": receipt["identity"]["name"], "Publisher": receipt["identity"]["publisher"], "Version": receipt["msix_version"], "ProcessorArchitecture": "x64"}, "MSIX identity differs")
        expected = {p.relative_to(runtime).as_posix(): p.read_bytes() for p in runtime.rglob("*") if p.is_file()}
        for image in store_images(): expected["Assets/" + image.name] = image.read_bytes()
        index = receipt.get("resource_index")
        require(index and index.get("name") == "resources.pri", "Candidate has no package resource index receipt")
        pri = bundle.read("resources.pri")
        require(pri.startswith(b"mrm_pri") and len(pri) == index["bytes"] and hashlib.sha256(pri).hexdigest() == index["sha256"], "Package resource index differs")
        makepri = ROOT / ".tools/packaging/windows-sdk-buildtools/bin/10.0.28000.0/x64/makepri.exe"
        require(makepri.is_file(), "MakePri is required to verify the Store icon resource index")
        with tempfile.TemporaryDirectory(prefix="snipsera-pri-check-") as temporary:
            directory = Path(temporary); saved_pri = directory / "resources.pri"; dump = directory / "index.xml"
            saved_pri.write_bytes(pri)
            run_makepri(makepri, "dump", "/if", saved_pri, "/of", dump, "/dt", "detailed", "/o")
            validate_resource_dump(dump, {"AppxManifest.xml"} | {"Assets/" + image.name for image in store_images()})
        expected["resources.pri"] = pri
        require(set(names) == set(expected) | {"AppxManifest.xml", "AppxBlockMap.xml", "[Content_Types].xml"}, "MSIX inventory differs")
        for name, data in expected.items(): require(bundle.read(name) == data, "MSIX payload differs: " + name)
        for file in ET.fromstring(bundle.read("AppxBlockMap.xml")):
            data = bundle.read(file.attrib["Name"].replace("\\", "/"))
            require(len(data) == int(file.attrib["Size"]), "MSIX block-map size differs")
            for index, block in enumerate(b for b in file if b.tag.endswith("}Block")):
                require(base64.b64decode(block.attrib["Hash"]) == hashlib.sha256(data[index * 65536:(index + 1) * 65536]).digest(), "MSIX block hash differs")
            for full_hash in (b for b in file if b.tag.endswith("}FileHash")):
                require(base64.b64decode(full_hash.attrib["Hash"]) == hashlib.sha256(data).digest(), "MSIX file hash differs")
    if require_signed:
        require(expected_publisher, "An approved certificate subject is required")
        subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "scripts/verify_signatures.ps1"), "-Directory", str(runtime.resolve()), "-ExpectedPublisher", expected_publisher], check=True)
        subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "scripts/verify_signatures.ps1"), "-Files", str((candidate / f"Snipsera-{version}-setup-win-x64.exe").resolve()), "-ExpectedPublisher", expected_publisher], check=True)
    print("Candidate hashes, safe paths, source parity, documentation links and MSIX contents/block hashes passed.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--require-signed", action="store_true")
    parser.add_argument("--expected-publisher")
    args = parser.parse_args()
    verify(args.candidate, args.require_signed, args.expected_publisher)
