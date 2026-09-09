from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'Expected one compatibility anchor, found {count}: {old[:150]!r}')
    text = text.replace(old, new, 1)


# Preserve the verified “241<content>” OCR layout, but never accept arbitrary
# textual prefixes such as “作者编号251”.
replace_once(
'''    private static readonly Regex BareIssueRegex = new(
        @"^\\s*[【\\[（({]?\\s*(?<issue>\\d{3,6})(?!\\d)\\s*[】\\]）)}]?\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);''',
'''    private static readonly Regex BareIssueRegex = new(
        @"^\\s*[【\\[（({]?\\s*(?<issue>\\d{3,6})(?!\\d)(?!\\s*\\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);''')

# Generic boundary detection keeps the old three-digit compact-row protection.
# Four-plus digit bare periods are handled relative to the requested issue below,
# so a number continuation like “4445” is not mistaken for another period.
replace_once(
'''        Match leading = BareIssueRegex.Match(line);
        return leading.Success || YearIssueRegex.IsMatch(line);''',
'''        Match leading = BareIssueRegex.Match(line);
        return leading.Success && leading.Groups["issue"].Value.Length == 3
            || YearIssueRegex.IsMatch(line);''')
replace_once(
'''                _ => ""),''',
'''                match => match.Groups["issue"].Value.Length == 3 ? "" : match.Value),''')

# Target-aware period boundaries: 1002 stops a 1001 block, but 4445 remains a
# valid compact pair continuation far away from the selected issue.
replace_once(
'''                if (ContainsAnyIssue(lines[next]) || combined.Length + lines[next].Length >= 180)
                    break;''',
'''                if (ContainsIssueBoundary(lines[next], issue) || combined.Length + lines[next].Length >= 180)
                    break;''')
replace_once(
'''    private static bool ContainsAnyIssue(string line)
    {''',
'''    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line))
            return true;
        Match leading = BareIssueRegex.Match(line);
        if (!leading.Success || leading.Groups["issue"].Value.Length <= 3
            || !int.TryParse(leading.Groups["issue"].Value, out int actual))
            return false;
        return actual != selectedIssue && Math.Abs((long)actual - selectedIssue) <= 10;
    }

    private static bool ContainsAnyIssue(string line)
    {''')

# Jieshao nine-zodiac values can be several OCR fragments below the selected
# issue row. Inspect all target candidates, but collect every complete value so
# two different target observations still become a conflict.
replace_once(
'''            foreach (string candidate in MaximalCandidates(candidates))''',
'''            foreach (string candidate in candidates)''')

# The “总次数” heading is metadata, not a frequency row. A genuine highest
# frequency row with no zodiac still remains invalid and cannot fall back to #2.
replace_once(
'''    private static bool TryExtractFrequencyRow(string line, out int count, out string value)
    {
        Match match = Regex.Match(line, @"(?<!\\d)(?<count>\\d{1,2})\\s*次");''',
'''    private static bool TryExtractFrequencyRow(string line, out int count, out string value)
    {
        string normalized = Normalize(line);
        if (normalized.Contains("总次数", StringComparison.Ordinal)
            || normalized.Contains("總次數", StringComparison.Ordinal))
        {
            count = 0;
            value = string.Empty;
            return false;
        }
        Match match = Regex.Match(line, @"(?<!\\d)(?<count>\\d{1,2})\\s*次");''')

# One reviewed premium poster is known to duplicate the same zodiac glyph. Keep
# that explicit exception; every other single-zodiac rule requires one raw glyph.
replace_once(
'''        foreach (string line in relevant)
        {
            string? value = ExtractTyped(line, rule.Type);''',
'''        foreach (string line in relevant)
        {
            string? value = ExtractTypedForRule(line, rule);''')
replace_once(
'''    private static string? ExtractTyped(string tail, string type)
    {''',
'''    private static string? ExtractTypedForRule(string tail, OcrRule rule)
    {
        if (rule.Id == "翩翩公子肖" && rule.Type == "生肖")
        {
            string beforeOpening = BeforeOpeningResult(SimplifyOcrText(tail));
            MatchCollection matches = Regex.Matches(beforeOpening, $"[{Zodiac}]");
            string[] distinct = matches.Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
            return matches.Count > 0 && distinct.Length == 1 ? distinct[0] : null;
        }
        return ExtractTyped(tail, rule.Type);
    }

    private static string? ExtractTyped(string tail, string type)
    {''')

path.write_text(text, encoding='utf-8')
print('Applied P1 compatibility refinement')
