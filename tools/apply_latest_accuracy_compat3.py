from pathlib import Path

# The issue-row ownership refinement is now part of
# apply_latest_accuracy_compat2.py together with the explicit-bracket handling.
# Keep this step intentionally idempotent while older workflow revisions still
# invoke compat3.
path = Path('OcrLineTool.App/RuleEngine.cs')
if not path.exists():
    raise RuntimeError('RuleEngine.cs not found')
print('Target issue row ownership refinement already applied by compat2')
