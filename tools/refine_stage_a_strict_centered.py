from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def repl(old, new, label, expected=1):
    global text
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f'{label}: expected {expected}, found {count}')
    text = text.replace(old, new, expected)

old = '''                int previous = index - 1;
                while (previous >= 0 && IsOpeningOnlySeparator(lines[previous]))
                    previous--;
                if (previous < 0 || ContainsAnyIssue(lines[previous])
                    || !HasNumberPayload(BeforeOpeningResult(lines[previous])))
                    continue;

                string? split = ExtractReviewedSplitNumberWindow(
                    lines, 0, index, issue, reviewedExpectedCount);'''
new = '''                int previous = index - 1;
                // A real centred row has its left data cell immediately before
                // the target issue cell. Crossing an opening separator or any
                // earlier row would reintroduce the previous-period borrowing bug.
                if (previous < 0 || ContainsAnyIssue(lines[previous])
                    || IsOpeningOnlySeparator(lines[previous])
                    || !HasNumberPayload(BeforeOpeningResult(lines[previous])))
                    continue;

                string? split = ExtractStrictCenteredNumberWindow(
                    lines, index, issue, reviewedExpectedCount);'''
repl(old, new, 'strict adapter uses immediate left cell')

anchor = '''    private static string? ExtractReviewedSplitNumberWindow(
        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount)
    {'''
helper = '''    private static string? ExtractStrictCenteredNumberWindow(
        string[] lines, int issueIndex, int issue, int expectedCount)
    {
        int previous = issueIndex - 1;
        if (previous < 0 || ContainsAnyIssue(lines[previous])
            || IsOpeningOnlySeparator(lines[previous]))
            return null;

        string left = BeforeOpeningResult(lines[previous]);
        if (!HasNumberPayload(left))
            return null;

        var parts = new List<string>
        {
            left,
            BeforeOpeningResult(TextAfterIssue(lines[issueIndex], issue))
        };
        string? complete = ExtractNumbers(string.Join(' ', parts), expectedCount);
        if (complete is not null)
            return complete;

        bool sawRightPayload = false;
        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            if (ContainsIssueBoundary(lines[index], issue))
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (IsNumberRowStart(lines[index]) && sawRightPayload)
                break;
            if (!IsNumberContinuation(lines[index], expectedCount))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }

            parts.Add(BeforeOpeningResult(lines[index]));
            sawRightPayload = true;
            complete = ExtractNumbers(string.Join(' ', parts), expectedCount);
            if (complete is not null)
                return complete;
        }
        return null;
    }

    private static string? ExtractReviewedSplitNumberWindow(
        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount)
    {'''
repl(anchor, helper, 'insert strict centered helper')

path.write_text(text, encoding='utf-8')
print('Isolated strict centered-row extraction')
