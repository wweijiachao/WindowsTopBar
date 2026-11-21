"""Pure packaging checks: temporary files and mocked publishing, no app/device operations."""

import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile


SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "package_release.py"
spec = importlib.util.spec_from_file_location("package_release", SCRIPT)
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class PackagingChecks(unittest.TestCase):
    def test_tag_must_match_clean_head(self):
        with patch.object(release, "run", side_effect=["", "v1.2.3"]):
            self.assertEqual(release.release_tag(Path.cwd(), None), "v1.2.3")
        for responses, requested in (
            ([" M README.md"], None),
            (["?? new.cs"], None),
            (["", ""], None),
            (["", "v1.0.0"], "v0.9.0"),
            (["", "v1.0.0\nv1.0"], None),
        ):
            with self.subTest(responses=responses), patch.object(release, "run", side_effect=responses):
                with self.assertRaises(ValueError):
                    release.release_tag(Path.cwd(), requested)
        with patch.object(release, "run", side_effect=["", "v1.0.0\nv1.0"]):
            self.assertEqual(release.release_tag(Path.cwd(), "v1.0.0"), "v1.0.0")

    def test_unsafe_tag_paths_rejected(self):
        for tag in ("../other", "v1/../../other", "C:\\other", "..", "CON", "nul.zip", "v1."):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.validate_tag(tag)

    def test_failure_preserves_previous_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            old = root / "artifacts" / "releases" / "v1.0.0"
            old.mkdir(parents=True)
            (old / "previous.zip").write_bytes(b"old release")
            failure = subprocess.CalledProcessError(1, ["dotnet", "publish"])
            with patch.object(release, "publish", side_effect=[None, failure]):
                with self.assertRaises(subprocess.CalledProcessError):
                    release.package_release(root, "v1.0.0")
            self.assertEqual((old / "previous.zip").read_bytes(), b"old release")
            self.assertEqual(list(old.parent.iterdir()), [old])

    def test_success_replaces_stale_outputs_and_zips_only_publish_files(self):
        def fake_publish(root, output, build, self_contained):
            output.mkdir(parents=True)
            (output / "WindowsTopBar.exe").write_bytes(b"test executable")
            if self_contained:
                (output / "coreclr.dll").write_bytes(b"test runtime")
            language = output / "zh-Hans"
            language.mkdir()
            (language / "test.resources.dll").write_bytes(b"test language")
            build.mkdir(parents=True)
            (build / "intermediate").write_bytes(b"must not be packaged")

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            old = root / "artifacts" / "releases" / "v1.0.0"
            (old / "lite").mkdir(parents=True)
            (old / "lite" / "stale.dll").write_bytes(b"stale")
            with patch.object(release, "publish", side_effect=fake_publish):
                result = release.package_release(root, "v1.0.0")
            self.assertEqual(result, old)
            self.assertFalse((result / "lite" / "stale.dll").exists())
            self.assertFalse((result / ".build").exists())
            for suffix in ("", "-lite"):
                with zipfile.ZipFile(result / f"WindowsTopBar-v1.0.0-win-x64{suffix}.zip") as archive:
                    names = set(archive.namelist())
                    self.assertIn("WindowsTopBar.exe", names)
                    self.assertIn("zh-Hans/test.resources.dll", names)
                    self.assertEqual("coreclr.dll" in names, suffix == "")
                    self.assertEqual(archive.testzip(), None)

    def test_publish_detects_missing_files_and_extra_languages(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(release, "run") as run:
            root = Path(temporary)
            output = root / "full"
            output.mkdir()
            with self.assertRaises(ValueError):
                release.publish(root, output, root / "build", True)
            for name in ("WindowsTopBar.exe", "WindowsTopBar.dll", "WindowsTopBar.runtimeconfig.json"):
                (output / name).touch()
            extra = output / "ja"
            extra.mkdir()
            (extra / "test.resources.dll").touch()
            with self.assertRaises(ValueError):
                release.publish(root, output, root / "build", True)
            (extra / "test.resources.dll").unlink()
            release.publish(root, output, root / "build", False)
            command = run.call_args.args[0]
            self.assertEqual(command[command.index("--self-contained") + 1], "false")
            self.assertIn("-p:SatelliteResourceLanguages=zh-Hans", command)


if __name__ == "__main__":
    unittest.main()
