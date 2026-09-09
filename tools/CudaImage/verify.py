"""Read-only comparison against archived decoded images and GDI fingerprints."""
import argparse
import ctypes
import json
import statistics
import time
from pathlib import Path
from PIL import Image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('library')
    parser.add_argument('baseline')
    args = parser.parse_args()
    library = ctypes.CDLL(str(Path(args.library).resolve()))
    hashes = library.gpu_hashes
    hashes.argtypes = [ctypes.POINTER(ctypes.c_ubyte), ctypes.c_int,
        ctypes.c_int, ctypes.c_double, ctypes.c_double,
        ctypes.POINTER(ctypes.c_double), ctypes.c_int,
        ctypes.POINTER(ctypes.c_uint64)]
    hashes.restype = ctypes.c_int
    entries = json.loads(Path(args.baseline).read_text(encoding='utf-8-sig'))
    distances = []
    started = time.perf_counter()
    for entry in entries:
        with Image.open(entry['Decoded']) as image:
            image = image.convert('RGB')
            width, height = image.size
            pixels = image.tobytes('raw', 'BGR')
        source = (ctypes.c_ubyte * len(pixels)).from_buffer_copy(pixels)
        shifts = (ctypes.c_double * 1)(0)
        result = (ctypes.c_uint64 * 16)()
        code = hashes(source, width, height, .1125, .2125, shifts, 1, result)
        if code:
            raise RuntimeError(f'CUDA returned {code}')
        expected = entry['Fingerprint']
        distances.append(sum((result[i] ^ int(expected[i*16:(i+1)*16], 16)).bit_count()
            for i in range(16)))
    print(json.dumps(dict(images=len(distances),
        seconds=round(time.perf_counter()-started, 3),
        exact=sum(value == 0 for value in distances),
        median=statistics.median(distances), maximum=max(distances),
        above_60=sum(value > 60 for value in distances)), indent=2))


if __name__ == '__main__':
    main()
