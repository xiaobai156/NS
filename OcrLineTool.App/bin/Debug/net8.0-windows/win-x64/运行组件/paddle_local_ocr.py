import argparse
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image

os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
MODEL_NAMES = {
    "small": ("PP-OCRv6_small_det", "PP-OCRv6_small_rec"),
    "medium": ("PP-OCRv6_medium_det", "PP-OCRv6_medium_rec"),
}
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def _write_error(path: str, message: str) -> None:
    with open(path, "w", encoding="utf-8") as output:
        json.dump({"error": message}, output, ensure_ascii=False)


def _require_cuda(device: str) -> None:
    if device.strip().lower() != "gpu:0":
        raise RuntimeError("NVIDIA CUDA 版只允许使用 gpu:0，禁止 CPU 回退。")
    import paddle

    paddle_device = getattr(paddle, "device", None)
    checker = getattr(paddle_device, "is_compiled_with_cuda", None)
    if checker is None:
        checker = getattr(paddle, "is_compiled_with_cuda", None)
    if checker is None or not checker():
        raise RuntimeError("当前 Python 环境不是 CUDA 版 PaddlePaddle，无法使用 NVIDIA GPU。")

    set_device = getattr(paddle_device, "set_device", None)
    get_device = getattr(paddle_device, "get_device", None)
    if set_device is None or get_device is None:
        raise RuntimeError("当前 PaddlePaddle 缺少 GPU 设备检查接口。")
    try:
        set_device(device)
        current = str(get_device())
    except Exception as exc:
        raise RuntimeError(f"无法启用 NVIDIA GPU {device}：{exc}") from exc
    if not current.lower().startswith("gpu"):
        raise RuntimeError(f"未检测到可用的 NVIDIA CUDA 设备（当前设备：{current}）。")


def _configured_model_root() -> Path:
    configured_root = os.environ.get("OCR_NVIDIA_MODEL_DIR")
    return Path(configured_root) if configured_root else Path(__file__).resolve().parent.parent / "模型"


def _model_cache_root() -> Path:
    configured_cache = os.environ.get("OCR_NVIDIA_MODEL_CACHE")
    if configured_cache:
        candidate = Path(configured_cache)
        if str(candidate).isascii():
            return candidate

    for variable in ("LOCALAPPDATA", "TEMP", "TMP"):
        value = os.environ.get(variable)
        if value:
            candidate = Path(value) / "OcrLineTool-NVIDIA-CUDA" / "models"
            if str(candidate).isascii():
                return candidate
    return Path("C:/OcrLineTool-NVIDIA-CUDA/models")


def _model_files_present(path: Path) -> bool:
    return path.is_dir() and all(
        (path / file_name).is_file()
        for file_name in ("inference.json", "inference.pdiparams")
    )


def _stage_model(source: Path, target: Path) -> None:
    if _model_files_present(target):
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    staging = target.parent / f".{target.name}.staging-{os.getpid()}"
    if staging.exists():
        shutil.rmtree(staging, ignore_errors=True)
    shutil.copytree(source, staging)
    if target.exists():
        shutil.rmtree(target, ignore_errors=True)
    os.replace(staging, target)


def _prepare_model_root(model_names: tuple[str, ...]) -> Path:
    source_root = _configured_model_root()
    if str(source_root).isascii() or not all(
        _model_files_present(source_root / model_name) for model_name in model_names
    ):
        return source_root

    cache_root = _model_cache_root()
    try:
        for model_name in model_names:
            _stage_model(source_root / model_name, cache_root / model_name)
    except OSError as exc:
        raise RuntimeError(f"无法创建 ASCII 模型缓存：{exc}") from exc
    return cache_root if all(
        _model_files_present(cache_root / model_name) for model_name in model_names
    ) else source_root


def _local_model_dir(model_name: str) -> str | None:
    root = _prepare_model_root((model_name,))
    path = root / model_name
    return str(path) if path.is_dir() else None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--list", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--cpu-threads", type=int, default=6)
    parser.add_argument("--device", type=str, default="gpu:0")
    parser.add_argument("--top-ratio", type=float, default=1.0)
    parser.add_argument("--model", choices=("small", "medium"), default="small")
    parser.add_argument("--det-max-side", type=int)
    parser.add_argument("--compact-folder")
    args = parser.parse_args()

    try:
        from paddleocr import PaddleOCR
    except Exception as exc:
        _write_error(args.output, f"无法加载 PaddleOCR：{exc}")
        return 2

    try:
        _require_cuda(args.device)
        detection_model, recognition_model = MODEL_NAMES[args.model]
        model_root = _prepare_model_root((detection_model, recognition_model))
        detection_dir = model_root / detection_model if (model_root / detection_model).is_dir() else None
        recognition_dir = model_root / recognition_model if (model_root / recognition_model).is_dir() else None
        model_dirs = {}
        if detection_dir and recognition_dir:
            model_dirs = {
                "text_detection_model_dir": detection_dir,
                "text_recognition_model_dir": recognition_dir,
            }
        ocr = PaddleOCR(
            text_detection_model_name=detection_model,
            text_recognition_model_name=recognition_model,
            **model_dirs,
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
            device=args.device,
            use_tensorrt=False,
            precision="fp32",
            enable_mkldnn=False,
            cpu_threads=args.cpu_threads,
            text_recognition_batch_size=32,
            text_det_limit_side_len=args.det_max_side,
            text_det_limit_type="max" if args.det_max_side else None,
        )
    except Exception as exc:
        _write_error(args.output, f"无法初始化 NVIDIA CUDA PaddleOCR：{exc}")
        return 3

    with open(args.list, "r", encoding="utf-8") as source:
        paths = [line.rstrip("\n") for line in source if line.rstrip("\n")]

    results = []
    for index, path in enumerate(paths, start=1):
        try:
            if args.compact_folder and any(
                parent.name.casefold() == args.compact_folder.casefold()
                for parent in Path(path).parents
            ):
                # Dense Jieshao tables otherwise get detected as vertical columns.
                # Keep the author header uncompressed to separate shared-folder sheets.
                with Image.open(path) as image:
                    header = image.crop((0, 0, image.width, max(1, round(image.height * 0.28)))).convert("RGB")
                    compact = image.resize((max(1, round(image.width * 0.55)), image.height)).convert("RGB")
                    sources = [np.asarray(part)[:, :, ::-1].copy() for part in (header, compact)]
            elif args.top_ratio < 1.0:
                with Image.open(path) as image:
                    crop_height = max(1, round(image.height * args.top_ratio))
                    rgb = image.crop((0, 0, image.width, crop_height)).convert("RGB")
                    source = np.asarray(rgb)[:, :, ::-1].copy()
                sources = [source]
            else:
                sources = [path]
            texts = []
            for source in sources:
                prediction = ocr.predict(source)
                if prediction:
                    first = prediction[0]
                    if hasattr(first, "get"):
                        texts.extend(str(value) for value in (first.get("rec_texts") or []) if str(value))
            results.append({"path": path, "texts": texts})
        except Exception as exc:
            if "ConvertPirAttribute2RuntimeAttribute" in str(exc):
                _write_error(args.output, "当前 CUDA PaddlePaddle 版本与运行时不兼容，请安装与项目匹配的 3.2.2 版。")
                return 4
            results.append({"path": path, "texts": [], "error": str(exc)})
        finally:
            print(f"OCR_PROGRESS|{index}|{len(paths)}|{path}", flush=True)

    with open(args.output, "w", encoding="utf-8") as output:
        json.dump({"results": results}, output, ensure_ascii=False)
    return 0


if __name__ == "__main__":
    sys.exit(main())
