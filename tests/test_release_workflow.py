"""Release boundary and failure-path checks without a signing credential."""
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))
import release_metadata
import verify_candidate
import finalize_signed_release


class SourceBoundaryChecks(unittest.TestCase):
    def test_branch_build_cannot_request_signing(self):
        with self.assertRaisesRegex(ValueError, "restricted"):
            release_metadata.metadata("branch", "main", True)

    def test_tag_must_match_the_source_version(self):
        with self.assertRaisesRegex(ValueError, "differs"):
            release_metadata.metadata("tag", "v99.0.0", False)

    def test_matching_tag_still_rejects_a_dirty_checkout(self):
        version = release_metadata.ET.parse(release_metadata.ROOT / "src/Framelet/Framelet.csproj").findtext("PropertyGroup/InformationalVersion")
        with patch.object(release_metadata.subprocess, "check_output", return_value=" M src/Framelet/Program.cs\n"), self.assertRaisesRegex(ValueError, "clean"):
            release_metadata.metadata("tag", "v" + version, True)

    def test_matching_clean_tag_records_the_actual_commit(self):
        version = release_metadata.ET.parse(release_metadata.ROOT / "src/Framelet/Framelet.csproj").findtext("PropertyGroup/InformationalVersion")
        with patch.object(release_metadata.subprocess, "check_output", side_effect=["", "a" * 40 + "\n"]):
            result = release_metadata.metadata("tag", "v" + version, True)
        self.assertEqual(result["source_sha"], "a" * 40)
        self.assertEqual(result["tagged"], "true")


class CandidateSafetyChecks(unittest.TestCase):
    def test_archive_rejects_traversal_absolute_and_windows_paths(self):
        for path in ("../outside", "/absolute", "C:/outside", "root\\outside"):
            stream = io.BytesIO()
            entry = zipfile.ZipInfo(path)
            entry.filename = path
            with zipfile.ZipFile(stream, "w") as bundle: bundle.writestr(entry, b"test")
            with zipfile.ZipFile(stream) as bundle, self.subTest(path=path), self.assertRaisesRegex(ValueError, "Unsafe"):
                verify_candidate.safe_names(bundle)

    def test_archive_rejects_case_collisions_on_windows(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as bundle:
            bundle.writestr("App.exe", b"first"); bundle.writestr("app.exe", b"second")
        with zipfile.ZipFile(stream) as bundle, self.assertRaisesRegex(ValueError, "Duplicate"):
            verify_candidate.safe_names(bundle)

    def test_failed_signature_never_replaces_the_reviewed_installer(self):
        temp_root = SCRIPTS.parent / ".tools/unit-tests"
        temp_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temp_root) as temp:
            self.assertTrue(Path(temp).resolve().is_relative_to(temp_root.resolve()))
            root = Path(temp); candidate = root / "candidate"; signed = root / "signed"
            candidate.mkdir(); signed.mkdir()
            name = "Snipsera-1.0.0-preview.1-setup-win-x64.exe"
            (candidate / name).write_bytes(b"original"); (signed / name).write_bytes(b"untrusted")
            (candidate / "package-results.json").write_text(json.dumps({"version": "1.0.0-preview.1", "artifacts": []}))
            with patch.object(finalize_signed_release.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "signature-check")), self.assertRaises(subprocess.CalledProcessError):
                finalize_signed_release.finalize(candidate, signed, "CN=Expected")
            self.assertEqual((candidate / name).read_bytes(), b"original")


if __name__ == "__main__": unittest.main()
