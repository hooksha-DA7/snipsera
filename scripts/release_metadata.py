"""Validate the source version and emit safe release-workflow outputs."""
import json
import os
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def metadata(ref_type, ref_name, signing):
    version = ET.parse(ROOT / "src/Framelet/Framelet.csproj").findtext("PropertyGroup/InformationalVersion")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*)?", version): raise ValueError("Invalid project version")
    tagged = ref_type == "tag"
    if tagged and ref_name != "v" + version: raise ValueError("Release tag differs from the project version")
    if signing and not tagged: raise ValueError("Signing is restricted to matching release tags")
    if signing and subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip(): raise ValueError("Signing requires a clean source checkout")
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    return {"version": version, "source_sha": commit, "runtime_folder": f"Snipsera-{version}-win-x64", "tagged": str(tagged).lower()}


if __name__ == "__main__":
    values = metadata(os.environ.get("GITHUB_REF_TYPE", "branch"), os.environ.get("GITHUB_REF_NAME", "main"), os.environ.get("REQUEST_SIGNING", "false").lower() == "true")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            for key, value in values.items(): stream.write(key + "=" + value + "\n")
    print(json.dumps(values, indent=2))
