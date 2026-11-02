"""Check package identities and source boundaries before invoking Windows tooling."""
import importlib.util
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("package_release", Path(__file__).resolve().parents[1] / "scripts/package_release.py")
PACKAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGE)


class PackagingChecks(unittest.TestCase):
    def test_store_manifest_uses_supplied_identity_and_full_trust_entry_point(self):
        identity = {"name": "Example.Snipsera", "publisher": "CN=example", "publisher_display_name": "Personal & Publisher"}
        manifest = PACKAGE.package_manifest(identity, "1.0.0.0")
        self.assertEqual(manifest.find(f"{{{PACKAGE.FOUNDATION}}}Identity").attrib["Publisher"], "CN=example")
        self.assertEqual(manifest.find(f"{{{PACKAGE.FOUNDATION}}}Applications/{{{PACKAGE.FOUNDATION}}}Application").attrib["Executable"], "Snipsera.exe")
        self.assertEqual(manifest.find(f"{{{PACKAGE.FOUNDATION}}}Properties/{{{PACKAGE.FOUNDATION}}}PublisherDisplayName").text, "Personal & Publisher")

    def test_invalid_or_placeholder_identities_are_rejected(self):
        for identity in ({}, {"name": "REPLACE_FROM_PARTNER_CENTER", "publisher": "CN=example", "publisher_display_name": "Example"}, {"name": "../escape", "publisher": "CN=example", "publisher_display_name": "Example"}):
            with self.subTest(identity=identity), self.assertRaises(ValueError): PACKAGE.package_manifest(identity, "1.0.0.0")

    def test_store_package_declares_native_recorder_runtime(self):
        identity = {"name": "Example.Snipsera", "publisher": "CN=example", "publisher_display_name": "Example"}
        manifest = PACKAGE.package_manifest(identity, "1.0.0.0")
        dependency = manifest.find(f"{{{PACKAGE.FOUNDATION}}}Dependencies/{{{PACKAGE.FOUNDATION}}}PackageDependency")
        self.assertIsNotNone(dependency, "A clean Store installation must receive the native C++ runtime")
        self.assertEqual(dependency.attrib, {"Name": "Microsoft.VCLibs.140.00.UWPDesktop", "MinVersion": "14.0.33321.0", "Publisher": "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"})

    def test_store_version_rules_are_enforced(self):
        identity = {"name": "Example.Snipsera", "publisher": "CN=example", "publisher_display_name": "Example"}
        for version in ("0.7.0.0", "1.0.0.1", "1.0.0-preview.1", "1.65536.0.0"):
            with self.subTest(version=version), self.assertRaises(ValueError): PACKAGE.package_manifest(identity, version)

    def test_source_package_includes_build_assets_and_excludes_private_work(self):
        paths = {p.relative_to(PACKAGE.ROOT).as_posix() for p in PACKAGE.source_files()}
        self.assertTrue({"packaging/Snipsera.iss", "packaging/AppxManifest.xml", "packaging/store-identity.json", "scripts/package_release.py", "src/Framelet/Assets/Snipsera.ico", "LICENSE", "THIRD-PARTY.md"}.issubset(paths))
        self.assertFalse(any(part in {".tools", ".git", "bin", "obj", "design"} for name in paths for part in Path(name).parts))
        self.assertFalse(any(name.startswith("verification/") and name != "verification/verify_packages.py" for name in paths))


if __name__ == "__main__": unittest.main()
