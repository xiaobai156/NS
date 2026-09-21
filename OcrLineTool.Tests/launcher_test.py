import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


@unittest.skipUnless(shutil.which("powershell.exe"), "Windows launcher")
class LauncherTest(unittest.TestCase):
    def test_root_release_is_preferred_without_python_or_gpu(self):
        self.check_launch(root_release=True)

    def test_latest_version_fallback_is_numeric(self):
        self.check_launch(root_release=False)

    def check_launch(self, root_release):
        with tempfile.TemporaryDirectory(prefix="ocr-launcher-") as temporary:
            root = Path(temporary)
            script = root / "start_cuda_ocr.ps1"
            shutil.copyfile(Path(__file__).parents[1] / script.name, script)
            release = root / "发布"
            exe_name = "OCR整行提取工具-NVIDIA-CUDA.exe"
            for version in ("1.5.9", "1.5.148"):
                folder = release / ("N卡本地识别-v" + version)
                folder.mkdir(parents=True)
                (folder / exe_name).touch()
            expected = release / "N卡本地识别-v1.5.148" / exe_name
            if root_release:
                expected = release / exe_name
                expected.touch()
            # Intercept only process launch; discovery and validation run unchanged.
            command = ("[Console]::OutputEncoding = [Text.UTF8Encoding]::new(); "
                       "function Start-Process { param($FilePath,$WorkingDirectory,$WindowStyle) "
                       "Write-Output ('EXE=' + $FilePath); Write-Output ('CWD=' + $WorkingDirectory) }; "
                       "& '" + str(script).replace("'", "''") + "'")
            result = subprocess.run(["powershell.exe", "-NoProfile", "-Command", command],
                                    capture_output=True, timeout=30)
            output = result.stdout.decode("utf-8", errors="replace")
            self.assertEqual(0, result.returncode, result.stderr.decode(errors="replace"))
            self.assertIn("EXE=" + str(expected), output)
            self.assertIn("CWD=" + str(expected.parent), output)


if __name__ == "__main__":
    unittest.main()
