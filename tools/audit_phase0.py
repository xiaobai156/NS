from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

path = 'tools/apply_audit_fixes.py'
text = (ROOT / path).read_text(encoding='utf-8-sig')
marker = '    # MainForm.cs also declares a disposable custom control; target the form only.'
if marker not in text:
    old = '    actual = text.count(old)\n    if actual != count:'
    new = '''    actual = text.count(old)
    # MainForm.cs also declares a disposable custom control; target the form only.
    if old == '    protected override void Dispose(bool disposing)' and actual == 2 and count == 1:
        position = text.index(old)
        following = text[position:position + 180]
        if 'dateTimer.Dispose();' not in following:
            raise RuntimeError('First Dispose is not the audited MainForm timer owner')
        return text.replace(old, new, 1)
    if actual != count:'''
    text = replace(text, old, new)
    write(path, text)

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
