from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'Expected exactly one refined anchor, found {count}: {old[:140]!r}')
    text = text.replace(old, new, 1)


replace_once(
'''        string joined = SimplifyOcrText(string.Join('\\n', lines));
        MatchCollection periods = Regex.Matches(joined,''',
'''        if (SplitIssueNumberRuleIds.Contains(rule.Id))
        {
            var splitObserved = new HashSet<string>(StringComparer.Ordinal);
            for (int index = scopeStart; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;
                string? split = ExtractReviewedSplitNumberWindow(
                    lines, scopeStart, index, issue, expectedCount);
                if (split is not null)
                    splitObserved.Add(split);
            }
            if (splitObserved.Count > 0)
                return splitObserved.Count == 1 ? splitObserved.Single() : null;
        }

        string joined = SimplifyOcrText(string.Join('\\n', lines));
        MatchCollection periods = Regex.Matches(joined,''')

replace_once(
'''                if (ContainsAnyIssue(lines[next]))
                    break;
                if (!IsNumberContinuation(lines[next]))
                    break;
                parts.Add(BeforeOpeningResult(lines[next]));''',
'''                if (ContainsAnyIssue(lines[next]))
                    break;
                if (SplitIssueNumberRuleIds.Contains(rule.Id) && IsOpeningOnlySeparator(lines[next]))
                    continue;
                if (!IsNumberContinuation(lines[next], expectedCount))
                    break;
                parts.Add(BeforeOpeningResult(lines[next]));''')

replace_once(
'''    private static string? ExtractDirectionalNumberTable(
        string[] lines, int issueIndex, int issue, int expectedCount, bool forward)
    {''',
'''    private static string? ExtractReviewedSplitNumberWindow(
        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount)
    {
        int left = issueIndex;
        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (ContainsAnyIssue(lines[index]))
                break;
            left = index;
            if (IsNumberRowStart(lines[index]))
                break;
        }

        var parts = new List<string>();
        for (int index = left; index < issueIndex; index++)
        {
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                return null;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            string candidate = BeforeOpeningResult(lines[index]);
            if (HasNumberPayload(candidate)
                || HasExactBracketPayload(candidate, expectedCount))
                parts.Add(candidate);
        }
        parts.Add(BeforeOpeningResult(TextAfterIssue(lines[issueIndex], issue)));

        bool sawRightPayload = false;
        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            if (ContainsAnyIssue(lines[index]))
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (IsNumberRowStart(lines[index]) && sawRightPayload)
                break;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (!IsNumberContinuation(lines[index], expectedCount))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }
            parts.Add(BeforeOpeningResult(lines[index]));
            sawRightPayload = true;
        }
        return ExtractNumbers(string.Join(' ', parts), expectedCount);
    }

    private static string? ExtractDirectionalNumberTable(
        string[] lines, int issueIndex, int issue, int expectedCount, bool forward)
    {''')

replace_once(
'''    private static bool IsNumberContinuation(string line)
    {
        if (ContainsAnyIssue(line)
            || Regex.IsMatch(SimplifyOcrText(line), @"参考|旁栏|排行|统计|说明"))
            return false;
        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(line)));
        if (numbers is null || numbers.Length == 0)
            return false;
        return numbers.Length >= 2 || !Regex.IsMatch(line, @"\\p{L}")
            || line.Contains('←') || line.Contains('→');
    }''',
'''    private static bool IsNumberContinuation(string line, int expectedCount)
    {
        if (ContainsAnyIssue(line)
            || Regex.IsMatch(SimplifyOcrText(line), @"参考|旁栏|排行|统计|说明"))
            return false;
        if (HasExactBracketPayload(line, expectedCount))
            return true;
        string simplified = SimplifyOcrText(line);
        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(simplified)));
        if (numbers is null || numbers.Length == 0)
            return false;
        if (numbers.Length >= 2 || !Regex.IsMatch(line, @"\\p{L}")
            || line.Contains('←') || line.Contains('→'))
            return true;
        return Regex.IsMatch(simplified, @"^\\s*[0-9 ,，.。]+\\s*开(?=\\s*(?:[?？]+|[0-9]+))");
    }

    private static bool HasExactBracketPayload(string line, int expectedCount)
    {
        foreach (Match bracket in Regex.Matches(
            BeforeOpeningResult(line), @"[【\\[（(](?<value>[^】\\]）)]*)[】\\]）)]"))
        {
            if (FormatNumbers(ParseNumbers(bracket.Groups["value"].Value), expectedCount) is not null)
                return true;
        }
        return false;
    }

    private static bool IsOpeningOnlySeparator(string line) =>
        Regex.IsMatch(SimplifyOcrText(line).Trim(), @"^开[?？中错錯对對准準]*$");''')

replace_once(
'''        Match opening = Regex.Match(simplified,
            $@"(?<!不)(?<!不会)开(?=\\s*(?:[?？]+|[0-9]{{1,2}}|[{Zodiac}]))");''',
'''        Match opening = Regex.Match(simplified,
            $@"(?<!不)(?<!不会)开(?=\\s*(?:[?？]+|[0-9]+|[{Zodiac}]))");''')

path.write_text(text, encoding='utf-8')
print('Applied P0 compatibility refinement to', path)
