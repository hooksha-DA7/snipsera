"""Read-only checks of the project and PNG produced by the desktop acceptance test."""
import hashlib
import io
import json
import zipfile
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / "verification/runs/phase2-ui-01"
with zipfile.ZipFile(RUN / "annotated.framelet") as archive:
    assert archive.testzip() is None
    assert set(archive.namelist()) == {"project.json", "original.png"}
    project = json.loads(archive.read("project.json"))
    original_bytes = archive.read("original.png")
assert project["Version"] == 1
assert [mark["Kind"] for mark in project["State"]["Marks"]] == ["Arrow", "Text"]
original = Image.open(io.BytesIO(original_bytes)).convert("RGBA")
exported = Image.open(RUN / "annotated.png").convert("RGBA")
assert original.size == exported.size == (809, 567)
different = sum(a != b for a, b in zip(original.get_flattened_data(), exported.get_flattened_data()))
coral = sum(r > 180 and g < 140 and b < 140 and a == 255 for r, g, b, a in exported.get_flattened_data())
assert different > 500 and coral > 500
arrow = project["State"]["Marks"][0]["Points"]
x = round((arrow[0]["X"] + arrow[1]["X"]) / 2)
y = round((arrow[0]["Y"] + arrow[1]["Y"]) / 2)
r, g, b, a = exported.getpixel((x, y))
assert r > 180 and g < 140 and b < 140 and a == 255
history = json.loads((RUN / "data/history.json").read_text())
matches = []
for entry in history:
    source = RUN / "data/captures" / f"{entry['Id']}.png"
    with Image.open(source) as image:
        if image.convert("RGBA").tobytes() == original.tobytes():
            matches.append(entry["Id"])
assert matches, "Saved project no longer contains the unmodified history pixels"
result = {"status": "passed", "dimensions": list(exported.size), "project_marks": 2,
          "changed_export_pixels": different, "coral_annotation_pixels": coral,
          "arrow_position": "passed", "original_history_pixels_preserved": True,
          "source_capture_ids": matches,
          "project_sha256": hashlib.sha256((RUN / "annotated.framelet").read_bytes()).hexdigest(),
          "png_sha256": hashlib.sha256((RUN / "annotated.png").read_bytes()).hexdigest()}
(RUN / "artifact-results.json").write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps(result, indent=2))
