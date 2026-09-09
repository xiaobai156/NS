import importlib.util
import json
import os
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from PIL import Image


class CompactFolderTest(unittest.TestCase):
    def test_only_selected_folder_uses_header_and_compact_image(self):
        self.check_sources(compact=True)

    def test_no_option_keeps_original_paths_even_in_jieshao_folder(self):
        self.check_sources(compact=False)

    def check_sources(self, compact):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_under_test", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        sources = []
        options = []

        class FakeOCR:
            def __init__(self, **kwargs):
                options.append(kwargs)

            def predict(self, source):
                sources.append(source if isinstance(source, str) else source.shape)
                return [{"rec_texts": [f"text-{len(sources)}"]}]

        with tempfile.TemporaryDirectory(prefix="ocr-compact-test-") as temporary:
            root = Path(temporary)
            selected = root / "杰少" / "sample.png"
            other = root / "其他" / "sample.png"
            for path in (selected, other):
                path.parent.mkdir()
                Image.new("RGB", (300, 400), "white").save(path)
            originals = [path.read_bytes() for path in (selected, other)]
            listing = root / "list.txt"
            listing.write_text(f"{selected}\n{other}\n", encoding="utf-8")
            output = root / "result.json"
            argv = [str(script), "--list", str(listing), "--output", str(output), "--det-max-side", "960"]
            if compact:
                argv += ["--compact-folder", "杰少"]
            fake_paddle = types.SimpleNamespace(
                device=types.SimpleNamespace(
                    is_compiled_with_cuda=lambda: True,
                    set_device=lambda device: None,
                    get_device=lambda: "gpu:0",
                )
            )
            model_root = root / "models"
            with patch.dict(os.environ, {"OCR_NVIDIA_MODEL_DIR": str(model_root)}), patch.dict(sys.modules, {
                "paddle": fake_paddle,
                "paddleocr": types.SimpleNamespace(PaddleOCR=FakeOCR),
            }), patch.object(sys, "argv", argv):
                self.assertEqual(0, module.main())
            expected = [(112, 300, 3), (400, 165, 3), str(other)] if compact else [str(selected), str(other)]
            self.assertEqual(expected, sources)
            results = json.loads(output.read_text(encoding="utf-8"))["results"]
            self.assertEqual("gpu:0", options[0]["device"])
            self.assertFalse(options[0]["enable_mkldnn"])
            self.assertFalse(options[0]["use_tensorrt"])
            self.assertEqual("fp32", options[0]["precision"])
            self.assertEqual([str(selected), str(other)], [item["path"] for item in results])
            self.assertEqual(["text-1", "text-2"] if compact else ["text-1"], results[0]["texts"])
            self.assertEqual(originals, [path.read_bytes() for path in (selected, other)])

    def test_cuda_build_stops_instead_of_falling_back_to_cpu(self):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_cuda_check", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        class FakeOCR:
            def __init__(self, **kwargs):
                self.options = kwargs

        fake_paddle = types.SimpleNamespace(
            device=types.SimpleNamespace(is_compiled_with_cuda=lambda: False)
        )
        with tempfile.TemporaryDirectory(prefix="ocr-cuda-check-") as temporary:
            root = Path(temporary)
            listing = root / "list.txt"
            listing.write_text("", encoding="utf-8")
            output = root / "result.json"
            argv = [str(script), "--list", str(listing), "--output", str(output)]
            with patch.dict(sys.modules, {
                "paddle": fake_paddle,
                "paddleocr": types.SimpleNamespace(PaddleOCR=FakeOCR),
            }), patch.object(sys, "argv", argv):
                self.assertEqual(3, module.main())

            error = json.loads(output.read_text(encoding="utf-8"))["error"]
            self.assertIn("不是 CUDA 版 PaddlePaddle", error)

    def test_uses_bundled_model_directories_when_present(self):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_models", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory(prefix="ocr-models-") as temporary:
            root = Path(temporary)
            for name in module.MODEL_NAMES["small"]:
                (root / name).mkdir()
            with patch.dict(os.environ, {"OCR_NVIDIA_MODEL_DIR": str(root)}):
                self.assertEqual(str(root / module.MODEL_NAMES["small"][0]), module._local_model_dir(module.MODEL_NAMES["small"][0]))

    def test_copies_models_to_ascii_cache_for_windows_paddle(self):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_ascii_models", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        with tempfile.TemporaryDirectory(prefix="ocr-ascii-cache-") as temporary:
            root = Path(temporary)
            source = root / "中文模型"
            ascii_cache = root / "ascii-cache"
            model = source / module.MODEL_NAMES["small"][0]
            model.mkdir(parents=True)
            for filename, contents in {
                "inference.json": "{}",
                "inference.pdiparams": "params",
                "inference.yml": "Global: {}",
            }.items():
                (model / filename).write_text(contents, encoding="utf-8")

            with patch.dict(os.environ, {
                "OCR_NVIDIA_MODEL_DIR": str(source),
                "OCR_NVIDIA_MODEL_CACHE": str(ascii_cache),
            }, clear=False):
                cached = module._local_model_dir(module.MODEL_NAMES["small"][0])

            self.assertIsNotNone(cached)
            self.assertNotEqual(str(model), cached)
            self.assertTrue(all(ord(char) < 128 for char in cached))
            for filename in ("inference.json", "inference.pdiparams", "inference.yml"):
                self.assertTrue((Path(cached) / filename).is_file())

    def test_stages_non_ascii_models_to_ascii_cache(self):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_model_staging", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        with tempfile.TemporaryDirectory(prefix="ocr-model-source-") as temporary:
            source = Path(temporary) / "模型源"
            cache = Path(temporary) / "model-cache"
            for name in module.MODEL_NAMES["small"]:
                model = source / name
                model.mkdir(parents=True)
                (model / "inference.json").write_text("{}", encoding="utf-8")
                (model / "inference.pdiparams").write_bytes(b"model")

            with patch.dict(os.environ, {
                "OCR_NVIDIA_MODEL_DIR": str(source),
                "OCR_NVIDIA_MODEL_CACHE": str(cache),
            }):
                prepared = module._prepare_model_root(module.MODEL_NAMES["small"])

            self.assertEqual(cache, prepared)
            for name in module.MODEL_NAMES["small"]:
                self.assertTrue((cache / name / "inference.json").is_file())
                self.assertTrue((cache / name / "inference.pdiparams").is_file())

    def test_cuda_requires_an_active_gpu_device(self):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_device_check", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        selected = []
        fake_paddle = types.SimpleNamespace(
            device=types.SimpleNamespace(
                is_compiled_with_cuda=lambda: True,
                set_device=lambda device: selected.append(device),
                get_device=lambda: "gpu:0",
            )
        )
        with patch.dict(sys.modules, {"paddle": fake_paddle}):
            module._require_cuda("gpu:0")
        self.assertEqual(["gpu:0"], selected)

    def test_cuda_rejects_a_cpu_fallback_device(self):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_cpu_fallback_check", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        fake_paddle = types.SimpleNamespace(
            device=types.SimpleNamespace(
                is_compiled_with_cuda=lambda: True,
                set_device=lambda device: None,
                get_device=lambda: "cpu",
            )
        )
        with patch.dict(sys.modules, {"paddle": fake_paddle}):
            with self.assertRaisesRegex(RuntimeError, "未检测到可用的 NVIDIA CUDA 设备"):
                module._require_cuda("gpu:0")

    def test_cuda_rejects_a_cpu_device_argument(self):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("local_ocr_cpu_argument_check", script)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        with self.assertRaisesRegex(RuntimeError, "只允许使用 gpu:0"):
            module._require_cuda("cpu")


if __name__ == "__main__":
    unittest.main()
