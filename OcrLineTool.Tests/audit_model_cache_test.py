import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
spec = importlib.util.spec_from_file_location("audit_model_worker", SCRIPT)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class ModelCacheAuditTests(unittest.TestCase):
    def create_model(self, path, payload):
        path.mkdir(parents=True, exist_ok=True)
        (path / "inference.json").write_text("{}", encoding="utf-8")
        (path / "inference.pdiparams").write_bytes(payload)

    def test_updated_weights_get_a_different_ascii_cache_root(self):
        with tempfile.TemporaryDirectory(prefix="ns-model-audit-") as temp:
            root = Path(temp)
            source = root / "源模型"
            name = "PP-OCRv6_small_det"
            self.create_model(source / name, b"old")
            with patch.dict(os.environ, {"OCR_NVIDIA_MODEL_DIR": str(source), "OCR_NVIDIA_MODEL_CACHE": str(root / "cache")}):
                first = worker._prepare_model_root((name,))
                (source / name / "inference.pdiparams").write_bytes(b"new")
                second = worker._prepare_model_root((name,))
            self.assertNotEqual(first, second)
            self.assertEqual(b"old", (first / name / "inference.pdiparams").read_bytes())
            self.assertEqual(b"new", (second / name / "inference.pdiparams").read_bytes())

    def test_existing_target_is_not_accepted_just_because_files_exist(self):
        with tempfile.TemporaryDirectory(prefix="ns-model-audit-") as temp:
            root = Path(temp)
            self.create_model(root / "source", b"new")
            self.create_model(root / "target", b"old")
            worker._stage_model(root / "source", root / "target")
            self.assertEqual(b"new", (root / "target" / "inference.pdiparams").read_bytes())


if __name__ == "__main__":
    unittest.main()
