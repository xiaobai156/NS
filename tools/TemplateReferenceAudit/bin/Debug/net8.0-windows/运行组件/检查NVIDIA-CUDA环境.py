import sys

try:
    import paddle
except Exception as exc:
    raise SystemExit(f"无法加载 PaddlePaddle GPU 环境：{exc}")

compiled = bool(
    getattr(getattr(paddle, "device", None), "is_compiled_with_cuda", lambda: False)()
)
print(f"PaddlePaddle: {paddle.__version__}")
print(f"CUDA 编译支持: {compiled}")

if not compiled:
    raise SystemExit("当前不是 GPU 版 PaddlePaddle，请重新运行安装脚本。")

try:
    paddle.device.set_device("gpu:0")
except Exception as exc:
    raise SystemExit(f"无法启用 NVIDIA GPU 0，请检查显卡和驱动：{exc}")

device = paddle.device.get_device()
print(f"当前设备: {device}")
if not str(device).lower().startswith("gpu"):
    raise SystemExit("未检测到可用的 NVIDIA CUDA 设备，请检查驱动和 GPU 版 PaddlePaddle。")

try:
    paddle.utils.run_check()
except Exception as exc:
    raise SystemExit(f"PaddlePaddle CUDA 自检失败：{exc}")

print("NVIDIA CUDA 环境检查通过。")
