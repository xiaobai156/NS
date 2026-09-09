# Audit validation run 34331744948

Commit tested: ef5cdf4fed0c2e820acb3ef7a46c7f923fc0dedd

Apply: failure; Python: skipped; Debug: skipped; Release: skipped.

CUDA-tagged hardware tests are excluded on this hosted VM. No production OCR, secrets, deployment or image folders are used.

## apply.log

~~~~text
Reviewed changed paths: 
Traceback (most recent call last):
  File "D:\a\NS\NS\tools\audit_phase3.py", line 384, in <module>
    text = replace(text, '    protected override void Dispose(bool disposing)', '''    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
           ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
  File "D:\a\NS\NS\tools\apply_audit_fixes.py", line 25, in replace
    raise RuntimeError(f'Expected {count} audited anchors, found {actual}: {old[:100]!r}')
RuntimeError: Expected 1 audited anchors, found 2: '    protected override void Dispose(bool disposing)'
~~~~

