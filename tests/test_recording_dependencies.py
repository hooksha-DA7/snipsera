"""Verify frozen encoder fallback downloads without executing downloaded files."""
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("recording_dependencies", ROOT / "scripts/prepare_recording_dependencies.py")
DEPS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DEPS)


def sha(content):
    return hashlib.sha256(content).hexdigest()


def archive_bytes(members):
    stream = io.BytesIO()
    with warnings.catch_warnings(), zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED) as bundle:
        warnings.simplefilter("ignore", UserWarning)
        for name, content in members:
            bundle.writestr(name, content)
    return stream.getvalue()


class RangeResponse(io.BytesIO):
    def __init__(self, content):
        super().__init__(content)
        self.status = 206
        self.headers = {"Content-Range": f"bytes 0-{len(content) - 1}/{len(content)}"}


class RecordingDependencyChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.cache = self.root / ".tools/recording-encoder"
        self.runtime = self.cache / "runtime"
        self.runtime.mkdir(parents=True)
        self.contents = {"ffmpeg.exe": b"synthetic encoder", "avcodec-62.dll": b"synthetic codec",
                         "FFmpeg-LICENSE.txt": b"encoder licence", "ScreenRecorderLib-LICENSE.txt": b"recorder licence"}
        self.prefix = "Snipsera-reviewed/encoder"
        self.members = [(f"{self.prefix}/{name}", value) for name, value in self.contents.items()]
        self.fallback_content = archive_bytes(self.members)
        self.primary_content = archive_bytes([
            ("ffmpeg/bin/ffmpeg.exe", self.contents["ffmpeg.exe"]),
            ("ffmpeg/bin/avcodec-62.dll", self.contents["avcodec-62.dll"]),
            ("ffmpeg/LICENSE.txt", self.contents["FFmpeg-LICENSE.txt"])])
        self.lock = self.source(self.primary_content, "https://example.invalid/original.zip")
        self.lock["runtime_files"] = {name: sha(content) for name, content in self.contents.items()}
        self.lock["fallback"] = self.source(self.fallback_content, "https://example.invalid/reviewed.zip")
        self.lock["fallback"]["member_prefix"] = self.prefix
        self.write_lock()
        licence = self.root / ".tools/nuget/screenrecorderlib/7.0.1/LICENSE"
        licence.parent.mkdir(parents=True)
        licence.write_bytes(self.contents["ScreenRecorderLib-LICENSE.txt"])
        self.addCleanup(patch.stopall)
        patch.object(DEPS, "ROOT", self.root).start()
        patch.object(DEPS.time, "sleep").start()

    def source(self, content, url):
        return {"url": url, "bytes": len(content), "sha256": sha(content)}

    def write_lock(self):
        (self.root / "recording-dependencies.json").write_text(json.dumps({"encoder": self.lock}), encoding="utf-8")

    def fallback_archive(self, members=None, content=None):
        content = content if content is not None else archive_bytes(self.members if members is None else members)
        path = self.cache / "fixture.zip"
        path.write_bytes(content)
        source = self.source(content, self.lock["fallback"]["url"])
        source["member_prefix"] = self.prefix
        return path, source

    def read_fallback(self, members=None, content=None):
        archive, source = self.fallback_archive(members, content)
        return DEPS.read_runtime(archive, source, self.lock["runtime_files"])

    def unavailable_primary(self, request, **kwargs):
        if request.full_url == self.lock["url"]:
            raise HTTPError(request.full_url, 404, "Not Found", {}, None)
        self.assertEqual(request.full_url, self.lock["fallback"]["url"])
        return RangeResponse(self.fallback_content)

    def test_primary_404_downloads_frozen_fallback_and_verifies_components(self):
        with patch.object(DEPS, "urlopen", side_effect=self.unavailable_primary) as network:
            DEPS.prepare()
        self.assertEqual(network.call_count, 2)
        self.assertEqual({path.name: path.read_bytes() for path in self.runtime.iterdir()}, self.contents)

    def test_available_primary_does_not_download_fallback(self):
        with patch.object(DEPS, "urlopen", return_value=RangeResponse(self.primary_content)) as network:
            DEPS.prepare()
        self.assertEqual(network.call_count, 1)
        self.assertEqual(network.call_args.args[0].full_url, self.lock["url"])
        self.assertEqual((self.runtime / "ffmpeg.exe").read_bytes(), self.contents["ffmpeg.exe"])

    def test_verified_runtime_cache_needs_no_download(self):
        for name, content in self.contents.items():
            (self.runtime / name).write_bytes(content)
        with patch.object(DEPS, "urlopen", side_effect=AssertionError("Unexpected download")):
            DEPS.prepare()

    def test_corrupt_runtime_is_repaired_from_verified_primary_archive_cache(self):
        (self.runtime / "ffmpeg.exe").write_bytes(b"damaged")
        (self.cache / "ffmpeg-8.1-lgpl-verified.zip").write_bytes(self.primary_content)
        with patch.object(DEPS, "urlopen", side_effect=AssertionError("Unexpected download")):
            DEPS.prepare()
        self.assertEqual((self.runtime / "ffmpeg.exe").read_bytes(), self.contents["ffmpeg.exe"])

    def test_verified_fallback_cache_is_reused_after_primary_404(self):
        (self.cache / "ffmpeg-release-fallback-verified.zip").write_bytes(self.fallback_content)
        with patch.object(DEPS, "urlopen", side_effect=self.unavailable_primary) as network:
            DEPS.prepare()
        self.assertEqual(network.call_count, 1)

    def test_corrupt_fallback_cache_is_downloaded_again(self):
        (self.cache / "ffmpeg-release-fallback-verified.zip").write_bytes(b"x" * len(self.fallback_content))
        with patch.object(DEPS, "urlopen", side_effect=self.unavailable_primary) as network:
            DEPS.prepare()
        self.assertEqual(network.call_count, 2)
        self.assertEqual(DEPS.digest(self.cache / "ffmpeg-release-fallback-verified.zip"), self.lock["fallback"]["sha256"])

    def test_primary_integrity_failure_does_not_switch_sources(self):
        self.lock["sha256"] = "0" * 64
        self.write_lock()
        with patch.object(DEPS, "urlopen", return_value=RangeResponse(self.primary_content)) as network:
            with self.assertRaisesRegex(ValueError, "SHA256"):
                DEPS.prepare()
        self.assertEqual(network.call_count, 1)
        self.assertEqual(list(self.runtime.iterdir()), [])

    def test_primary_non_404_error_does_not_switch_sources(self):
        error = HTTPError(self.lock["url"], 403, "Forbidden", {}, None)
        with patch.object(DEPS, "urlopen", side_effect=error) as network:
            with self.assertRaises(HTTPError):
                DEPS.prepare()
        self.assertEqual(network.call_count, 3)
        self.assertTrue(all(call.args[0].full_url == self.lock["url"] for call in network.call_args_list))

    def test_downloaded_fallback_hash_mismatch_does_not_write_runtime(self):
        self.lock["fallback"]["sha256"] = "0" * 64
        self.write_lock()
        with patch.object(DEPS, "urlopen", side_effect=self.unavailable_primary):
            with self.assertRaisesRegex(ValueError, "SHA256"):
                DEPS.prepare()
        self.assertEqual(list(self.runtime.iterdir()), [])

    def test_archive_size_and_hash_are_verified_before_reading_members(self):
        archive, source = self.fallback_archive()
        for field, value in (("bytes", source["bytes"] + 1), ("sha256", "0" * 64)):
            with self.subTest(field=field), patch.object(DEPS.zipfile, "ZipFile", side_effect=AssertionError("Read before verification")):
                with self.assertRaisesRegex(ValueError, "SHA256 or size"):
                    DEPS.read_runtime(archive, dict(source, **{field: value}), self.lock["runtime_files"])

    def test_fallback_requires_each_component_under_exact_reviewed_prefix(self):
        for name in self.contents:
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "Missing encoder file"):
                self.read_fallback([(path, data) for path, data in self.members if path != f"{self.prefix}/{name}"])
        wrong_path = [(path.replace(self.prefix + "/", "other/encoder/") if path.endswith("ffmpeg.exe") else path, data)
                      for path, data in self.members]
        with self.assertRaisesRegex(ValueError, "Missing encoder file: ffmpeg.exe"):
            self.read_fallback(wrong_path)

    def test_fallback_rejects_changed_component_even_with_matching_archive_hash(self):
        for name in self.contents:
            changed = [(path, b"changed" if path == f"{self.prefix}/{name}" else data) for path, data in self.members]
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "component hash differs"):
                self.read_fallback(changed)

    def test_no_runtime_files_written_until_every_component_passes(self):
        self.lock["runtime_files"]["ScreenRecorderLib-LICENSE.txt"] = "0" * 64
        self.write_lock()
        with patch.object(DEPS, "urlopen", side_effect=self.unavailable_primary):
            with self.assertRaisesRegex(ValueError, "component hash differs"):
                DEPS.prepare()
        self.assertEqual(list(self.runtime.iterdir()), [])

    def test_unsafe_paths_anywhere_in_archive_are_rejected(self):
        for path in ("../escape", "/absolute", "C:/absolute", "folder\\..\\escape", "file:stream"):
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, "Unsafe encoder archive path"):
                self.read_fallback(self.members + [(path, b"unrelated")])

    def test_duplicate_runtime_member_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Ambiguous encoder archive member"):
            self.read_fallback(self.members + [self.members[0]])

    def test_crc_is_checked_for_non_runtime_members_too(self):
        content = archive_bytes(self.members + [("notes.txt", b"unique crc payload")])
        damaged = content.replace(b"unique crc payload", b"broken crc payload")
        with self.assertRaisesRegex(ValueError, "ZIP CRC failure"):
            self.read_fallback(content=damaged)

    def test_inventory_cannot_write_outside_runtime(self):
        for name in ("../escape", "..", "folder\\escape", "C:escape", "/absolute"):
            self.lock["runtime_files"] = {name: sha(b"payload")}
            self.write_lock()
            with self.subTest(name=name), patch.object(DEPS, "urlopen", side_effect=AssertionError("Unexpected download")):
                with self.assertRaisesRegex(ValueError, "Invalid encoder inventory"):
                    DEPS.prepare()

    def test_reviewed_fallback_metadata_matches_official_catalog(self):
        lock = json.loads((ROOT / "recording-dependencies.json").read_text(encoding="utf-8"))["encoder"]
        catalog = json.loads((ROOT / "releases/catalog.json").read_text(encoding="utf-8"))
        release = next(item for item in catalog["releases"] if item["tag"] == "v1.0.6-preview.3")
        asset = next(item for item in release["assets"] if item["name"] == lock["fallback"]["name"])
        for field in ("name", "url", "bytes", "sha256"):
            self.assertEqual(lock["fallback"][field], asset[field])
        self.assertEqual(lock["fallback"]["member_prefix"], "Snipsera-1.0.6-preview.3-win-x64/encoder")
        self.assertEqual(len(lock["runtime_files"]), 11)


if __name__ == "__main__":
    unittest.main()
