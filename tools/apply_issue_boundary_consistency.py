from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 occurrence, found {count}')
    text = text.replace(old, new, 1)

# A later 4-6 digit bare issue row must close a single-head issue block just as
# a normal NNN期 row does. Also stop on a repeated bare selected issue.
replace_once(
'''            int end = index + 1;
            while (end < lines.Length && !ContainsAnyIssue(lines[end]))
                end++;''',
'''            int end = index + 1;
            while (end < lines.Length
                && !ContainsIssue(lines[end], issue)
                && !ContainsIssueBoundary(lines[end], issue))
                end++;''',
'single-head issue boundary')

# Summary rows are issue-scoped. ContainsAnyIssue intentionally ignores some
# ambiguous 4-6 digit runs, so use the selected-issue-aware boundary predicate.
replace_once(
'''        foreach (string line in SplitInlineIssueRows(source))
        {
            if (ContainsAnyIssue(line))
                inTarget = ContainsIssue(line, issue);
            if (inTarget)
                rows.Add(line);
        }''',
'''        foreach (string line in SplitInlineIssueRows(source))
        {
            if (ContainsIssue(line, issue))
                inTarget = true;
            else if (ContainsIssueBoundary(line, issue))
                inTarget = false;
            if (inTarget)
                rows.Add(line);
        }''',
'summary issue boundary')

# Highest-frequency tables must stop at any recognized next issue, including a
# nearby 4-6 digit bare issue row.
replace_once(
'''            if (ContainsAnyIssue(line))
            {
                if (ContainsIssue(line, issue))
                    inTargetIssue = true;
                else if (inTargetIssue)
                    break;
            }

            if (!inTargetIssue || !TryExtractFrequencyRow(line, out int count, out string rowValue))''',
'''            if (ContainsIssue(line, issue))
            {
                inTargetIssue = true;
            }
            else if (ContainsIssueBoundary(line, issue))
            {
                if (inTargetIssue)
                    break;
                continue;
            }

            if (!inTargetIssue || !TryExtractFrequencyRow(line, out int count, out string rowValue))''',
'frequency issue boundary')

# Nearby-value cards previously skipped an explicit other issue row and then
# kept scanning below it. A different issue is a hard stop, not a row to ignore.
old = '''                if (ContainsAnyIssue(line) && !ContainsIssue(line, issue))
                    continue;'''
new = '''                if (ContainsIssueBoundary(line, issue) && !ContainsIssue(line, issue))
                    break;'''
count = text.count(old)
if count != 2:
    raise RuntimeError(f'nearby zodiac boundary: expected 2 occurrences, found {count}')
text = text.replace(old, new)

path.write_text(text, encoding='utf-8')
print('Applied selected-issue boundary consistency repair')
