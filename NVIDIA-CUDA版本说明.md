# OCR 整行提取工具 NVIDIA CUDA 版

本文件夹是 CPU 版本的独立副本，业务规则、图片处理、缓存逻辑、复抓和分流逻辑沿用原版本；本副本只把本地 PaddleOCR 改为 NVIDIA CUDA 后端。

## 当前改动

- 默认设备为 `gpu:0`。
- 使用 GPU 版 PaddlePaddle，不启用 CPU MKL-DNN。
- GPU 识别使用单进程，避免 RTX 2070 SUPER 载入两套模型。
- CUDA 版使用独立缓存和临时目录，不与 CPU 版混用。
- 四个 PP-OCRv6 模型随本版本放在 `模型` 目录，运行时优先读取本地模型，不依赖首次启动下载。
- Windows Paddle 底层对中文模型路径不稳定；首次 OCR 会把所需模型原子复制到 ASCII 缓存（默认 `%LOCALAPPDATA%\OcrLineTool-NVIDIA-CUDA\models`），后续直接复用。
- 优先使用本文件夹或其上级目录中的 `.venv\Scripts\python.exe`；也可用 `OCR_NVIDIA_PYTHON` 指定解释器。
- “本地主识别”开始前会先执行 CUDA 预检；没有 NVIDIA GPU 或驱动时直接停止，不会改走 CPU 或用云 OCR 冒充本地完成。

## 显卡到货后安装环境

在本文件夹运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\安装NVIDIA-CUDA环境.ps1
```

脚本安装 Windows x64 的 `paddlepaddle-gpu==3.2.2`（CUDA 11.8 索引）和 `paddleocr==3.7.0`，然后执行 CUDA 自检。

显卡到货后还必须安装能识别 RTX 2070 SUPER 的 NVIDIA 驱动；启动脚本会先运行 CUDA 自检，未检测到 GPU 时不会启动 OCR。当前机器没有 NVIDIA 显卡时不要运行识别；实际速度需要在 RTX 2070 SUPER 到货后验证。
