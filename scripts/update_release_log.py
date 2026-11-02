"""Render the changelog, release notes and download links from the release catalog."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
START = "<!-- FRAMELET_DOWNLOADS_START -->"
END = "<!-- FRAMELET_DOWNLOADS_END -->"


def download_block(catalog):
    entries = {item["tag"]: item for item in catalog["releases"]}
    baseline = entries[catalog["verified_baseline"]]
    preview = entries.get(catalog.get("latest_preview"))
    windows = next(asset for asset in baseline["assets"] if asset["role"] == "windows-x64")
    return (START + "\n"
            + f"[Download for Windows ({baseline['version']})]({windows['url']}) · "
            + (f"[Try the preview ({preview['version']})]({preview['release_url']}) · " if preview else "")
            + "[All releases](RELEASES.md)\n" + END)


def change_sections(entry):
    lines = []
    for heading in ("Added", "Improved", "Fixed"):
        items = entry["changes"].get(heading, [])
        if items:
            lines += [f"### {heading}", ""]
            lines += ["- " + item for item in items]
            lines.append("")
    return lines


def download_links(catalog, entry):
    assets = {asset["role"]: asset for asset in entry["assets"]}
    sums = f"https://github.com/{catalog['repository']}/releases/download/{entry['tag']}/SHA256SUMS.txt"
    return (f"[Windows ZIP]({assets['windows-x64']['url']}) · "
            + f"[Source ZIP]({assets['source']['url']}) · [Checksums]({sums})")


def release_body(catalog, entry):
    lines = [entry["intro"], ""]
    lines += change_sections(entry)
    if entry.get("notice"):
        lines += [entry["notice"], ""]
    lines += ["### Download", "", download_links(catalog, entry), "",
              "Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.", ""]
    return lines


def render_notes(catalog, entry):
    return "\n".join([f"# Framelet {entry['version']}: {entry['title']}", "",
                       f"{entry['build_date']} · {entry['kind']}", "",
                       *release_body(catalog, entry)])


def render(catalog):
    entries = {item["tag"]: item for item in catalog["releases"]}
    baseline = entries[catalog["verified_baseline"]]
    lines = ["# Releases", "",
             f"Download [{baseline['version']}]({baseline['release_url']}) for the latest release. Earlier versions and their changes are listed below.", ""]
    for entry in reversed(catalog["releases"]):
        lines += [f"## {entry['version']}: {entry['title']}", "",
                  f"{entry['build_date']} · {entry['kind']} · [Release page]({entry['release_url']})", "",
                  *release_body(catalog, entry)]
    return "\n".join(lines)


def render_changelog(catalog):
    lines = ["# Changelog", "", "[Downloads and release notes](RELEASES.md)", ""]
    if catalog.get("unreleased"):
        lines += [f"## {catalog['unreleased']['version']} — Unreleased", "", catalog["unreleased"]["intro"], "", *change_sections(catalog["unreleased"])]
    for entry in reversed(catalog["releases"]):
        lines += [f"## {entry['version']} — {entry['build_date']}", "",
                  f"{entry['kind']}. {entry['intro']}", "", *change_sections(entry)]
        if entry.get("notice"):
            lines += [entry["notice"], ""]
    return "\n".join(lines)


def update_readme(readme, catalog):
    if readme.count(START) != 1 or readme.count(END) != 1 or readme.index(END) < readme.index(START):
        raise ValueError("README download markers are missing or ambiguous")
    return readme[:readme.index(START)] + download_block(catalog) + readme[readme.index(END) + len(END):]


def main():
    catalog = json.loads((ROOT / "releases/catalog.json").read_text(encoding="utf-8"))
    assert catalog["schema_version"] == 1
    assert len({item["tag"] for item in catalog["releases"]}) == len(catalog["releases"])
    (ROOT / "RELEASES.md").write_text(render(catalog), encoding="utf-8", newline="\n")
    (ROOT / "CHANGELOG.md").write_text(render_changelog(catalog), encoding="utf-8", newline="\n")
    for entry in catalog["releases"]:
        (ROOT / entry["notes"]).write_text(render_notes(catalog, entry), encoding="utf-8", newline="\n")
    raw_readme = (ROOT / "README.md").read_bytes()
    line_ending = "\r\n" if b"\r\n" in raw_readme else "\n"
    readme = raw_readme.decode("utf-8").replace("\r\n", "\n")
    (ROOT / "README.md").write_text(update_readme(readme, catalog), encoding="utf-8", newline=line_ending)
    print(f"Updated changelog, notes and download links for {len(catalog['releases'])} releases")


if __name__ == "__main__":
    main()
