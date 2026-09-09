"""Audit the existing custom CUDA kernel against recovered reference candidates.

Writes evidence only. Never updates production templates or accepts nearest
images as verified identities. Zero-shift calibration is not full acceptance.
"""
import argparse
import ctypes
import importlib.util
import json
from pathlib import Path
import time

import numpy as np


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('catalog', type=Path)
    parser.add_argument('references', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    spec = importlib.util.spec_from_file_location('cuda_template', root / 'OcrLineTool.App/cuda_template.py')
    backend = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(backend)
    cv, handles = backend.load_cuda(root / 'artifacts/opencv-cuda-build/install',
                                   Path('C:/Program Files/NVIDIA GPU Computing Toolkit/CUDA/v12.6/bin'))
    dll = ctypes.CDLL(str(root / 'artifacts/cuda-validation/cuda_hash.dll'))
    kernel = dll.cuda_fingerprint
    kernel.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int,
                       ctypes.c_double, ctypes.c_double, ctypes.c_double, ctypes.c_void_p]
    kernel.restype = ctypes.c_int
    catalog = json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    references = json.loads(args.references.read_text(encoding='utf-8-sig'))
    templates = catalog['templates']
    regions = [(t.get('fingerprintTopWidthRatio', catalog.get('fingerprintTopWidthRatio', .1125)),
                t.get('fingerprintBottomWidthRatio', catalog.get('fingerprintBottomWidthRatio', .2125)))
               for t in templates]
    unique_regions = list(dict.fromkeys(regions))
    selected = {e['Id']: e['Candidates'][0]['Path'] for e in references['Entries']}
    image_root = Path(next(iter(selected.values()))).parent
    # Locate the original group root, including images in nested folders.
    while image_root.name != Path(args.references).stem and image_root.parent.name != '结果':
        if image_root == image_root.parent:
            raise RuntimeError('Cannot locate source group')
        image_root = image_root.parent
    paths = sorted((p for p in image_root.rglob('*') if p.suffix.lower() in
                    ('.jpg', '.jpeg', '.png', '.bmp')), key=lambda p: str(p).casefold())
    hashes, errors = {}, []
    started = time.perf_counter()
    for path in paths:
        image = cv.imdecode(np.fromfile(path, dtype=np.uint8), cv.IMREAD_COLOR)
        if image is None:
            errors.append(str(path))
            continue
        image = np.ascontiguousarray(image)
        hashes[str(path)] = {}
        for region in unique_regions:
            bits = np.zeros(16, dtype=np.uint64)
            status = kernel(image.ctypes.data, image.shape[1], image.shape[0],
                            *region, 0., bits.ctypes.data)
            if status != 0:
                raise RuntimeError(f'CUDA kernel failed: {status}')
            hashes[str(path)][region] = int.from_bytes(bits.tobytes(), 'little')
    rows = []
    for template, region in zip(templates, regions):
        reference = selected[template['id']]
        expected = hashes[reference][region]
        candidates = sorted(((expected ^ values[region]).bit_count(), path)
                            for path, values in hashes.items())
        rows.append({'Id': template['id'], 'ReferenceCandidate': reference,
                     'Fingerprint': ''.join(f'{(expected >> (i * 64)) & ((1 << 64) - 1):016X}' for i in range(16)),
                     'ZeroDistanceImages': sum(d == 0 for d, _ in candidates),
                     'NearestOtherDistance': min((d for d, p in candidates if p != reference), default=None),
                     'Top3': [{'Distance': d, 'Path': p} for d, p in candidates[:3]]})
    report = {'Scope': 'zero-shift candidate calibration; not verified templates',
              'DeviceCount': cv.cuda.getCudaEnabledDeviceCount(), 'Images': len(paths),
              'Seconds': time.perf_counter() - started, 'Entries': rows, 'DecodeErrors': errors}
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'Entries'}, ensure_ascii=False))
    print('ambiguous zero-distance references:', sum(r['ZeroDistanceImages'] != 1 for r in rows))


if __name__ == '__main__':
    main()
