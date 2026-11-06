"""Check the exact icon/Store resources before packaging."""
import importlib.util
import struct
import hashlib
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/Framelet/Assets"
SPEC = importlib.util.spec_from_file_location("brand_package", ROOT / "scripts/package_release.py")
PACKAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGE)


def dimensions(data):
    if data[:8] != b"\x89PNG\r\n\x1a\n": raise ValueError("Not a PNG")
    return struct.unpack(">II", data[16:24])


class BrandingChecks(unittest.TestCase):
    def test_ico_frames_use_the_exact_approved_native_images(self):
        for name, sizes in (("Snipsera.ico", (16, 20, 24, 32, 48, 64, 128, 256)), ("Snipsera-Tray.ico", (16, 20, 24, 32, 40, 48, 64))):
            with self.subTest(icon=name):
                data = (ASSETS / name).read_bytes()
                self.assertEqual(struct.unpack_from("<HHH", data), (0, 1, len(sizes)))
                offset = 6 + 16 * len(sizes)
                for index, size in enumerate(sizes):
                    w, h, colours, reserved, planes, bits, length, start = struct.unpack_from("<BBBBHHII", data, 6 + index * 16)
                    self.assertEqual((w or 256, h or 256, colours, reserved, planes, bits, start), (size, size, 0, 0, 1, 32, offset))
                    block = data[start:start + length]
                    prefix = "Snipsera-Tray-" if name == "Snipsera-Tray.ico" else "Snipsera-"
                    self.assertEqual(block, (ASSETS / f"{prefix}{size}.png").read_bytes())
                    self.assertEqual(dimensions(block), (size, size))
                    offset += length
                self.assertEqual(offset, len(data))

    def test_store_images_include_scale_and_unplated_theme_variants(self):
        included = {p.name for p in PACKAGE.store_images()}
        for base in (44, 50, 150):
            for scale in (200, 400):
                name = f"Snipsera-{base}.scale-{scale}.png"
                self.assertIn(name, included)
                self.assertEqual(dimensions((ASSETS / name).read_bytes()), (base * scale // 100,) * 2)
        for size in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
            expected = (ASSETS / f"Snipsera-{size}.png").read_bytes()
            for form in ("", "_altform-unplated", "_altform-lightunplated"):
                name = f"Snipsera-44.targetsize-{size}{form}.png"
                self.assertIn(name, included)
                self.assertEqual((ASSETS / name).read_bytes(), expected)
        self.assertFalse(any("Tray" in name or "master" in name for name in included))

    def test_brand_sources_ship_without_private_design_records(self):
        included = {p.relative_to(ROOT).as_posix() for p in PACKAGE.source_files()}
        for name in ("master.png", "tray.json", "tray-16.svg", "tray-20.svg", "tray-24.svg", "tray-32.svg"):
            self.assertIn("src/Framelet/Assets/Brand/" + name, included)
        self.assertFalse(any(name.startswith((".tools/", "design/")) for name in included))

    def test_approved_native_tray_png_bytes_are_pinned(self):
        approved = json.loads((ASSETS / "Brand/tray.json").read_text(encoding="utf-8"))["approved_png_sha256"]
        self.assertEqual(set(approved), {"16", "20", "24", "32"})
        for size, expected in approved.items():
            self.assertEqual(hashlib.sha256((ASSETS / f"Snipsera-Tray-{size}.png").read_bytes()).hexdigest(), expected)

    def test_resource_index_keeps_all_qualifiers_in_a_single_package(self):
        config = PACKAGE.resource_config(b'<resources><packaging><autoResourcePackage qualifier="Scale"/></packaging><index><default><qualifier name="Language" value="en-US"/></default></index></resources>')
        self.assertIsNone(config.find("packaging"))
        self.assertEqual(config.find("index/default/qualifier").attrib, {"name": "Language", "value": "en-US"})


if __name__ == "__main__": unittest.main()
