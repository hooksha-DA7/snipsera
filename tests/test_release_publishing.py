"""Offline checks for the actual draft-lookup failure encountered during the historical import."""
import importlib.util
import copy
import json
import io
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("framelet_publisher", Path(__file__).resolve().parents[1] / "scripts/publish_release.py")
PUBLISHER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PUBLISHER)
LOG_SPEC = importlib.util.spec_from_file_location("framelet_release_log", Path(__file__).resolve().parents[1] / "scripts/update_release_log.py")
LOG = importlib.util.module_from_spec(LOG_SPEC)
LOG_SPEC.loader.exec_module(LOG)


class ReleaseLookupChecks(unittest.TestCase):
    def test_large_public_download_hashes_every_range_in_order(self):
        content = bytes(range(251)) * 37000

        def response(request, timeout):
            interval = request.get_header("Range").removeprefix("bytes=")
            start, end = map(int, interval.split("-"))
            stream = io.BytesIO(content[start:end + 1])
            stream.status = 206
            stream.headers = {"Content-Range": f"bytes {start}-{end}/{len(content)}"}
            return stream

        with patch.object(PUBLISHER, "urlopen", side_effect=response):
            self.assertEqual(PUBLISHER.public_download_digest("https://example.test/asset.zip", len(content)), (PUBLISHER.digest(content), len(content)))

    def test_truncated_public_download_is_never_accepted(self):
        with patch.object(PUBLISHER, "urlopen", return_value=io.BytesIO(b"short")):
            with self.assertRaisesRegex(ValueError, "length differs"):
                PUBLISHER.public_download_digest("https://example.test/asset.zip", 100)

    def test_draft_is_found_when_tag_endpoint_returns_404(self):
        draft = {"tag_name": "v0.1.0", "id": 123, "draft": True}
        with patch.object(PUBLISHER, "github", side_effect=[None, json.dumps([[draft]])]) as request:
            self.assertEqual(PUBLISHER.release_info("gh", "owner/app", "v0.1.0"), draft)
            self.assertIn("--paginate", request.call_args.args[1])

    def test_published_release_does_not_need_a_listing(self):
        release = {"tag_name": "v1.0.0", "id": 456, "draft": False}
        with patch.object(PUBLISHER, "github", return_value=json.dumps(release)) as request:
            self.assertEqual(PUBLISHER.release_info("gh", "owner/app", "v1.0.0"), release)
            self.assertEqual(request.call_count, 1)

    def test_missing_release_remains_distinct_from_a_draft(self):
        with patch.object(PUBLISHER, "github", side_effect=[None, "[[]]"]):
            self.assertIsNone(PUBLISHER.release_info("gh", "owner/app", "v2.0.0", missing_ok=True))
        with patch.object(PUBLISHER, "github", side_effect=[None, "[[]]"]):
            with self.assertRaisesRegex(ValueError, "Release was not found"):
                PUBLISHER.release_info("gh", "owner/app", "v2.0.0")

    def test_duplicate_matching_drafts_require_inspection(self):
        drafts = [{"tag_name": "v1.0.0", "id": item, "draft": True} for item in (1, 2)]
        with patch.object(PUBLISHER, "github", side_effect=[None, json.dumps([drafts])]):
            with self.assertRaisesRegex(ValueError, "Multiple releases"):
                PUBLISHER.release_info("gh", "owner/app", "v1.0.0")

    def test_transient_read_failure_is_retried(self):
        failed = subprocess.CompletedProcess([], 1, "", "wsarecv: connection attempt failed")
        passed = subprocess.CompletedProcess([], 0, "{}", "")
        with patch.object(PUBLISHER.subprocess, "run", side_effect=[failed, passed]) as request, patch.object(PUBLISHER.time, "sleep"):
            self.assertEqual(PUBLISHER.github("gh", ["api", "repos/owner/app"]), "{}")
            self.assertEqual(request.call_count, 2)

    def test_uncertain_write_is_not_blindly_retried(self):
        failed = subprocess.CompletedProcess([], 1, "", "wsarecv: connection attempt failed")
        with patch.object(PUBLISHER.subprocess, "run", return_value=failed) as request:
            with self.assertRaises(RuntimeError):
                PUBLISHER.github("gh", ["release", "create", "v1.0.0"])
            self.assertEqual(request.call_count, 1)


class ReleaseLogChecks(unittest.TestCase):
    def setUp(self):
        self.catalog = json.loads((Path(__file__).resolve().parents[1] / "releases/catalog.json").read_text(encoding="utf-8"))

    def test_every_release_has_direct_links_to_its_original_archives(self):
        text = LOG.render(self.catalog)
        for entry in self.catalog["releases"]:
            for asset in entry["assets"]:
                self.assertIn(asset["url"], text)

    def test_readme_downloads_follow_the_catalog_preview_pointer(self):
        updated = copy.deepcopy(self.catalog)
        updated["verified_baseline"] = "v0.5.2"
        updated["latest_preview"] = "v0.6.0-preview.3"
        block = LOG.download_block(updated)
        self.assertIn("0.6.0-preview.3", block)
        self.assertNotIn("0.6.0-preview.4", block)
        self.assertIn("0.5.2", block)

    def test_readme_does_not_offer_an_old_preview_after_stable_promotion(self):
        updated = copy.deepcopy(self.catalog)
        updated["latest_preview"] = None
        baseline = next(entry for entry in updated["releases"] if entry["tag"] == updated["verified_baseline"])
        block = LOG.download_block(updated)
        self.assertIn(f"Download for Windows ({baseline['version']})", block)
        self.assertNotIn("Try the preview", block)
        self.assertIn("[All releases](RELEASES.md)", block)

    def test_public_notes_and_changelog_are_current_with_the_catalog(self):
        root = Path(__file__).resolve().parents[1]
        for entry in self.catalog["releases"]:
            with self.subTest(version=entry["version"]):
                self.assertEqual((root / entry["notes"]).read_text(encoding="utf-8"), LOG.render_notes(self.catalog, entry))
        self.assertEqual((root / "RELEASES.md").read_text(encoding="utf-8"), LOG.render(self.catalog))
        self.assertEqual((root / "CHANGELOG.md").read_text(encoding="utf-8"), LOG.render_changelog(self.catalog))


if __name__ == "__main__":
    unittest.main()
