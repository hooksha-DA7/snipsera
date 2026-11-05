"""Build private EXE/MSIX candidates. Signing and Store submission are separate."""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FOUNDATION = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
for prefix, uri in {"": FOUNDATION, "uap": "http://schemas.microsoft.com/appx/manifest/uap/windows10", "uap10": "http://schemas.microsoft.com/appx/manifest/uap/windows10/10", "rescap": "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities", "desktop": "http://schemas.microsoft.com/appx/manifest/desktop/windows10"}.items():
    ET.register_namespace(prefix, uri)


def source_files():
    files = {ROOT / name for name in ("README.md", "CHANGELOG.md", "RELEASES.md", "BUILD-STATUS.md", "LICENSE", "THIRD-PARTY.md", "PRIVACY.md", "SUPPORT.md", "global.json", "recording-dependencies.json", "Directory.Build.props", "NuGet.Config", "build.ps1", "test.ps1", "publish.ps1", ".gitignore", "verification/verify_packages.py")}
    extensions = {".cs", ".csproj", ".manifest", ".png", ".ico", ".json", ".md", ".txt", ".rtf", ".py", ".ps1", ".xml", ".iss", ".yml", ".yaml"}
    for tree in ("src", "tests", "scripts", "packaging", "docs", "releases", ".github/workflows"):
        files.update(p for p in (ROOT / tree).rglob("*") if p.is_file() and p.suffix in extensions and not {"bin", "obj", "__pycache__"}.intersection(p.relative_to(ROOT).parts))
    return sorted(files)


def archive(folder, destination):
    entries = [{"path": p.relative_to(folder).as_posix(), "sha256": hashlib.sha256(p.read_bytes()).hexdigest(), "bytes": p.stat().st_size} for p in sorted(folder.rglob("*")) if p.is_file()]
    (folder / "manifest-sha256.json").write_text(json.dumps(entries, indent=2) + "\n", encoding="utf-8")
    with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(folder.rglob("*")):
            if path.is_file(): bundle.write(path, folder.name + "/" + path.relative_to(folder).as_posix())


def package_manifest(identity, version):
    if not re.fullmatch(r"[1-9]\d{0,4}(?:\.\d{1,5}){3}", version) or any(int(p) > 65535 for p in version.split(".")) or not version.endswith(".0"):
        raise ValueError("Store MSIX version needs four numbers, a nonzero major version and a zero fourth field")
    if not re.fullmatch(r"[A-Za-z0-9.-]{3,50}", identity.get("name", "")) or not identity.get("publisher", "").startswith("CN=") or not identity.get("publisher_display_name", "").strip():
        raise ValueError("Use the exact Name, Publisher and PublisherDisplayName from Partner Center")
    if any("REPLACE" in str(value) for value in identity.values()): raise ValueError("Placeholder identity")
    manifest = ET.parse(ROOT / "packaging/AppxManifest.xml")
    manifest.find(f"{{{FOUNDATION}}}Identity").attrib.update(Name=identity["name"], Publisher=identity["publisher"], Version=version)
    manifest.find(f"{{{FOUNDATION}}}Properties/{{{FOUNDATION}}}PublisherDisplayName").text = identity["publisher_display_name"]
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--store-identity", type=Path, help="Private JSON: name, publisher, publisher_display_name")
    group.add_argument("--development-msix", action="store_true", help="Test identity; never submit to Store")
    parser.add_argument("--output", type=Path, required=True, help="Fresh folder; never overwrites")
    parser.add_argument("--signed-apps", type=Path, help="Verified SignPath application binaries to package")
    parser.add_argument("--expected-publisher", help="Exact approved certificate subject for signed application files")
    parser.add_argument("--inno", type=Path, default=ROOT / ".tools/packaging/inno/ISCC.exe")
    parser.add_argument("--makeappx", type=Path, default=ROOT / ".tools/packaging/windows-sdk-buildtools/bin/10.0.28000.0/x64/makeappx.exe")
    args = parser.parse_args()
    if bool(args.signed_apps) != bool(args.expected_publisher): parser.error("Signed applications require the expected certificate subject")
    for tool in (args.inno, args.makeappx):
        if not tool.is_file(): parser.error("Packaging tool missing: " + str(tool))
    project = ET.parse(ROOT / "src/Framelet/Framelet.csproj")
    version = project.findtext("PropertyGroup/InformationalVersion")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*)?", version): parser.error("Invalid project version")
    msix_version = project.findtext("PropertyGroup/Version") + ".0"
    details = {"name": "HookshaDA7.Snipsera.Development", "publisher": "CN=Snipsera Development", "publisher_display_name": "Snipsera Development"} if args.development_msix else json.loads(args.store_identity.read_text(encoding="utf-8-sig"))
    manifest = package_manifest(details, msix_version)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    runtime = output / f"Snipsera-{version}-win-x64"
    dotnet = ROOT / ".tools/dotnet/dotnet.exe"
    if not dotnet.exists(): dotnet = Path(shutil.which("dotnet") or "dotnet")
    env = os.environ.copy()
    env.update(DOTNET_CLI_HOME=str(ROOT / ".tools/dotnet-home"), NUGET_PACKAGES=str(ROOT / ".tools/nuget"), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    subprocess.run([str(dotnet), "publish", str(ROOT / "src/Framelet/Framelet.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:DebugType=None", "-p:DebugSymbols=false", "--output", str(runtime), "--nologo"], cwd=ROOT, env=env, check=True)
    if args.signed_apps:
        subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "scripts/verify_signatures.ps1"), "-Directory", str(args.signed_apps.resolve()), "-ExpectedPublisher", args.expected_publisher], check=True)
        for name in ("Snipsera.exe", "Snipsera.dll"): shutil.copy2(args.signed_apps / name, runtime / name)
    for name in ("README.md", "CHANGELOG.md", "RELEASES.md", "BUILD-STATUS.md", "LICENSE", "THIRD-PARTY.md", "PRIVACY.md", "SUPPORT.md", "recording-dependencies.json"):
        shutil.copy2(ROOT / name, runtime / name)
    shutil.copytree(ROOT / "docs", runtime / "docs")
    source = output / f"Snipsera-{version}-source"
    source.mkdir()
    for path in source_files():
        target = source / path.relative_to(ROOT); target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(path, target)
    for folder in (source, runtime): archive(folder, output / (folder.name + ".zip"))
    subprocess.run([str(args.inno.resolve()), f"/DAppDir={runtime}", f"/DReleaseVersion={version}", f"/DOutputDir={output}", str(ROOT / "packaging/Snipsera.iss")], check=True)
    msix_root = output / "msix-staging"
    shutil.copytree(runtime, msix_root)
    (msix_root / "Assets").mkdir()
    for size in (44, 50, 150): shutil.copy2(ROOT / f"src/Framelet/Assets/Snipsera-{size}.png", msix_root / f"Assets/Snipsera-{size}.png")
    manifest.write(msix_root / "AppxManifest.xml", encoding="utf-8", xml_declaration=True)
    suffix = "development" if args.development_msix else "store-submission"
    msix = output / f"Snipsera-{version}-{suffix}-x64.msix"
    subprocess.run([str(args.makeappx.resolve()), "pack", "/d", str(msix_root), "/p", str(msix), "/h", "SHA256"], check=True)
    files = sorted(p for p in output.iterdir() if p.suffix in {".exe", ".msix", ".zip"})
    artifacts = [{"name": p.name, "bytes": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in files]
    (output / "SHA256SUMS.txt").write_text("".join(a["sha256"] + "  " + a["name"] + "\n" for a in artifacts), encoding="utf-8")
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip() if (ROOT / ".git").exists() else None
    receipt = {"version": version, "msix_version": msix_version, "source_commit": commit, "source_ref": os.environ.get("GITHUB_REF"), "identity_kind": suffix, "identity": details, "signing": "Application files signed; installer and MSIX not yet signed." if args.signed_apps else "Unsigned candidates; SignPath and Store certification pending.", "acceptance": "Package creation only; installation, upgrade, uninstall and packaged runtime acceptance pending.", "artifacts": artifacts}
    (output / "package-results.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__": main()
