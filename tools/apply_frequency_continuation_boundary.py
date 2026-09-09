from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 occurrence, found {count}')
    text = text.replace(old, new, 1)

replace_once(
'''                    if (ContainsAnyIssue(lines[next]) || Regex.IsMatch(lines[next], @"(?<!\\d)\\d{1,2}\\s*次"))
                        break;''',
'''                    if (ContainsIssue(lines[next], issue)
                        || ContainsIssueBoundary(lines[next], issue)
                        || Regex.IsMatch(lines[next], @"(?<!\\d)\\d{1,2}\\s*次"))
                        break;''',
'frequency continuation boundary')

replace_once(
'''    private static bool HasCurrentFieldStartAfterEarlierIssue(
        string[] lines, int scopeStart, int issueIndex, OcrRule rule, int expectedCount)''',
'''    private static bool HasCurrentFieldStartAfterEarlierIssue(
        string[] lines, int scopeStart, int issueIndex, int issue, OcrRule rule, int expectedCount)''',
'split-number helper signature')

replace_once(
'''HasCurrentFieldStartAfterEarlierIssue(lines, 0, issueIndex, rule, expectedCount)''',
'''HasCurrentFieldStartAfterEarlierIssue(lines, 0, issueIndex, issue, rule, expectedCount)''',
'strict centered split call')

replace_once(
'''HasCurrentFieldStartAfterEarlierIssue(lines, scopeStart, issueIndex, rule, expectedCount)''',
'''HasCurrentFieldStartAfterEarlierIssue(lines, scopeStart, issueIndex, issue, rule, expectedCount)''',
'reviewed split call')

replace_once(
'''        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (!ContainsAnyIssue(lines[index]))
                continue;
            previousIssue = index;
            break;
        }''',
'''        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (!ContainsIssue(lines[index], issue)
                && !ContainsIssueBoundary(lines[index], issue))
                continue;
            previousIssue = index;
            break;
        }''',
'split-number previous issue boundary')

replace_once(
'''            if (ContainsAnyIssue(lines[index]))
            {
                if (ContainsIssue(lines[index], issue))
                    continue;
                break;
            }
            string current = Normalize(lines[index]);''',
'''            if (ContainsIssue(lines[index], issue))
                continue;
            if (ContainsIssueBoundary(lines[index], issue))
                break;
            string current = Normalize(lines[index]);''',
'section ownership issue boundary')

path.write_text(text, encoding='utf-8')
print('Applied remaining selected-issue boundary repairs')
