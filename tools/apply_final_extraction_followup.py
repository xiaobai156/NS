from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old = '''        if (forward)
        {
            start = issueIndex + 1;
            end = start;
            while (end < lines.Length && !ContainsAnyIssue(lines[end]))
                end++;
        }
        else
        {
            end = issueIndex;
            start = issueIndex - 1;
            while (start >= 0 && !ContainsAnyIssue(lines[start]))
                start--;
            start++;
        }'''
new = '''        if (forward)
        {
            start = issueIndex + 1;
            end = start;
            while (end < lines.Length && !ContainsIssueBoundary(lines[end], issue))
                end++;
        }
        else
        {
            end = issueIndex;
            start = issueIndex - 1;
            while (start >= 0 && !ContainsIssueBoundary(lines[start], issue))
                start--;
            start++;
        }'''
if text.count(old) != 1:
    raise RuntimeError(f'directional issue-window anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Applied directional issue-boundary follow-up')
