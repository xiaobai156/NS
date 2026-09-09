import ctypes, json, sys
from pathlib import Path
from PIL import Image

lib = ctypes.CDLL(str(Path(sys.argv[1]).resolve()))
f = lib.cuda_fingerprints
f.argtypes = [ctypes.POINTER(ctypes.c_ubyte), ctypes.c_int, ctypes.c_int,
              ctypes.c_double, ctypes.c_double, ctypes.POINTER(ctypes.c_double),
              ctypes.c_int, ctypes.POINTER(ctypes.c_uint64)]
f.restype = ctypes.c_int
catalog_path, refs_path, baseline_path = map(Path, sys.argv[2:5])
catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
refs = {e['Id']: e['Candidates'][0]['Path'] for e in json.loads(refs_path.read_text(encoding='utf-8-sig'))['Entries']}
decoded = {e['Path']: e['Decoded'] for e in json.loads(baseline_path.read_text(encoding='utf-8-sig'))}
for item in catalog['templates']:
    path = sys.argv[5] if item['id'] == '雷锋' and len(sys.argv) > 5 else decoded[refs[item['id']]]
    with Image.open(path) as image:
        image = image.convert('RGB'); w, h = image.size; raw = image.tobytes('raw', 'BGR')
    src = (ctypes.c_ubyte * len(raw)).from_buffer_copy(raw)
    shifts = (ctypes.c_double * 1)(0); out = (ctypes.c_uint64 * 16)()
    top = item.get('fingerprintTopWidthRatio') or catalog.get('fingerprintTopWidthRatio') or .1125
    bottom = item.get('fingerprintBottomWidthRatio') or catalog.get('fingerprintBottomWidthRatio') or .2125
    code = f(src, w, h, top, bottom, shifts, 1, out)
    if code: raise SystemExit(f'{item["id"]}: CUDA {code}')
    item['fingerprint'] = ''.join(f'{value:016X}' for value in out)
catalog_path.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
