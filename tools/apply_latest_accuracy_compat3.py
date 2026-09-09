from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')
old = '''            var parts = new List<string>();
            string first = ScopeNumberPayload(TextAfterIssue(lines[index], issue), rule, expectedCount);
            if (!string.IsNullOrWhiteSpace(first))
                parts.Add(first);
            for (int next = index + 1; next < lines.Length; next++)'''
new = '''            var parts = new List<string>();
            string first = ScopeNumberPayload(TextAfterIssue(lines[index], issue), rule, expectedCount);
            string issueField = RemoveIssue(lines[index]);
            // The target-period row may be only a banner/title containing unrelated
            // digits (for example a site name with 100). Treat it as data only if
            // the same field-ownership rules used for continuations prove it.
            if (!string.IsNullOrWhiteSpace(first)
                && IsNumberContinuation(issueField, expectedCount, rule, issue))
                parts.Add(first);
            for (int next = index + 1; next < lines.Length; next++)'''
if text.count(old) != 1:
    raise RuntimeError(f'target issue field anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Applied target issue row ownership refinement')
