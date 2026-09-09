from pathlib import Path
import re

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'Expected exactly one anchor, found {count}: {old[:120]!r}')
    text = text.replace(old, new, 1)


replace_once(
    '    private const string Zodiac = "马蛇龙兔虎牛鼠猪狗鸡猴羊";\n',
    '''    private const string Zodiac = "马蛇龙兔虎牛鼠猪狗鸡猴羊";\n    // Only these reviewed card layouts are allowed to place part of one number row\n    // immediately before its issue cell. Generic rules must never borrow numbers\n    // from a heading or an earlier issue just because the final count happens to fit.\n    private static readonly IReadOnlySet<string> SplitIssueNumberRuleIds =\n        new HashSet<string>(StringComparer.Ordinal)\n        {\n            "宝典", "心水", "内幕", "强哥", "锁妖", "赛马会", "龙王", "红人馆", "老人味", "表弟", "祥瑞阁"\n        };\n''')

replace_once(
    '                : ExtractNumbersAroundIssue(lines, scopeStart, issue, expectedCount);',
    '                : ExtractNumbersAroundIssue(lines, scopeStart, issue, expectedCount, rule);')

replace_once(
'''            if (!ContainsIssue(line, issue))\n                continue;\n\n            candidates.Add(line);\n            string combined = line;''',
'''            if (!ContainsIssue(line, issue))\n                continue;\n            // Shared multi-author sheets must identify the author on the selected\n            // issue row itself. A heading attached to an older issue is not proof.\n            if (rule.Folder == "天机阁杀料"\n                && !aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)))\n                continue;\n            // A section applies only until the next issue boundary. This prevents a\n            // later sibling section from satisfying an earlier requested section.\n            if (!string.IsNullOrWhiteSpace(rule.Section)\n                && !HasSectionForIssueRow(lines, index, scopeStart, rule.Section))\n                continue;\n\n            candidates.Add(line);\n            string combined = line;''')

replace_once(
'''        // Same issue can appear in several different materials on one sheet.\n        // A section printed on target rows scopes that material before conflict checking.\n        if (!string.IsNullOrWhiteSpace(rule.Section))\n        {\n            string section = Normalize(rule.Section);\n            if (candidates.Any(line => Normalize(line).Contains(section, StringComparison.Ordinal)))\n                candidates = candidates.Where(line => Normalize(line).Contains(section, StringComparison.Ordinal)).ToList();\n        }\n\n''',
'''        // Section ownership is enforced while each issue candidate is built; do\n        // not reopen the scope later based on a different sibling candidate.\n\n''')

replace_once(
'''        if (rule.AllowNearbyValue\n            && rule.Type is ("生肖" or "单生肖" or "九肖")\n            && (rule.Type != "九肖" || candidates.Count > 0))''',
'''        if (rule.AllowNearbyValue\n            && rule.Type is ("生肖" or "单生肖"))''')

replace_once(
'''                int forbiddenIndex = tail.IndexOf('禁');\n                if (forbiddenIndex >= 0)\n                {\n                    string? value = ExtractSingleZodiac(tail[(forbiddenIndex + 1)..]);\n                    if (value is not null)\n                        observed.Add(value);\n                }''',
'''                int forbiddenIndex = tail.IndexOf('禁');\n                if (forbiddenIndex >= 0)\n                {\n                    string statusPrefix = Normalize(tail[..forbiddenIndex]);\n                    // Between an author's name and its 禁 field only status marks\n                    // may appear. Another author's name or “待更新” closes ownership.\n                    if (statusPrefix.Length > 0\n                        && Regex.IsMatch(statusPrefix, @"[^正准準中错錯对對0-9]"))\n                        continue;\n                    string? value = ExtractSingleZodiac(tail[(forbiddenIndex + 1)..]);\n                    if (value is not null)\n                        observed.Add(value);\n                }''')

replace_once(
'''    private static string[] SummaryRowsForIssue(IEnumerable<string> source, int issue)\n    {''',
'''    private static bool HasSectionForIssueRow(\n        string[] lines, int issueIndex, int scopeStart, string sectionText)\n    {\n        string section = Normalize(sectionText);\n        if (Normalize(lines[issueIndex]).Contains(section, StringComparison.Ordinal))\n            return true;\n\n        for (int index = issueIndex - 1; index >= scopeStart; index--)\n        {\n            if (ContainsAnyIssue(lines[index]))\n                break;\n            if (Normalize(lines[index]).Contains(section, StringComparison.Ordinal))\n                return true;\n        }\n        return false;\n    }\n\n    private static string[] SummaryRowsForIssue(IEnumerable<string> source, int issue)\n    {''')

replace_once(
'''    private static string? ExtractLeadingSplitNumberTable(\n        string text, MatchCollection periods, int issue, OcrRule rule)\n    {\n        // This card's payload follows its issue label; preceding numbers belong\n        // to the opening banner, never to a wrapped data row.\n        if (rule.Id == "翩翩公子杀十码")\n            return null;''',
'''    private static string? ExtractLeadingSplitNumberTable(\n        string text, MatchCollection periods, int issue, OcrRule rule)\n    {\n        if (!SplitIssueNumberRuleIds.Contains(rule.Id))\n            return null;\n        // These explicitly reviewed cards have a physical row that can straddle\n        // the issue/opening cells. Every other rule is forbidden from this repair.\n        if (rule.Id == "翩翩公子杀十码")\n            return null;''')

replace_once(
'''        tail = SimplifyOcrText(tail);\n        string beforeOpening = tail.Split('开', 2)[0];\n        if (type == "尾" && (tail.Contains("亚太地区八尾", StringComparison.Ordinal) || tail.Contains("团队八尾", StringComparison.Ordinal)))\n        {\n            string digits = string.Concat(Regex.Matches(beforeOpening, @"[0-9]").Select(m => m.Value));\n            var present = digits.Where(char.IsDigit).Select(c => c - '0').Distinct().ToArray();\n            return present.Length == 8 ? string.Join("尾 ", Enumerable.Range(0, 10).Where(n => !present.Contains(n)).Select(n => $"{n}尾")) : null;\n        }''',
'''        tail = SimplifyOcrText(tail);\n        string beforeOpening = BeforeOpeningResult(tail);\n        string withoutIssueBeforeOpening = RemoveIssue(beforeOpening);\n        if (type == "尾" && (tail.Contains("亚太地区八尾", StringComparison.Ordinal) || tail.Contains("团队八尾", StringComparison.Ordinal)))\n        {\n            string digits = string.Concat(Regex.Matches(withoutIssueBeforeOpening, @"[0-9]").Select(m => m.Value));\n            var present = digits.Where(char.IsDigit).Select(c => c - '0').Distinct().ToArray();\n            int[] missing = Enumerable.Range(0, 10).Where(n => !present.Contains(n)).ToArray();\n            return present.Length == 8 && missing.Length == 2\n                ? string.Join(' ', missing.Select(n => $"{n}尾"))\n                : null;\n        }''')

replace_once(
'''        if (type == "头" && (tail.Contains("亚太地区四头", StringComparison.Ordinal) || tail.Contains("团队四头", StringComparison.Ordinal)))\n        {\n            var heads = beforeOpening.Where(c => c is >= '0' and <= '4').Distinct().ToArray();\n            return heads.Length == 4 ? $"{Enumerable.Range(0, 5).Single(n => !heads.Contains((char)('0' + n)))}头" : null;\n        }''',
'''        if (type == "头" && (tail.Contains("亚太地区四头", StringComparison.Ordinal) || tail.Contains("团队四头", StringComparison.Ordinal)))\n        {\n            var heads = withoutIssueBeforeOpening.Where(c => c is >= '0' and <= '4').Distinct().ToArray();\n            return heads.Length == 4 ? $"{Enumerable.Range(0, 5).Single(n => !heads.Contains((char)('0' + n)))}头" : null;\n        }''')

start = text.index('    private static string? ExtractNumbersAroundIssue(')
end = text.index('    private static bool IsNumberRowStart', start)
text = text[:start] + r'''    private static string? ExtractNumbersAroundIssue(
        string[] lines, int scopeStart, int issue, int expectedCount, OcrRule rule)
    {
        if (rule.Id == "蓝色")
            return ExtractFollowingNumberTable(lines, scopeStart, issue, expectedCount);
        if (rule.Id == "彩图")
            return ExtractPrecedingNumberTable(lines, scopeStart, issue, expectedCount);

        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = scopeStart; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;

            var block = new List<string> { TextAfterIssue(lines[index], issue) };
            for (int next = index + 1; next < lines.Length; next++)
            {
                if (ContainsAnyIssue(lines[next]) || !IsNumberContinuation(lines[next]))
                    break;
                block.Add(lines[next]);
            }

            string? value = ExtractNumbers(string.Join(' ', block), expectedCount);
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 1 ? observed.Single() : null;
    }

    private static string? ExtractFollowingNumberTable(
        string[] lines, int scopeStart, int issue, int expectedCount)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = scopeStart; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            var payload = new List<string>();
            for (int next = index + 1; next < lines.Length; next++)
            {
                if (ContainsAnyIssue(lines[next]) || !IsNumberContinuation(lines[next]))
                    break;
                payload.Add(lines[next]);
            }
            string? value = payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 1 ? observed.Single() : null;
    }

    private static string? ExtractPrecedingNumberTable(
        string[] lines, int scopeStart, int issue, int expectedCount)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = scopeStart; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            int first = index - 1;
            while (first >= scopeStart && IsNumberContinuation(lines[first]) && !ContainsAnyIssue(lines[first]))
                first--;
            first++;
            if (first >= index)
                continue;
            string? value = ExtractNumbers(string.Join(' ', lines[first..index]), expectedCount);
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 1 ? observed.Single() : null;
    }

    private static bool IsNumberContinuation(string line)
    {
        if (ContainsAnyIssue(line)
            || Regex.IsMatch(SimplifyOcrText(line), @"参考|旁栏|排行|统计|说明"))
            return false;
        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(line)));
        if (numbers is null || numbers.Length == 0)
            return false;
        return numbers.Length >= 2 || !Regex.IsMatch(line, @"\p{L}");
    }

''' + text[end:]

start = text.index('    private static string? ExtractNumbers(string text, int expectedCount)')
end = text.index('    private static string[]? ParseNumbers', start)
text = text[:start] + r'''    private static string? ExtractNumbers(string text, int expectedCount)
    {
        string beforeOpening = BeforeOpeningResult(text);
        foreach (Match bracket in Regex.Matches(beforeOpening, @"[【\[（(](?<value>[^】\]）)]*)[】\]）)]"))
        {
            string? bracketed = FormatNumbers(ParseNumbers(bracket.Groups["value"].Value), expectedCount);
            if (bracketed is not null)
                return bracketed;
        }
        return FormatNumbers(ParseNumbers(RemoveIssue(beforeOpening)), expectedCount);
    }

    private static string BeforeOpeningResult(string text)
    {
        string simplified = SimplifyOcrText(text);
        Match opening = Regex.Match(simplified,
            $@"(?<!不)(?<!不会)开(?=\s*(?:[?？]+|[0-9]{{1,2}}|[{Zodiac}]))");
        return opening.Success ? simplified[..opening.Index] : simplified;
    }

''' + text[end:]

path.write_text(text, encoding='utf-8')
print('Applied P0 data-accuracy patch to', path)
