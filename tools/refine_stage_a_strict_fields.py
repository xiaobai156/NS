from pathlib import Path
import re

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

pattern = re.compile(r'''    private static string\? ExtractStrictCenteredNumberWindow\(\n        string\[\] lines, int issueIndex, int issue, int expectedCount\)\n    \{.*?\n    \}\n\n    private static string\? ExtractReviewedSplitNumberWindow''', re.S)
replacement = r'''    private static string? ExtractStrictCenteredNumberWindow(
        string[] lines, int issueIndex, int issue, int expectedCount)
    {
        int previous = issueIndex - 1;
        if (previous < 0 || ContainsAnyIssue(lines[previous])
            || IsOpeningOnlySeparator(lines[previous]))
            return null;

        string immediate = BeforeOpeningResult(lines[previous]);
        if (!HasNumberPayload(immediate))
            return null;

        // Candidate A: the immediate left cell. This is the normal centred-row
        // layout and must never reach farther back just to make the count fit.
        var leftCandidates = new List<List<string>>
        {
            new() { immediate }
        };

        // Candidate B: OCR may split one labelled left cell into several short
        // lines (e.g. “杀特码:02”, “04”, ...). Walk back only until an explicit
        // labelled numeric field start. Never cross an issue/opening/other text.
        var expanded = new List<string> { immediate };
        bool foundFieldStart = IsNumberRowStart(lines[previous]);
        for (int index = previous - 1; !foundFieldStart && index >= 0; index--)
        {
            if (ContainsAnyIssue(lines[index]) || IsOpeningOnlySeparator(lines[index])
                || Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            string candidate = BeforeOpeningResult(lines[index]);
            if (!HasNumberPayload(candidate))
                break;
            expanded.Insert(0, candidate);
            if (IsNumberRowStart(lines[index]))
                foundFieldStart = true;
        }
        if (foundFieldStart && expanded.Count > 1)
            leftCandidates.Add(expanded);

        string centre = SimplifyOcrText(TextAfterIssue(lines[issueIndex], issue));
        // Results such as “兔40中” belong to the opening/result cell, not the
        // data field. The generic BeforeOpeningResult only handles an explicit 开.
        centre = Regex.Split(centre,
            $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]")[0];

        var candidates = leftCandidates
            .Select(left => new List<string>(left) { centre })
            .ToArray();

        string? ResolveComplete()
        {
            string[] complete = candidates
                .Select(parts => ExtractNumbers(string.Join(' ', parts), expectedCount))
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return complete.Length == 1 ? complete[0] : null;
        }

        string? resolved = ResolveComplete();
        if (resolved is not null)
            return resolved;

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

            string right = BeforeOpeningResult(lines[index]);
            foreach (List<string> candidate in candidates)
                candidate.Add(right);
            sawRightPayload = true;
            resolved = ResolveComplete();
            if (resolved is not null)
                return resolved;
        }
        return null;
    }

    private static string? ExtractReviewedSplitNumberWindow'''
new_text, count = pattern.subn(lambda _: replacement, text, count=1)
if count != 1:
    raise RuntimeError(f'strict centered helper: expected 1, found {count}')
path.write_text(new_text, encoding='utf-8')
print('Proved strict centered fields and excluded opening results')
