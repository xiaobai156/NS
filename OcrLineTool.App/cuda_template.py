"""CUDA fingerprint backend. No CPU matching fallback; not yet wired into the UI."""
import math
import os
from pathlib import Path
import sys


def require_cuda(cv):
    if cv.cuda.getCudaEnabledDeviceCount() < 1:
        raise RuntimeError('NVIDIA CUDA template matching is unavailable')
    cv.cuda.setDevice(0)


def load_cuda(opencv_directory, cuda_directory):
    """Load the isolated build, never the unrelated system cv2 installation."""
    root = Path(opencv_directory).resolve()
    extension = root / 'python' / 'cv2' / 'python-3.11'
    if sys.version_info[:2] != (3, 11) or not (extension / 'cv2.cp311-win_amd64.pyd').is_file():
        raise RuntimeError('CUDA OpenCV requires the packaged Python 3.11 extension')
    if 'cv2' in sys.modules:
        raise RuntimeError('OpenCV already loaded; use a separate CUDA worker process')
    handles = [os.add_dll_directory(str(root / 'x64' / 'vc17' / 'bin')),
               os.add_dll_directory(str(Path(cuda_directory).resolve()))]
    sys.path.insert(0, str(extension))
    import cv2
    require_cuda(cv2)
    return cv2, handles


def region_bounds(width, height, top, bottom, shift):
    if (width <= 0 or height <= 0 or not all(map(math.isfinite, (top, bottom, shift)))
            or top < 0 or bottom <= top):
        raise ValueError('Invalid fingerprint region')
    first = min(max(round(width * (top + shift)), 0), height - 1)
    last = min(max(round(width * (bottom + shift)), first + 1), height)
    return first, last


def fingerprints(cv, image, regions, shifts):
    """Upload once, compute shifted thumbnails/comparisons on GPU, download bits.

    Memory is bounded to one source image and one region's shifted thumbnails.
    File decoding and compact bit serialization are host operations, not fallback.
    """
    import numpy as np
    if image is None or image.ndim != 3 or image.shape[2] != 3 or image.dtype != np.uint8:
        raise ValueError('Expected a decoded uint8 BGR image')
    if not shifts or len(shifts) > 241:
        raise ValueError('Expected 1..241 shifts')
    gpu = cv.cuda_GpuMat()
    gpu.upload(image)
    height, width = image.shape[:2]
    output = []
    for top, bottom in regions:
        reduced = cv.cuda_GpuMat(16 * len(shifts), 65, cv.CV_8UC3)
        for index, shift in enumerate(shifts):
            first, last = region_bounds(width, height, top, bottom, shift)
            cv.cuda.resize(gpu.rowRange(first, last), (65, 16),
                           dst=reduced.rowRange(index * 16, (index + 1) * 16),
                           interpolation=cv.INTER_AREA)
        gray = cv.cuda.cvtColor(reduced, cv.COLOR_BGR2GRAY)
        compared = cv.cuda.compare(gray.colRange(0, 64), gray.colRange(1, 65), cv.CMP_LE)
        bits = compared.download().reshape(len(shifts), 16, 64) != 0
        packed = np.packbits(bits, axis=2, bitorder='little').reshape(len(shifts), 16, 8)
        output.append([''.join(f'{int.from_bytes(row.tobytes(), "little"):016X}'
                               for row in variant) for variant in packed])
    return output
