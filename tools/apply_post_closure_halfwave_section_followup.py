from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')
old = '''            "半波" => @"(?:(?:红红半波|粉红半波|蓝黑半波)\\s*)+",'''
new = '''            "半波" => @"(?:杀半波\\s*)?(?:(?:红红半波|粉红半波|蓝黑半波)\\s*)+",'''
if text.count(old) != 1:
    raise RuntimeError(f'half-wave section prefix mismatch: {text.count(old)}')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Applied configured half-wave section prefix')
