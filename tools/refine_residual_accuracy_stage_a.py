from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8')


def repl(text, old, new, label, expected=1):
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f'{label}: expected {expected}, found {count}')
    return text.replace(old, new, expected)

# ---------------------------------------------------------------------------
# RuleEngine compatibility refinements.
# ---------------------------------------------------------------------------
path = 'OcrLineTool.App/RuleEngine.cs'
text = read(path)

text = repl(text,
'''    private static readonly Regex BareIssueRegex = new(
        @"^\\s*(?:【|\\[|（|\\(|\\{)?\\s*(?<issue>\\d{3,6})\\s*(?:】|\\]|）|\\)|\\})?\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);''',
'''    private static readonly Regex BareIssueRegex = new(
        @"^\\s*[【\\[（({]?\\s*(?<issue>\\d{3,6})(?!\\d)(?!\\s*\\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);''',
'bare issue prefix compatibility')

text = repl(text,
'''        return BareIssueRegex.IsMatch(line) || YearIssueRegex.IsMatch(line);''',
'''        Match leading = BareIssueRegex.Match(line);
        return leading.Success && leading.Groups["issue"].Value.Length == 3
            || YearIssueRegex.IsMatch(line);''',
'global issue boundary stays conservative')

text = repl(text,
'''    private static string RemoveIssue(string text) =>
        YearIssueRegex.Replace(
            BareIssueRegex.Replace(
                CompactYearIssueRegex.Replace(IssueRegex.Replace(text, ""), ""),
                ""),
            "");''',
'''    private static string RemoveIssue(string text) =>
        YearIssueRegex.Replace(
            BareIssueRegex.Replace(
                CompactYearIssueRegex.Replace(IssueRegex.Replace(text, ""), ""),
                match => match.Groups["issue"].Value.Length == 3 ? "" : match.Value),
            "");''',
'bare issue removal compatibility')

# Number continuation alone gets the selected-issue-aware four+ digit boundary.
text = repl(text,
'''            for (int next = index + 1; next < lines.Length; next++)
            {
                if (ContainsAnyIssue(lines[next]))
                    break;
                if (SplitIssueNumberRuleIds.Contains(rule.Id) && IsOpeningOnlySeparator(lines[next]))''',
'''            for (int next = index + 1; next < lines.Length; next++)
            {
                if (ContainsIssueBoundary(lines[next], issue))
                    break;
                if (SplitIssueNumberRuleIds.Contains(rule.Id) && IsOpeningOnlySeparator(lines[next]))''',
'number continuation selected issue boundary')

text = repl(text,
'''        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            if (ContainsAnyIssue(lines[index]))
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))''',
'''        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            if (ContainsIssueBoundary(lines[index], issue))
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))''',
'reviewed split right boundary')

# Restore reviewed centred layouts with the line-bounded adapter only.
anchor = '''    private static string? ExtractStrictIssueBlock(string[] lines, int issue, OcrRule rule)
    {
        string text = SimplifyOcrText(string.Join('\\n', lines));'''
replacement = '''    private static string? ExtractStrictIssueBlock(string[] lines, int issue, OcrRule rule)
    {
        if (SplitIssueNumberRuleIds.Contains(rule.Id)
            && rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int reviewedExpectedCount))
        {
            var reviewed = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;
                string? split = ExtractReviewedSplitNumberWindow(
                    lines, 0, index, issue, reviewedExpectedCount);
                if (split is not null)
                    reviewed.Add(split);
            }
            if (reviewed.Count > 0)
                return reviewed.Count == 1 ? reviewed.Single() : null;
        }

        string text = SimplifyOcrText(string.Join('\\n', lines));'''
text = repl(text, anchor, replacement, 'reviewed strict split adapter')

# Only pure multi-number brackets define a number field. Parenthesized single
# values such as 马(49)鸡(34)... remain ordinary inline number tokens.
old = '''        Match[] numericBrackets = Regex.Matches(
                beforeOpening, @"[【\\[（(](?<value>[^】\\]）)]*)[】\\]）)]")
            .Cast<Match>()
            .Where(bracket => Regex.IsMatch(bracket.Groups["value"].Value, @"\\d"))
            .ToArray();'''
new = '''        Match[] numericBrackets = Regex.Matches(
                beforeOpening, @"[【\\[（(](?<value>[^】\\]）)]*)[】\\]）)]")
            .Cast<Match>()
            .Where(bracket =>
            {
                string payload = bracket.Groups["value"].Value;
                if (!Regex.IsMatch(payload, @"^\\s*[0-9 ,，.。]+\\s*$"))
                    return false;
                string[]? parsed = ParseNumbers(payload);
                return parsed is { Length: > 1 };
            })
            .ToArray();'''
text = repl(text, old, new, 'number bracket field classifier')

write(path, text)

# ---------------------------------------------------------------------------
# Tests: update only contracts that explicitly conflict with the re-audit F10.
# ---------------------------------------------------------------------------
path = 'OcrLineTool.Tests/MacauRuleHardeningTests.cs'
text = read(path)
old = '''    [Fact]
    public void TimelessCardIgnoresIssueButNeverAcceptsDuplicateOrOpeningNumbers()
    {
        var rule = Rule("时点半");
        string numbers = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        foreach (int issue in new[] { 7, 318, 1001 })
            Assert.Equal(numbers, RuleEngine.ExtractFinalValue([rule.Keyword, "36码", numbers, "上期开奖结果:鸡46中"], issue, rule));
        Assert.Equal(numbers, RuleEngine.ExtractFinalValue(["标题误读", "36码", numbers], 318, rule));
        Assert.Equal(numbers, RuleEngine.ExtractFinalValue([numbers], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "36码", numbers + " 36"], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "36码", numbers.Replace("36", "35")], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "36码", numbers.Replace("36", ""), "上期开奖结果:36"], 318, rule));
    }'''
new = '''    [Fact]
    public void TimelessCardRequiresExplicitSelectedIssueUntilTrustedPublicationEvidenceExists()
    {
        var rule = Rule("时点半");
        string numbers = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        foreach (int issue in new[] { 7, 318, 1001 })
            Assert.Equal(numbers, RuleEngine.ExtractFinalValue(
                [$"{issue}期", rule.Keyword, "36码", numbers, "上期开奖结果:鸡46中"], issue, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "36码", numbers], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["标题误读", "36码", numbers], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([numbers], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期", rule.Keyword, "36码", numbers + " 36"], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期", rule.Keyword, "36码", numbers.Replace("36", "35")], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期", rule.Keyword, "36码", numbers.Replace("36", ""), "上期开奖结果:36"], 318, rule));
    }'''
text = repl(text, old, new, 'timeless card contract')
text = repl(text,
'''        values.Add("时点半", RuleEngine.ExtractFinalValue(["十点半集团大围", "36码", numbers], issue, Rule("时点半"))!);''',
'''        values.Add("时点半", RuleEngine.ExtractFinalValue([$"{issue}期", "十点半集团大围", "36码", numbers], issue, Rule("时点半"))!);''',
'validated distribution timepoint evidence')
write(path, text)

path = 'OcrLineTool.Tests/RuleEngineTests.cs'
text = read(path)
old = '''    [Fact]
    public void ExtractsTimePointNumbersWithoutAnIssueMarkerWhenConfigured()
    {
        var rule = new OcrRule("十点半集团大围", "号码:36", "时点半", IgnoreIssue: true);
        string[] lines = ["十点半集团大围 36码", "28 01 06 47 42 49", "27 21 19 09 08 45", "43 17 35 12 14 29", "07 39 41 46 37 23", "48 10 13 16 26 15", "11 31 22 38 33 32"];

        Assert.Equal("28 01 06 47 42 49 27 21 19 09 08 45 43 17 35 12 14 29 07 39 41 46 37 23 48 10 13 16 26 15 11 31 22 38 33 32",
            RuleEngine.ExtractFinalValue(lines, 243, rule));
    }'''
new = '''    [Fact]
    public void TimePointNumbersStayUnverifiedWithoutAnIssueMarker()
    {
        var rule = new OcrRule("十点半集团大围", "号码:36", "时点半", IgnoreIssue: true);
        string[] lines = ["十点半集团大围 36码", "28 01 06 47 42 49", "27 21 19 09 08 45", "43 17 35 12 14 29", "07 39 41 46 37 23", "48 10 13 16 26 15", "11 31 22 38 33 32"];
        string expected = "28 01 06 47 42 49 27 21 19 09 08 45 43 17 35 12 14 29 07 39 41 46 37 23 48 10 13 16 26 15 11 31 22 38 33 32";

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 243, rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(["243期", ..lines], 243, rule));
    }'''
text = repl(text, old, new, 'RuleEngine timepoint contract')
write(path, text)

print('Refined residual accuracy stage A compatibility')
