"""Verify the returned installer and refresh hashes after the last signing pass."""
import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def finalize(candidate, signed_installer, expected_publisher):
    receipt = json.loads((candidate / "package-results.json").read_text(encoding="utf-8"))
    name = f"Snipsera-{receipt['version']}-setup-win-x64.exe"
    source = signed_installer / name
    subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "scripts/verify_signatures.ps1"), "-Files", str(source.resolve()), "-ExpectedPublisher", expected_publisher], check=True)
    runtime = candidate / f"Snipsera-{receipt['version']}-win-x64"
    subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "scripts/verify_signatures.ps1"), "-Directory", str(runtime.resolve()), "-ExpectedPublisher", expected_publisher], check=True)
    shutil.copy2(source, candidate / name)
    for artifact in receipt["artifacts"]:
        path = candidate / artifact["name"]
        artifact.update(bytes=path.stat().st_size, sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    receipt["signing"] = "Installer and Snipsera application binaries have verified timestamped signatures. MSIX remains an unsigned Store submission package."
    receipt["expected_publisher"] = expected_publisher
    (candidate / "package-results.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    (candidate / "SHA256SUMS.txt").write_text("".join(a["sha256"] + "  " + a["name"] + "\n" for a in receipt["artifacts"]), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--signed-installer", type=Path, required=True)
    parser.add_argument("--expected-publisher", required=True)
    args = parser.parse_args()
    finalize(args.candidate, args.signed_installer, args.expected_publisher)
