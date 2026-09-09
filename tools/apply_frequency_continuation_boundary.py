from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')
old = '''                    if (ContainsAnyIssue(lines[next]) || Regex.IsMatch(lines[next], @"(?<!\\d)\\d{1,2}\\s*次"))
                        break;'''
new = '''                    if (ContainsIssue(lines[next], issue)
                        || ContainsIssueBoundary(lines[next], issue)
                        || Regex.IsMatch(lines[next], @"(?<!\\d)\\d{1,2}\\s*次"))
                        break;'''
count = text.count(old)
if count != 1:
    raise RuntimeError(f'frequency continuation boundary: expected 1 occurrence, found {count}')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Applied frequency continuation issue-boundary repair')
