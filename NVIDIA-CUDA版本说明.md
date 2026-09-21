# OCR 整行提取工具 NVIDIA CUDA 版

本文件夹是 GPU/CPU 双运行时版本，业务规则、图片处理、缓存逻辑、复抓和分流逻辑沿用原版本；本地 PaddleOCR 默认使用 NVIDIA CUDA，也可在“设置”中手动切换 CPU。切换只改变 PaddleOCR 和标题指纹的计算设备，指纹算法、模板、匹配阈值和其他业务流程保持一致。

## 当前改动

- 默认设备为 `gpu:0`。
- 设置页可手动切换 `cpu`；GPU/CPU 不自动互相回退，GPU 失败时只提示手动切换。
- CPU 模式的两个模板群使用与 CUDA 相同的标题指纹算法，不调用 `cuda_hash.dll`，无需重建模板。
- GPU 模式下，进程启动、模型初始化、运行时和返回协议故障停止本轮任务，不进入云 OCR 兜底；普通图片识别失败仍按原业务流程处理。横带和右侧小块补读同样传播 GPU 故障。
- 启动脚本优先运行 `发布` 根目录的 EXE，没有时按版本号选择最新归档；启动界面不预检 GPU 或 Python，保证用户能够进入设置手动切换设备。
- 使用 GPU 版 PaddlePaddle 时不启用 CPU MKL-DNN；CPU 模式使用同一套本地模型和识别参数。
- GPU 识别使用单进程，避免 RTX 2070 SUPER 载入两套模型。
- CUDA 版使用独立缓存和临时目录，不与 CPU 版混用。
- 四个 PP-OCRv6 模型随本版本放在 `模型` 目录，运行时优先读取本地模型，不依赖首次启动下载。
- Windows Paddle 底层对中文模型路径不稳定；首次 OCR 会把所需模型原子复制到 ASCII 缓存（默认 `%LOCALAPPDATA%\OcrLineTool-NVIDIA-CUDA\models`），后续直接复用。
- GPU 优先使用本文件夹或其上级目录中的 `.venv\Scripts\python.exe`；也可用 `OCR_NVIDIA_PYTHON` 指定解释器。CPU 优先使用 `.venv-cpu\Scripts\python.exe`，也可用 `OCR_NVIDIA_CPU_PYTHON` 指定 CPU-only 解释器；未找到时使用系统 `python`。
- GPU 模式的“本地主识别”开始前会执行 CUDA 预检；没有 NVIDIA GPU 或驱动时直接停止并提示手动切换 CPU。CPU 模式跳过 CUDA 预检，不会自动改变用户选择，也不会用云 OCR 冒充本地完成。

## 显卡到货后安装环境

在本文件夹运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\安装NVIDIA-CUDA环境.ps1
```

脚本安装 Windows x64 的 `paddlepaddle-gpu==3.2.2`（CUDA 11.8 索引）和 `paddleocr==3.7.0`，然后执行 CUDA 自检。需要独立 CPU 环境时运行 `安装CPU-OCR环境.ps1`，它会安装 CPU-only `paddlepaddle==3.2.2` 和 `paddleocr==3.7.0` 到 `.venv-cpu`，避免 CUDA 版 Paddle 的 CPU 路径触发原生崩溃。

GPU 模式还必须安装能识别 RTX 2070 SUPER 的 NVIDIA 驱动；当前机器没有 NVIDIA 显卡时可在设置中手动切换 CPU，实际速度需要分别验证。
