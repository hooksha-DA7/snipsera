"""Render the public download log from curated release metadata; no network calls."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
START = "<!-- FRAMELET_DOWNLOADS_START -->"
END = "<!-- FRAMELET_DOWNLOADS_END -->"


def download_block(catalog):
    entries = {item["tag"]: item for item in catalog["releases"]}
    baseline = entries[catalog["verified_baseline"]]
    preview = entries[catalog["latest_preview"]]
    return (START + "\n**Downloads:** "
            + f"[last locally verified build ({baseline['version']})]({baseline['release_url']}) · "
            + f"[latest feature preview ({preview['version']})]({preview['release_url']}) · "
            + "[all versions and download log](RELEASES.md).\n" + END)


def render(catalog):
    repo = catalog["repository"]
    entries = {item["tag"]: item for item in catalog["releases"]}
    baseline = entries[catalog["verified_baseline"]]
    preview = entries[catalog["latest_preview"]]
    lines = ["# Framelet download log", "", "A chronological record of the app, with the newest entries first. Build dates come from the original local changelogs; GitHub publication dates reflect the later import. Historical source commits were reconstructed from archived source ZIPs, not backdated.", "",
             f"- **Last locally verified capture/annotation baseline:** [{baseline['version']}]({baseline['release_url']}).",
             f"- **Latest experimental feature preview:** [{preview['version']}]({preview['release_url']}).",
             "- Windows ZIPs require .NET 10 Desktop Runtime x64. Extract the full folder, keep its files together and quit earlier copies before opening Framelet.exe.",
             "- Previews remain unverified for native desktop acceptance while desktop testing is paused. Downloading a preview does not make it a stable release.", "",
             "| Build date | Version | Changes | Status | Downloads |", "| --- | --- | --- | --- | --- |"]
    for entry in reversed(catalog["releases"]):
        assets = {a["role"]: a for a in entry["assets"]}
        sums = f"https://github.com/{repo}/releases/download/{entry['tag']}/SHA256SUMS.txt"
        links = f"[Windows ZIP]({assets['windows-x64']['url']}) · [Source ZIP]({assets['source']['url']}) · [SHA256]({sums})"
        lines.append(f"| {entry['build_date']} | [{entry['version']}]({entry['release_url']}) | {entry['title']} | {entry['status']} | {links} |")
    lines += ["", "## Release notes", ""]
    for entry in reversed(catalog["releases"]):
        lines += [f"### {entry['build_date']} — {entry['version']}: {entry['title']}", ""]
        lines += ["- " + bullet for bullet in entry["summary"]]
        lines += ["", entry["verification"], "", f"[Full release and downloads]({entry['release_url']}) · [Archived notes]({entry['notes']})", ""]
    return "\n".join(lines)


if __name__ == "__main__":
    catalog = json.loads((ROOT / "releases/catalog.json").read_text(encoding="utf-8"))
    assert catalog["schema_version"] == 1
    assert len({item["tag"] for item in catalog["releases"]}) == len(catalog["releases"])
    (ROOT / "RELEASES.md").write_text(render(catalog), encoding="utf-8", newline="\n")
    raw_readme = (ROOT / "README.md").read_bytes()
    line_ending = "\r\n" if b"\r\n" in raw_readme else "\n"
    readme = raw_readme.decode("utf-8").replace("\r\n", "\n")
    if readme.count(START) != 1 or readme.count(END) != 1 or readme.index(END) < readme.index(START):
        raise ValueError("README download markers are missing or ambiguous")
    readme = readme[:readme.index(START)] + download_block(catalog) + readme[readme.index(END) + len(END):]
    (ROOT / "README.md").write_text(readme, encoding="utf-8", newline=line_ending)
    print(f"Updated RELEASES.md and README downloads with {len(catalog['releases'])} versions")
