using System.Text.RegularExpressions;

namespace OcrLineTool;

public sealed record OcrRule(
    string Keyword,
    string Type,
    string? Label = null,
    string? Section = null,
    string? RequiredKeyword = null,
    string? Folder = null,
    bool IgnoreIssue = false,
    bool AllowNearbyValue = false,
    bool AllowValueWithoutKeyword = false,
    bool StrictIssueBlock = false,
    bool SingleValuePerIssue = false)
{
    public string Id => Label ?? Keyword;
    public string OutputLabel => Label ?? Keyword;
}

public static class RuleEngine
{
    private static readonly Regex IssueRegex = new(@"(?<!\d)(?<issue>\d{1,6})\s*期", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CompactYearIssueRegex = new(@"(?<!\d)20\d{2}(?<issue>\d{3})\s*期", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BareIssueRegex = new(@"^[^\d]{0,8}(?<issue>\d{3,6})(?!\d)(?!\s*\*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex YearIssueRegex = new(@"^\s*\d{4}\s*[-—/]\s*(?<issue>\d{3,6})(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string Zodiac = "马蛇龙兔虎牛鼠猪狗鸡猴羊";

    public static IReadOnlyList<OcrRule> FindMatches(IEnumerable<string> localLines, IEnumerable<OcrRule> rules)
    {
        string text = Normalize(string.Concat(localLines));
        return rules.Where(rule => MatchesText(text, rule)).ToArray();
    }

    public static IReadOnlyList<OcrRule> FindMatches(
        string imagePath,
        IEnumerable<string> localLines,
        IEnumerable<OcrRule> rules)
    {
        string[] lines = localLines.ToArray();
        string text = Normalize(string.Concat(lines));
        string folder = Path.GetFileName(Path.GetDirectoryName(imagePath)) ?? "";
        return rules.Where(rule =>
        {
            string expectedFolder = rule.Folder ?? rule.Keyword;
            bool folderMatches = folder.Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
                || DirectoryAncestors(imagePath).Any(item =>
                    item.Equals(expectedFolder, StringComparison.OrdinalIgnoreCase));
            if (!folderMatches)
                return string.IsNullOrWhiteSpace(rule.Folder) && MatchesText(text, rule);
            if (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                && !RuleCatalog.NormalizeGroupName(rule.RequiredKeyword)
                    .Equals(RuleCatalog.NormalizeGroupName(expectedFolder), StringComparison.OrdinalIgnoreCase)
                && !ContainsKeyword(text, Normalize(rule.RequiredKeyword)))
                return false;
            return HasValueForAnyIssue(lines, rule);
        }).ToArray();
    }

    private static IEnumerable<string> DirectoryAncestors(string imagePath)
    {
        string? directory = Path.GetDirectoryName(imagePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string? name = Path.GetFileName(directory);
            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
            directory = Path.GetDirectoryName(directory);
        }
    }

    public static bool HasValueForAnyIssue(IEnumerable<string> lines, OcrRule rule)
    {
        string[] snapshot = lines.ToArray();
        if (rule.IgnoreIssue)
            return ExtractFinalValue(snapshot, 1, rule) is not null;
        return FindIssues(snapshot).Where(foundIssue => foundIssue > 0).Distinct()
            .Any(foundIssue => ExtractFinalValue(snapshot, foundIssue, rule) is not null);
    }

    public static int? DetectIssueMismatch(
        IEnumerable<IReadOnlyList<string>> cloudResults,
        int selectedIssue,
        int requiredCount = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(selectedIssue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredCount);
        IReadOnlyList<string>[] samples = cloudResults.Take(requiredCount).ToArray();
        if (samples.Length < requiredCount)
            return null;

        var latestIssues = new List<int>(requiredCount);
        foreach (IReadOnlyList<string> sample in samples)
        {
            if (!sample.Any(line => !string.IsNullOrWhiteSpace(line)))
                return null;
            int[] issues = FindIssues(sample).Distinct().ToArray();
            if (issues.Contains(selectedIssue))
                return null;
            if (issues.Length > 0)
                latestIssues.Add(issues.Max());
        }

        IGrouping<int, int>? likelyIssue = latestIssues
            .GroupBy(issue => issue)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .FirstOrDefault();
        return likelyIssue?.Count() >= (requiredCount + 1) / 2
            ? likelyIssue.Key
            : null;
    }

    private static IEnumerable<int> FindIssues(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            foreach (Match match in IssueRegex.Matches(line))
            {
                if (int.TryParse(match.Groups["issue"].Value, out int issue))
                    yield return issue;
            }

            foreach (Match match in CompactYearIssueRegex.Matches(line))
            {
                if (int.TryParse(match.Groups["issue"].Value, out int issue))
                    yield return issue;
            }

            Match leading = BareIssueRegex.Match(line);
            if (leading.Success
                && int.TryParse(leading.Groups["issue"].Value, out int leadingIssue))
                yield return leadingIssue;

            Match yearIssue = YearIssueRegex.Match(line);
            if (yearIssue.Success
                && int.TryParse(yearIssue.Groups["issue"].Value, out int yearIssueNumber))
                yield return yearIssueNumber;
        }
    }

    private static bool MatchesText(string text, OcrRule rule)
    {
        if (rule.StrictIssueBlock)
            text = SimplifyFixedCardText(text);
        // The generic portrait title must not borrow another card's longer
        // title. Its identity cannot depend on last week's displayed zodiac.
        if (rule.StrictIssueBlock && rule.Keyword == "禁肖图"
            && Regex.IsMatch(text, "(?:佛祖|三怪|澳门)禁肖图"))
            return false;
        bool identityMatched = ContainsKeyword(text, Normalize(rule.Keyword));
        if (!identityMatched
            && IsSummaryText(text)
            && !string.IsNullOrWhiteSpace(rule.Label))
        {
            identityMatched = ContainsKeyword(text, Normalize(rule.Label));
        }

        return identityMatched
            && (string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                || ContainsKeyword(text, Normalize(rule.RequiredKeyword)));
    }

    public static bool IsZodiacSummary(IEnumerable<string> lines) =>
        IsSummaryText(Normalize(string.Concat(lines)));

    private static bool IsSummaryText(string text) =>
        text.Contains("统计表", StringComparison.Ordinal)
        || text.Contains("統計表", StringComparison.Ordinal);

    private static bool ContainsKeyword(string text, string keyword)
    {
        if (text.Contains(keyword, StringComparison.Ordinal))
            return true;
        if (keyword.Length < 4)
            return false;

        int minimumLength = Math.Max(1, keyword.Length - 1);
        int maximumLength = keyword.Length + 1;
        for (int length = minimumLength; length <= maximumLength; length++)
        {
            for (int start = 0; start + length <= text.Length; start++)
            {
                if (WithinOneEdit(text.AsSpan(start, length), keyword.AsSpan()))
                    return true;
            }
        }
        return false;
    }

    private static bool WithinOneEdit(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
            return false;
        if (left.Length == right.Length)
        {
            int differences = 0;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index] && ++differences > 1)
                    return false;
            }
            return true;
        }

        ReadOnlySpan<char> longer = left.Length > right.Length ? left : right;
        ReadOnlySpan<char> shorter = left.Length > right.Length ? right : left;
        int longIndex = 0;
        int shortIndex = 0;
        int skips = 0;
        while (longIndex < longer.Length && shortIndex < shorter.Length)
        {
            if (longer[longIndex] == shorter[shortIndex])
            {
                longIndex++;
                shortIndex++;
                continue;
            }
            if (++skips > 1)
                return false;
            longIndex++;
        }
        return true;
    }

    public static string? ExtractValue(IEnumerable<string> cloudLines, int issue, OcrRule rule)
        => ExtractValueCore(cloudLines, issue, rule, requireCloudKeyword: true);

    private static string? ExtractValueCore(IEnumerable<string> cloudLines, int issue, OcrRule rule, bool requireCloudKeyword)
    {
        if (!rule.IgnoreIssue)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issue);
        string[] lines = cloudLines.Select(line => rule.StrictIssueBlock ? SimplifyFixedCardText(line) : line).ToArray();
        // A requested issue is a query, never evidence for repairing OCR.
        if (!rule.StrictIssueBlock)
            lines = SplitInlineIssueRows(lines);
        string keyword = Normalize(rule.Keyword);
        string[] aliases = new[] { rule.Keyword, rule.Label ?? string.Empty }
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (rule.Type == "生肖" && IsZodiacSummary(lines))
            return ExtractZodiacSummaryValue(lines, issue, rule);

        int scopeStart = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            string section = Normalize(rule.Section);
            scopeStart = Array.FindIndex(lines, line => Normalize(line).Contains(section, StringComparison.Ordinal));
            if (scopeStart < 0)
                return null;
        }

        var candidates = new List<string>();

        for (int index = scopeStart; index < lines.Length; index++)
        {
            string line = lines[index];
            if (!ContainsIssue(line, issue))
                continue;

            candidates.Add(line);
            string combined = line;
            for (int next = index + 1; next < lines.Length && next <= index + 4; next++)
            {
                if (ContainsAnyIssue(lines[next]) || combined.Length + lines[next].Length >= 180)
                    break;
                combined += " " + lines[next];
                candidates.Add(combined);
            }
        }

        bool keywordInTarget = candidates.Any(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)));
        bool keywordInHeading = aliases.Any(alias => HasKeywordInHeading(lines, alias));
        if (requireCloudKeyword && !rule.AllowValueWithoutKeyword && !keywordInTarget && !keywordInHeading)
            return null;

        if (rule.SingleValuePerIssue && rule.Type == "头")
            return ExtractSingleHeadPerIssue(lines, issue);

        if (rule.Type == "统计生肖")
            return ExtractMostFrequentZodiac(lines, issue, rule);

        if (rule.StrictIssueBlock)
            return ExtractStrictIssueBlock(lines, issue, rule);

        // This sheet prints the opening result before a separate nine-zodiac row.
        // Match that row within the requested issue, never combine the smaller tiers.
        if (rule.Id == "杰少九肖" && rule.Type == "九肖")
        {
            foreach (string candidate in candidates)
            {
                Match nine = Regex.Match(Normalize(candidate), $"九肖(?<value>[{Zodiac}]{{9}})(?![{Zodiac}])");
                if (nine.Success && nine.Groups["value"].Value.Distinct().Count() == 9)
                    return nine.Groups["value"].Value;
            }
            return null;
        }

        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int expectedCount))
        {
            string? numberValue = rule.IgnoreIssue
                ? ExtractNumbers(string.Join(' ', lines), expectedCount)
                : ExtractNumbersAroundIssue(lines, scopeStart, issue, expectedCount);
            if (numberValue is not null)
                return numberValue;
        }

        var observed = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> relevant = keywordInTarget
            ? candidates.Where(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)))
            : candidates;
        foreach (string line in relevant)
        {
            string? value = ExtractTyped(line, rule.Type);
            if (value is not null)
                observed.Add(value);
        }
        // Never let an earlier copy of the selected issue silently win.
        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : null;

        if (rule.AllowNearbyValue
            && rule.Type is ("生肖" or "单生肖" or "九肖")
            && (rule.Type != "九肖" || candidates.Count > 0))
        {
            int heading = rule.Type == "九肖"
                ? Array.FindIndex(lines, line => Normalize(line).Contains(keyword, StringComparison.Ordinal))
                : Array.FindIndex(lines, line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)));
            if (heading >= 0)
            {
                string nearby = rule.Type == "九肖"
                    ? string.Join(' ', lines.Skip(heading).Take(2))
                    : string.Join(' ', lines.Skip(Math.Max(0, heading - 1)).Take(6));
                string? value = ExtractTyped(nearby, rule.Type);
                if (value is not null)
                    return value;
            }

            // Fixed-template crops can omit the title while retaining the value
            // immediately before or after the current issue marker.
            foreach (int issueIndex in Enumerable.Range(0, lines.Length)
                         .Where(index => ContainsIssue(lines[index], issue)))
            {
                string nearby = string.Join(' ', lines.Skip(Math.Max(0, issueIndex - 2)).Take(5));
                string? value = ExtractTyped(nearby, rule.Type);
                if (value is not null)
                    return value;
            }
        }

        return null;
    }

    private static string? ExtractSingleHeadPerIssue(string[] lines, int issue)
    {
        var heads = new HashSet<char>();
        bool foundIssue = false;

        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;

            foundIssue = true;
            int end = index + 1;
            while (end < lines.Length && !ContainsAnyIssue(lines[end]))
                end++;

            for (int row = index; row < end; row++)
            {
                string text = row == index
                    ? TextAfterIssue(lines[row], issue)
                    : lines[row];
                string beforeOpening = SimplifyOcrText(text).Split('开', 2)[0];
                foreach (Match match in Regex.Matches(
                    beforeOpening,
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头"))
                {
                    heads.Add(ToArabicDigit(match.Groups["head"].Value[0]));
                }
            }
        }

        return foundIssue && heads.Count == 1 ? $"{heads.Single()}头" : null;
    }

    private static string TextAfterIssue(string line, int issue)
    {
        foreach (Match match in IssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return line[(match.Index + match.Length)..];
        }

        foreach (Match match in CompactYearIssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return line[(match.Index + match.Length)..];
        }

        Match leading = BareIssueRegex.Match(line);
        if (leading.Success && int.TryParse(leading.Groups["issue"].Value, out int leadingIssue) && leadingIssue == issue)
            return line[(leading.Index + leading.Length)..];

        Match year = YearIssueRegex.Match(line);
        return year.Success && int.TryParse(year.Groups["issue"].Value, out int yearIssue) && yearIssue == issue
            ? line[(year.Index + year.Length)..]
            : RemoveIssue(line);
    }

    private static string[] SplitInlineIssueRows(IEnumerable<string> source)
    {
        var output = new List<string>();
        foreach (string line in source)
        {
            MatchCollection periods = IssueRegex.Matches(line);
            int start = 0;
            for (int index = 1; index < periods.Count; index++)
            {
                output.Add(line[start..periods[index].Index]);
                start = periods[index].Index;
            }
            output.Add(line[start..]);
        }
        return output.ToArray();
    }

    private static string[] SummaryRowsForIssue(IEnumerable<string> source, int issue)
    {
        var rows = new List<string>();
        bool inTarget = false;
        foreach (string line in SplitInlineIssueRows(source))
        {
            if (ContainsAnyIssue(line))
                inTarget = ContainsIssue(line, issue);
            if (inTarget)
                rows.Add(line);
        }
        return rows.ToArray();
    }

    private static string? ExtractZodiacSummaryValue(string[] lines, int issue, OcrRule rule)
    {
        if (rule.Type != "生肖"
            || !IsZodiacSummary(lines)
            || !FindIssues(lines).Contains(issue))
        {
            return null;
        }

        lines = SummaryRowsForIssue(lines, issue);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        string[] aliases = [rule.Keyword, rule.Label ?? string.Empty];
        foreach (string aliasText in aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal))
        {
            for (int index = 0; index < lines.Length; index++)
            {
                string line = Normalize(lines[index]);
                int aliasIndex = line.IndexOf(aliasText, StringComparison.Ordinal);
                if (aliasIndex < 0)
                    continue;

                string tail = line[(aliasIndex + aliasText.Length)..];
                int forbiddenIndex = tail.IndexOf('禁');
                if (forbiddenIndex >= 0)
                {
                    string? value = ExtractSingleZodiac(tail[(forbiddenIndex + 1)..]);
                    if (value is not null)
                        observed.Add(value);
                }

                if (index + 2 < lines.Length
                    && Regex.IsMatch(Normalize(lines[index + 1]), "^禁+$"))
                {
                    string? value = ExtractSingleZodiac(Normalize(lines[index + 2]));
                    if (value is not null)
                        observed.Add(value);
                }
            }
        }

        return observed.Count == 1 ? observed.Single() : null;
    }

    private static string? ExtractMostFrequentZodiac(string[] lines, int issue, OcrRule rule)
    {
        int start = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            string section = Normalize(rule.Section);
            start = Array.FindIndex(lines, line => Normalize(line).Contains(section, StringComparison.Ordinal));
            if (start < 0)
                return null;
        }

        bool inTargetIssue = false;
        var rows = new List<(int Count, string Value)>();
        for (int index = start; index < lines.Length; index++)
        {
            string line = lines[index];
            if (ContainsAnyIssue(line))
            {
                if (ContainsIssue(line, issue))
                    inTargetIssue = true;
                else if (inTargetIssue)
                    break;
            }

            if (!inTargetIssue || !TryExtractFrequencyRow(line, out int count, out string rowValue))
                continue;
            if (rowValue.Length == 0)
            {
                for (int next = index + 1; next < lines.Length; next++)
                {
                    if (ContainsAnyIssue(lines[next]) || Regex.IsMatch(lines[next], @"(?<!\d)\d{1,2}\s*次"))
                        break;
                    string nextValue = string.Concat(Regex.Matches(SimplifyOcrText(lines[next]), $"[{Zodiac}]")
                        .Select(item => item.Value).Distinct());
                    if (nextValue.Length > 0)
                    {
                        rowValue = nextValue;
                        break;
                    }
                }
            }
            if (rowValue.Length == 0)
                continue;
            rows.Add((count, rowValue));
        }

        if (rows.Count == 0)
            return null;

        int maximum = rows.Max(row => row.Count);
        string result = string.Concat(rows
            .Where(row => row.Count == maximum)
            .Select(row => row.Value)
            .SelectMany(item => item)
            .Distinct());
        return result.Length > 0 ? result : null;
    }

    private static bool TryExtractFrequencyRow(string line, out int count, out string value)
    {
        Match match = Regex.Match(line, @"(?<!\d)(?<count>\d{1,2})\s*次");
        if (!match.Success || !int.TryParse(match.Groups["count"].Value, out count))
        {
            count = 0;
            value = string.Empty;
            return false;
        }

        string payload = line[(match.Index + match.Length)..];
        payload = payload.Trim().TrimStart(':', '：', ']', '】', ')', '）');
        value = string.Concat(Regex.Matches(SimplifyOcrText(payload), $"[{Zodiac}]")
            .Select(item => item.Value)
            .Distinct());
        return true;
    }

    private static string? ExtractSingleZodiac(string text)
    {
        MatchCollection matches = Regex.Matches(SimplifyOcrText(text), $"[{Zodiac}]");
        return matches.Count == 1 ? matches[0].Value : null;
    }

    private static bool HasKeywordInHeading(string[] lines, string keyword)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            if (ContainsAnyIssue(lines[index]))
                continue;

            string combined = lines[index];
            if (Normalize(combined).Contains(keyword, StringComparison.Ordinal))
                return true;
            for (int next = index + 1; next < lines.Length && next <= index + 2 && !ContainsAnyIssue(lines[next]); next++)
            {
                combined += " " + lines[next];
                if (Normalize(combined).Contains(keyword, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    public static string? ExtractFinalValue(IEnumerable<string> cloudLines, int issue, OcrRule rule) =>
        ExtractValueCore(RejectCrossIssueNearbyValue(cloudLines, issue, rule), issue, rule, requireCloudKeyword: false);

    private static IEnumerable<string> RejectCrossIssueNearbyValue(IEnumerable<string> source, int issue, OcrRule rule)
    {
        string[] lines = source.ToArray();
        if (!rule.StrictIssueBlock || !rule.AllowNearbyValue
            || (rule.Id, rule.Folder, rule.Type) == ("骁腾杀肖", "骁腾系列", "生肖"))
            return lines;
        int target = Array.FindIndex(lines, line => ContainsIssue(line, issue));
        if (target < 0)
            return lines;
        bool hasEarlierIssue = lines.Take(target).Any(ContainsAnyIssue);
        bool hasKeyword = Normalize(lines[target]).Contains(
            Normalize(rule.RequiredKeyword ?? rule.Keyword), StringComparison.Ordinal);
        if (hasEarlierIssue && !hasKeyword)
            return lines.Take(target + 1).ToArray();
        return lines;
    }

    public static string DescribeExtractionFailure(IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        string[] lines = cloudLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length == 0)
            return "云 OCR 未返回有效文字";
        if (!FindIssues(lines).Contains(issue))
            return $"已找到图片和文字，但未识别到第{issue}期";
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int count))
            return $"已找到候选图片和{issue}期文字，但未通过{count}个两位数号码校验（要求01-49且不重复）";
        if (rule.Type is "生肖" or "单生肖" or "九肖")
            return $"已找到{issue}期文字，但未识别到唯一有效生肖";
        if (rule.Type == "统计生肖")
            return $"已找到{issue}期文字，但未提取到最高次数生肖";
        return $"已找到{issue}期文字，但未提取到符合规则的{rule.OutputLabel}值";
    }

    // Opt-in for hardened multi-row cards. Never fall through to the generic
    // nearby/prefix extraction when a complete issue block fails validation.
    private static string? ExtractStrictIssueBlock(string[] lines, int issue, OcrRule rule)
    {
        string text = SimplifyOcrText(string.Join('\n', lines));
        text = Regex.Split(text, @"上期\s*开奖\s*结果")[0];
        if (rule.IgnoreIssue)
        {
            // Identity was already established by candidate/template selection;
            // a noisy title must not be required again for this unnumbered card.
            Match marker = Regex.Match(text, @"36\s*码");
            if (marker.Success)
                return ExtractStrictTableValue(text[(marker.Index + marker.Length)..], rule);
            string heading = string.Join(@"\s*", rule.Keyword.Select(c => Regex.Escape(c.ToString())));
            return ExtractStrictTableValue(Regex.Replace(text.Trim(), "^" + heading, ""), rule);
        }
        MatchCollection periods = Regex.Matches(text,
            @"(?<!\d)(?:第\s*)?(?<issue>\d{1,6})\s*期|(?m:^\s*(?<issue>\d{3})(?!\d)(?=\s|$))");
        string? verticalZodiac = ExtractVerticalIssueZodiac(lines, issue, rule);
        if (verticalZodiac is not null)
            return verticalZodiac;
        string? splitTable = ExtractLeadingSplitNumberTable(text, periods, issue, rule);
        if (splitTable is not null)
            return splitTable;
        if (rule.AllowNearbyValue && rule.Type == "生肖" && rule.Id != "骁腾杀肖")
        {
            int[] issueValues = periods
                .Select(period => int.TryParse(period.Groups["issue"].Value, out int value) ? value : 0)
                .Where(value => value > 0)
                .Distinct()
                .ToArray();
            if (issueValues.Length == 1 && issueValues[0] == issue)
                return ExtractNearbySingleZodiac(lines, issue, rule);
        }
        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < periods.Count; index++)
        {
            Match period = periods[index];
            if (!int.TryParse(period.Groups["issue"].Value, out int actualIssue) || actualIssue != issue)
                continue;
            int start = period.Index + period.Length;
            int end = index + 1 < periods.Count ? periods[index + 1].Index : text.Length;
            string block = text[start..end].Trim();
            if (TryExtractWrappedCardRow(text, period, rule, out string? wrappedValue))
            {
                if (wrappedValue is null)
                    return null;
                values.Add(wrappedValue);
                continue;
            }
            // The banner's previous opening is not a data row, even when its
            // period equals the selected one.
            if (block.StartsWith('开'))
                continue;
            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !Normalize(block).Contains(Normalize(rule.Section), StringComparison.Ordinal))
                continue;
            if (rule.Folder == "公式杀料" && !FormulaBlockAppliesToRule(block, rule))
                continue;
            string? value = ExtractStrictTableValue(block, rule);
            if (value is null)
                return null;
            values.Add(value);
        }
        return values.Count == 1 ? values.Single() : null;
    }

    private static bool TryExtractWrappedCardRow(string text, Match period, OcrRule rule, out string? value)
    {
        value = null;
        if ((rule.Id, rule.Type) is not (("狗庄", "号码:12") or ("天线宝杀", "号码:12") or ("内幕", "号码:36")))
            return false;

        int lineStart = text.LastIndexOf('\n', period.Index);
        int lineEnd = text.IndexOf('\n', period.Index + period.Length);
        if (lineStart < 0 || lineEnd < 0 || text[(lineStart + 1)..period.Index].Trim().Length != 0)
            return false;
        int previousStart = lineStart == 0 ? -1 : text.LastIndexOf('\n', lineStart - 1);
        int nextEnd = text.IndexOf('\n', lineEnd + 1);
        string before = text[(previousStart + 1)..lineStart].Trim();
        string after = text[(lineEnd + 1)..(nextEnd < 0 ? text.Length : nextEnd)].Trim();
        string middle = text[(period.Index + period.Length)..lineEnd].Trim();
        Match opening = Regex.Match(middle, $@"(?:开\s*[?？]+|[{Zodiac}]\s*\d{{2}}\s*[中错])\s*$");
        if (!opening.Success || ContainsAnyIssue(before) || ContainsAnyIssue(after))
            return false;
        middle = middle[..opening.Index].Trim();
        if (rule.Id == "天线宝杀")
        {
            before = Regex.Replace(before, @"^(?:12码\s*→?\s*)+", "");
            after = Regex.Replace(after, @"\s*←精选杀$", "");
        }
        if (!Regex.IsMatch(before, @"^[0-9 ]+$") || !Regex.IsMatch(after, @"^[0-9 ]+$")
            || !Regex.IsMatch(middle, @"^[0-9 ]*$"))
            return false;

        // These cards wrap a single physical row around its centred issue/result
        // cells. Validate whole lines, never take a numeric prefix from another row.
        string[]? first = ParseNumbers(before);
        string[]? last = ParseNumbers(after);
        string[]? centre = ParseNumbers(middle);
        int expected = int.Parse(rule.Type.AsSpan("号码:".Length));
        if (first is null || last is null || centre is null)
            return true;
        bool validLayout = rule.Id == "内幕"
            ? first.Length == centre.Length && first.Length > last.Length && last.Length > 0
            : first.Length == expected - 1 && centre.Length == 0 && last.Length == 1;
        if (validLayout)
            value = FormatNumbers([.. first, .. centre, .. last], expected);
        return true;
    }

    private static string? ExtractNearbySingleZodiac(string[] lines, int issue, OcrRule rule)
    {
        lines = lines.TakeWhile(line => !Regex.IsMatch(
            SimplifyFixedCardText(line), @"上期\s*开奖\s*结果")).ToArray();
        var standaloneValues = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            int previousIssueIndex = Enumerable.Range(0, index)
                .LastOrDefault(i => ContainsAnyIssue(lines[i]), -1);
            int nearbyStart = previousIssueIndex >= 0 ? index : Math.Max(0, index - 4);
            foreach (string line in lines.Skip(nearbyStart).Take(9))
            {
                // Do not borrow a nearby value across another issue boundary.
                if (ContainsAnyIssue(line) && !ContainsIssue(line, issue))
                    continue;
                string normalized = Normalize(line);
                if (Regex.IsMatch(normalized, $"^[{Zodiac}]$"))
                    standaloneValues.Add(SimplifyOcrText(normalized));
                else if (rule.Id == "包公肖肖" && line.Trim() == "￥")
                    standaloneValues.Add("羊");
            }
        }
        if (standaloneValues.Count > 0)
            return standaloneValues.Count == 1 ? standaloneValues.Single() : null;

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            int previousIssueIndex = Enumerable.Range(0, index)
                .LastOrDefault(i => ContainsAnyIssue(lines[i]), -1);
            int nearbyStart = previousIssueIndex >= 0 ? index : Math.Max(0, index - 1);
            foreach (string line in lines.Skip(nearbyStart).Take(3))
            {
                if (ContainsAnyIssue(line) && !ContainsIssue(line, issue))
                    continue;
                string? value = ExtractSingleZodiac(line);
                if (value is not null)
                    values.Add(value);
            }
        }
        return values.Count == 1 ? values.Single() : null;
    }

    private static string? ExtractVerticalIssueZodiac(string[] lines, int issue, OcrRule rule)
    {
        if (rule.Folder != "骁腾系列" || rule.Type != "生肖" || !rule.AllowNearbyValue)
            return null;

        int headingIndex = Array.FindIndex(lines,
            line => Normalize(line).Contains(Normalize(rule.RequiredKeyword ?? rule.Keyword), StringComparison.Ordinal));
        int issueLabelIndex = headingIndex < 0 ? -1 : Array.FindIndex(lines, headingIndex + 1,
            line => Regex.IsMatch(Normalize(line), "^期{2,}$"));
        if (issueLabelIndex < 0)
            return null;

        string[] digitRows = lines.Skip(issueLabelIndex + 1)
            .Select(line => Regex.Replace(line, @"\s+", ""))
            .TakeWhile(line => Regex.IsMatch(line, @"^\d+$"))
            .Take(6)
            .ToArray();
        if (digitRows.Length == 0 || digitRows.Any(row => row.Length != digitRows[0].Length))
            return null;

        int width = digitRows[0].Length;
        string[] zodiacRows = lines.Skip(headingIndex + 1).Take(issueLabelIndex - headingIndex - 1)
            .Select(line => Regex.Replace(SimplifyOcrText(line), @"\s+", ""))
            .Where(line => line.Length == width && Regex.IsMatch(line, $"^[{Zodiac}]+$"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (zodiacRows.Length != 1)
            return null;

        var values = new List<char>();
        for (int column = 0; column < width; column++)
        {
            string issueText = string.Concat(digitRows.Reverse().Select(row => row[column]));
            if (int.TryParse(issueText, out int actualIssue) && actualIssue == issue)
                values.Add(zodiacRows[0][column]);
        }
        return values.Count == 1 ? values[0].ToString() : null;
    }

    private static string? ExtractLeadingSplitNumberTable(
        string text, MatchCollection periods, int issue, OcrRule rule)
    {
        // This card's payload follows its issue label; preceding numbers belong
        // to the opening banner, never to a wrapped data row.
        if (rule.Id == "翩翩公子杀十码")
            return null;
        if (periods.Count == 0
            || rule.Type is not { } type
            || !rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            || !int.TryParse(type.AsSpan("号码:".Length), out int expectedCount))
            return null;

        List<(int Position, int Value)>? tokens = ExtractPositionedTableNumbers(text, periods, expectedCount);
        if (tokens is null)
            return null;
        Match firstPeriod = periods[0];
        int leadingCount = tokens.Count(token => token.Position < firstPeriod.Index);
        if (leadingCount == 0 || leadingCount >= expectedCount)
            return null;

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < periods.Count; index++)
        {
            Match target = periods[index];
            if (!int.TryParse(target.Groups["issue"].Value, out int actualIssue) || actualIssue != issue)
                continue;
            int nextPeriodIndex = index + 1 < periods.Count ? periods[index + 1].Index : text.Length;
            int intervalCount = tokens.Count(token => token.Position > target.Index && token.Position < nextPeriodIndex);
            bool validInterval = index + 1 < periods.Count
                ? intervalCount == expectedCount
                    || expectedCount < 35 && index == 0 && leadingCount > 0
                        && intervalCount == expectedCount - leadingCount
                : intervalCount == expectedCount - leadingCount;
            if (!validInterval)
                return null;

            int[] numbers = tokens.Where(token => token.Position < target.Index)
                .TakeLast(leadingCount)
                .Concat(tokens.Where(token => token.Position > target.Index).Take(expectedCount - leadingCount))
                .Select(token => token.Value)
                .ToArray();
            if (numbers.Length != expectedCount || numbers.Distinct().Count() != expectedCount
                || numbers.Any(number => number is < 1 or > 49))
                return null;
            values.Add(string.Join(' ', numbers.Select(number => number.ToString("00"))));
        }
        return values.Count == 1 ? values.Single() : null;
    }

    private static List<(int Position, int Value)>? ExtractPositionedTableNumbers(
        string text, MatchCollection periods, int expectedCount)
    {
        var tokens = new List<(int Position, int Value)>();
        foreach (Match run in Regex.Matches(text, @"\d+"))
        {
            if (periods.Cast<Match>().Any(period => run.Index >= period.Index
                    && run.Index < period.Index + period.Length))
                continue;
            string following = text[(run.Index + run.Length)..];
            if (Regex.IsMatch(following, @"^\s*%"))
                continue;
            if (int.TryParse(run.Value, out int labelCount)
                && labelCount == expectedCount
                && Regex.IsMatch(following, @"^\s*(?:个|個|码|碼|计|計)"))
                continue;
            string preceding = text[..run.Index].TrimEnd();
            if (preceding.Length > 0 && (preceding[^1] == '开' || preceding[^1] == '特'
                    || Zodiac.Contains(preceding[^1])))
                continue;
            if (run.Length % 2 != 0)
                return null;
            for (int offset = 0; offset < run.Length; offset += 2)
            {
                string pair = run.Value.Substring(offset, 2);
                if (int.TryParse(pair, out int value))
                    tokens.Add((run.Index + offset, value));
            }
        }
        return tokens;
    }

    private static string? ExtractStrictTableValue(string block, OcrRule rule)
    {
        if (rule.Id == "翩翩公子尾" && rule.Type == "缺尾")
        {
            Match fixedCard = Regex.Match(block,
                @"公子送尾数\s*[:：]\s*(?<digits>[0-9\s]{9,})\s*准[!！]?",
                RegexOptions.Singleline);
            if (fixedCard.Success)
            {
          int[] tails = fixedCard.Groups["digits"].Value
              .Where(char.IsDigit).Select(c => c - '0').Distinct().ToArray();
          int digitCount = fixedCard.Groups["digits"].Value.Count(char.IsDigit);
          return digitCount == 9 && tails.Length == 9
              ? $"{Enumerable.Range(0, 10).Single(n => !tails.Contains(n))}尾"
                    : null;
            }
            Match wrapped = Regex.Match(block,
                $@"\A公子送尾数\s*[:：]\s*(?<first>[0-9 $]+)(?:开\s*[?？]+|[{Zodiac}]\s*\d{{2}}\s*[中错])\s*\n\s*(?<last>[0-9 ]+)\s*准[!！]?\s*\z");
            if (wrapped.Success)
                block = wrapped.Groups["first"].Value + " " + wrapped.Groups["last"].Value;
        }
        // An opening column can say "牛18中" without 开. Stop before it and
        // before 准; neither the result nor later OCR lines may fill a deficit.
        block = Regex.Split(block, $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]")[0];
        if (rule.Folder is "68" or "香奈儿" or "战狼" or "红人馆")
        {
            block = ExtractHuangdaxianPayload(Regex.Replace(block, @"\s+", ""), rule);
            if (block.Length == 0)
                return null;
        }
        else if (rule.Folder is "各种杀" or "公式杀料" or "一套组合拳" or "骁腾系列")
        {
            block = ExtractDragonflyPayload(Regex.Replace(block, @"\s+", ""), rule);
            if (block.Length == 0)
                return null;
        }
        // Decorations are separators, not values. Keep unknown letters and
        // digits so malformed OCR cannot silently become a successful result.
        block = Regex.Replace(block, @"[^\p{L}\p{N}\s]", " ").Trim();
        if (rule.Id == "藏宝十二码" && rule.Type == "号码:12")
        {
            block = Regex.Replace(block, @"^藏宝库\s*杀一波12码\s*", "");
            block = Regex.Replace(block, @"^杀\s*[红蓝绿]\s*", "");
            block = Regex.Replace(block, @"[红蓝绿]\s*[中错]\s*$", "");
        }
        if (rule.Id == "藏宝头" && rule.Type == "头")
        {
            if (!block.Contains("无错四头", StringComparison.Ordinal))
                return null;
            Match heads = Regex.Match(block, @"今晚买\s*[【\[]?(?<values>[0-4\s]{7,})[】\]]?\s*头\s*$");
            if (!heads.Success)
                return null;
            string digits = new string(heads.Groups["values"].Value.Where(c => c is >= '0' and <= '4').Distinct().ToArray());
            return digits.Length == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !digits.Contains((char)('0' + n)))}头"
                : null;
        }
        if (rule.Id == "小骚货" && rule.Type == "九肖")
        {
            Match heading = Regex.Match(block, @"[\s\S]*?快乐的骚货\s*九肖\s*(?:正\s*)?");
            if (!heading.Success)
                return null;
            block = block[heading.Length..];
        }
        if (rule.Id is "祖师公肖" or "祖师公尾")
        {
            Match shared = Regex.Match(block, @"^(?:①|1)\s*杀\s*(?<zodiac>.*?)\s*肖\s*(?:①|1)\s*杀\s*(?<tail>.*?)\s*尾$",
                RegexOptions.Singleline);
            if (!shared.Success)
                return null;
            block = shared.Groups[rule.Type == "生肖" ? "zodiac" : "tail"].Value;
        }
        string prefix = rule.Type switch
        {
            var type when type.StartsWith("号码:", StringComparison.Ordinal) =>
                @"(?:公子神算杀?10码|绝杀|庄家必杀|十不中|精杀12码|12码|包围36码|36码|36计|强哥三十六码|锁三十六码|35码\s*赛马会|杀四个特码|杀特码|吃码|稳杀|不开|杀)",
            "缺尾" => @"(?:公子送尾数|狂赢九尾)",
            "缺头" => @"(?:四头出特|必中四头)",
            "生肖" => @"(?:公子杀一肖|帅铁杀一肖)",
            "生肖组合" => @"(?:祥瑞阁杀|绿杀|蓝杀|杀生肖|今期庄吃|(?:广东|福建|广西|贵州|海南|江西|湖南|上海|深圳|云南|四川)报)",
            "色单双" => @"公子秒杀半波",
            "五行" => @"(?:公子4行|大赢家四行|必中)",
            "头" => @"(?:公子禁止[1一]头|帅铁杀一头|今晚你买|特杀|禁|买)",
            "尾" => @"(?:一尾绝杀|帅铁杀一尾)",
            "尾数组合" => @"(?:特码封杀|精杀)",
            "段" => @"杀",
            _ => "(?!)"
        };
        block = Regex.Replace(block.Trim(), "^" + prefix + @"\s*", "");
        block = Regex.Replace(block.Trim(), @"(?:精选杀|必输|会输很惨|不会开)$", "").Trim();
        if (rule.Id == "雷锋")
        {
            if (!Regex.IsMatch(block, $@"^(?:[{Zodiac}]\s*[0-9]+\s*){{4}}$"))
                return null;
            block = Regex.Replace(block, $"[{Zodiac}]", " ");
        }
        if (rule.Id == "杀料五码")
        {
            Match mixed = Regex.Match(block, $@"^(?:[{Zodiac}]\s*){{5}}(?<numbers>[0-9\s]+)$");
            if (!mixed.Success)
                return null;
            block = mixed.Groups["numbers"].Value;
        }
        string value = Regex.Replace(block, @"\s+", "");

        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal))
        {
            string numberText = Regex.Replace(block, @"\s+", " ").Trim();
            if (!Regex.IsMatch(numberText, @"^[0-9]+(?: [0-9]+)*$"))
                return null;
            var numbers = new List<int>();
            foreach (string run in numberText.Split(' '))
            {
                if (run.Length % 2 != 0)
                    return null;
                for (int offset = 0; offset < run.Length; offset += 2)
                    numbers.Add(int.Parse(run.Substring(offset, 2)));
            }
            int count = int.Parse(rule.Type.AsSpan("号码:".Length));
            return numbers.Count == count && numbers.Distinct().Count() == count
                && numbers.All(number => number is >= 1 and <= 49)
                    ? string.Join(' ', numbers.Select(number => number.ToString("00"))) : null;
        }

        if (rule.Type == "缺尾")
            return Regex.IsMatch(value, "^[0-9]{9}$") && value.Distinct().Count() == 9
                ? $"{Enumerable.Range(0, 10).Single(number => !value.Contains((char)('0' + number)))}尾" : null;
        if (rule.Type == "五行")
        {
            value = Regex.Replace(value, "码$", "");
            return Regex.IsMatch(value, "^[金木水火土]{4}$") && value.Distinct().Count() == 4 ? value : null;
        }
        if (rule.Type == "生肖")
            return Regex.IsMatch(value, $"^[{Zodiac}]{{1,3}}$") && value.Distinct().Count() == 1
                && (rule.Id == "翩翩公子肖" || value.Length == 1) ? value[..1] : null;
        if (rule.Type == "生肖组合")
            return Regex.IsMatch(value, $"^[{Zodiac}]{{2}}$") && value.Distinct().Count() == 2 ? value : null;
        if (rule.Type == "色单双")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;
        if (rule.Type == "头")
            return Regex.IsMatch(value, "^[0-4零一二三四]头?$") ? $"{ToArabicDigit(value[0])}头" : null;
        if (rule.Type == "尾")
        {
            value = Regex.Replace(value, "尾$", "");
            return Regex.IsMatch(value, "^[0-9]+$") && value.Distinct().Count() == 1
                && (value.Length == 1 || rule.Id == "宝典尾" && value.Length == 5) ? $"{value[0]}尾" : null;
        }
        if (rule.Type == "尾数组合")
        {
            if (!Regex.IsMatch(value, "^[0-9]尾?[0-9]尾?$"))
                return null;
            value = value.Replace("尾", "");
            return value[0] != value[1] ? $"{value[0]}尾+{value[1]}尾" : null;
        }
        if (rule.Type == "缺头")
        {
            value = Regex.Replace(value, "头$", "");
            return Regex.IsMatch(value, "^[0-4]{4}$") && value.Distinct().Count() == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !value.Contains((char)('0' + n)))}头" : null;
        }
        if (rule.Type == "段")
            return Regex.IsMatch(value, "^[0-7]段$") ? value : null;
        if (rule.Type == "九肖")
            return Regex.IsMatch(value, $"^[{Zodiac}]{{9}}$") && value.Distinct().Count() == 9 ? value : null;
        if (rule.Type == "单五行")
            return Regex.IsMatch(value, "^[金木水火土]$") ? value : null;
        if (rule.Type == "合")
        {
            Match sum = Regex.Match(value, @"^(?<value>0?[1-9]|1[0-3])合$");
            return sum.Success ? $"{int.Parse(sum.Groups["value"].Value):00}合" : null;
        }
        if (rule.Type == "半头")
            return Regex.IsMatch(value, "^[0-4]头[单双]$") ? value : null;
        return null;
    }

    private static bool FormulaBlockAppliesToRule(string block, OcrRule rule)
    {
        Match marker = Regex.Match(block, @"[=＝]\s*杀");
        if (!marker.Success)
            return false;
        string payload = block[(marker.Index + marker.Length)..];
        bool hasZodiac = Regex.IsMatch(payload, $"[{Zodiac}]");
        return rule.Type == "生肖组合" ? hasZodiac
            : rule.Type == "尾数组合" && !hasZodiac && Regex.IsMatch(payload, @"\d.*尾");
    }

    private static string ExtractDragonflyPayload(string block, OcrRule rule)
    {
        if (rule.Id is "黑字杀头" or "黑字杀行" or "黑字杀合")
        {
            string row = Regex.Replace(block, @"[^\p{L}\p{N}]", "");
            Match columns = Regex.Match(row,
                @"^(?<head>[0-4]头)?(?<element>[金木水火土])?(?<sum>(?:0?[1-9]|1[0-3])合)?$");
            if (!columns.Success)
                return string.Empty;
            string value = rule.Id switch
            {
                "黑字杀头" => columns.Groups["head"].Value,
                "黑字杀行" => columns.Groups["element"].Value,
                _ => columns.Groups["sum"].Value
            };
            return value.Length > 0 ? value : string.Empty;
        }

        string pattern = rule.Id switch
        {
            "绿格子双杀" => @"绝杀2肖",
            "公式杀两肖肖" or "公式杀两尾尾" => @"[=＝]杀",
            "红蜻蜓" => @"红蜻蜓必中(?:⑨|9)肖",
            "神奇宇宙" => @"神秘宇宙绝杀半波",
            "墨羽" => @"墨羽尘曦(?:精)?杀一肖",
            "骁腾杀肖" => $@"杀一(?:肖|(?=《[{Zodiac}]》))",
            "骁腾" => @"九肖",
            _ => "(?!)"
        };
        Match marker = Regex.Match(block, pattern, RegexOptions.Singleline);
        if (!marker.Success)
            return string.Empty;
        string payload = block[(marker.Index + marker.Length)..];
        Match framed = Regex.Match(payload,
            @"^[：:，,、\-]*(?:【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\{(?<value>[^}]+)\})");
        if (framed.Success)
            return framed.Groups["value"].Value;
        return Regex.Split(payload, @"发|發")[0].Trim();
    }

    private static string ExtractHuangdaxianPayload(string block, OcrRule rule)
    {
        string pattern = rule.Id switch
        {
            "68小陈" => @"小陈.*?杀一段",
            "68赵高" => @"赵高.*?杀一头",
            "68宝爷" => @"宝爷.*?杀一肖",
            "68兴旺" => @"兴旺.*?杀一行",
            "68老大" => @"九肖中特",
            "68旺仔" => @"旺仔.*?杀一尾",
            "68凯哥" => @"凯哥.*?杀五码",
            "68波波" => @"波波.*?杀半波",
            "香奈风清扬" => @"四头出特",
            "香奈微风细雨" => @"(?:⑨|9)肖中特",
            "香奈老大" => @"四行中特",
            "香奈肖肖" => @"禁肖",
            "香奈尾" => @"禁尾",
            "战狼八戒" => @"绝杀二肖",
            "战狼小馒头" => @"杀",
            "战狼天空" => @"九肖",
            "战狼老王" => @"老王.*?杀半头",
            "红人关公肖" => @"关公.*?杀一肖",
            "红人关公尾" => @"关公.*?杀一尾",
            "红人极点肖" => @"极点.*?杀一肖",
            "战狼九点" or "战狼蔷薇" => "^",
            _ => "(?!)"
        };
        Match marker = Regex.Match(block, pattern, RegexOptions.Singleline);
        if (!marker.Success)
            return string.Empty;
        string payload = block[(marker.Index + marker.Length)..];
        if (rule.Id == "68凯哥")
            payload = Regex.Split(payload, @"来")[0];
        if (rule.Id == "香奈肖肖")
            payload = Regex.Split(payload, @"禁尾|特")[0];
        else if (rule.Id == "香奈尾")
            payload = Regex.Split(payload, @"特")[0];
        else if (rule.Id == "战狼蔷薇")
            payload = Regex.Split(payload, @"特")[0];
        return payload.Trim();
    }

    public static string DescribeMissing(bool foundImage, bool recognizedText) =>
        !foundImage ? "未找到对应图片"
        : !recognizedText ? "图片文字识别失败"
        : "未识别到当期目标数据";

    public static string[] FormatOutput(
        IEnumerable<OcrRule> rules,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string>? missingReasons = null) =>
        rules.Select(rule => values.TryGetValue(rule.Id, out string? value)
            ? $"{FormatForOutput(rule, value)} {rule.OutputLabel}"
            : missingReasons is not null && missingReasons.TryGetValue(rule.Id, out string? reason)
                ? $"缺失（{reason}） {rule.OutputLabel}"
                : $"缺失 {rule.OutputLabel}").ToArray();

    private static string FormatForOutput(OcrRule rule, string value)
    {
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal))
            return Regex.Replace(value.Trim(), @"\s+", ",");
        if (rule.Type is "尾数组合" or "头数组合")
            return value.Replace('+', ' ');
        if (rule.Type == "五行")
        {
            string missing = string.Concat("金木水火土".Where(element => !value.Contains(element)));
            return missing.Length == 1 ? missing : value;
        }
        return value;
    }

    private static string? ExtractTyped(string tail, string type)
    {
        tail = SimplifyOcrText(tail);
        string beforeOpening = tail.Split('开', 2)[0];
        if (type == "尾" && (tail.Contains("亚太地区八尾", StringComparison.Ordinal) || tail.Contains("团队八尾", StringComparison.Ordinal)))
        {
            string digits = string.Concat(Regex.Matches(beforeOpening, @"[0-9]").Select(m => m.Value));
            var present = digits.Where(char.IsDigit).Select(c => c - '0').Distinct().ToArray();
            return present.Length == 8 ? string.Join("尾 ", Enumerable.Range(0, 10).Where(n => !present.Contains(n)).Select(n => $"{n}尾")) : null;
        }
        if (type == "五行" && (tail.Contains("亚太地区四行", StringComparison.Ordinal) || tail.Contains("团队四行", StringComparison.Ordinal)))
        {
            string elements = string.Concat(beforeOpening.Where("金木水火土".Contains));
            return elements.Distinct().Count() == 4 ? string.Concat("金木水火土".Where(e => !elements.Contains(e))) : null;
        }
        if (type == "生肖组合" && (tail.Contains("亚太地区十肖", StringComparison.Ordinal) || tail.Contains("团队十肖", StringComparison.Ordinal)))
        {
            string zodiac = string.Concat(beforeOpening.Where(Zodiac.Contains));
            return zodiac.Distinct().Count() == 10 ? string.Concat(Zodiac.Where(z => !zodiac.Contains(z))) : null;
        }
        if (type == "头" && (tail.Contains("亚太地区四头", StringComparison.Ordinal) || tail.Contains("团队四头", StringComparison.Ordinal)))
        {
            var heads = beforeOpening.Where(c => c is >= '0' and <= '4').Distinct().ToArray();
            return heads.Length == 4 ? $"{Enumerable.Range(0, 5).Single(n => !heads.Contains((char)('0' + n)))}头" : null;
        }
        if (type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(type.AsSpan("号码:".Length), out int numberCount))
            return ExtractNumbers(beforeOpening, numberCount);

        if (type == "缺尾")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("公子送尾数", StringComparison.Ordinal);
            string tailText = marker >= 0
                ? withoutIssue[(marker + "公子送尾数".Length)..]
                : withoutIssue;
            int[] tails = Regex.Matches(tailText, @"(?<!\d)[0-9]+(?!\d)")
                .SelectMany(match => marker >= 0 || match.Value.Length == 1 || match.Value.Length == 9
                    ? match.Value.Select(character => character - '0')
                    : [])
                .Distinct()
                .ToArray();
            if (tails.Length != 9)
                return null;
            int missing = Enumerable.Range(0, 10).Single(number => !tails.Contains(number));
            return $"{missing}尾";
        }

        if (type == "单生肖")
        {
            MatchCollection matches = Regex.Matches(beforeOpening, $"[{Zodiac}]");
            return matches.Count == 1 ? matches[0].Value : null;
        }

        if (type is "生肖组合" or "九肖")
        {
            int nineMarker = beforeOpening.LastIndexOf("解九肖", StringComparison.Ordinal);
            string zodiacText = type == "九肖" && nineMarker >= 0
                ? beforeOpening[(nineMarker + "解九肖".Length)..]
                : beforeOpening;
            string value = string.Concat(Regex.Matches(zodiacText, $"[{Zodiac}]").Select(match => match.Value).Distinct());
            return type == "九肖"
                ? value.Length == 9 ? value : null
                : value.Length >= 2 ? value : null;
        }

        if (type == "单五行")
        {
            Match element = Regex.Match(beforeOpening, @"[【\[]?(?<element>[金木水火土])[】\]]?");
            return element.Success ? element.Groups["element"].Value : null;
        }

        if (type == "五行")
        {
            string value = string.Concat(Regex.Matches(beforeOpening, "[金木水火土]").Select(match => match.Value).Distinct());
            return value.Length == 4 ? value : null;
        }

        if (type == "尾数组合")
        {
            Match combination = Regex.Match(beforeOpening, @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?");
            if (!combination.Success)
                combination = Regex.Match(beforeOpening, @"[【\[](?<first>[0-9])\s*(?<second>[0-9])\s*[】\]]?尾");
            if (combination.Success)
                return $"{combination.Groups["first"].Value}尾+{combination.Groups["second"].Value}尾";

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\d)(?<digits>[0-9]{2})\s*尾");
            return compact.Success
                ? $"{compact.Groups["digits"].Value[0]}尾+{compact.Groups["digits"].Value[1]}尾"
                : null;
        }

        if (type is "头数组合" or "缺头")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("四头出特", StringComparison.Ordinal);
            string valueText = marker >= 0 ? withoutIssue[(marker + "四头出特".Length)..] : withoutIssue;
            char[] heads = valueText
                .Where(character => character is >= '0' and <= '4')
                .Distinct()
                .ToArray();
            if (heads.Length != 4)
                return null;
            if (type == "缺头")
            {
                int missing = Enumerable.Range(0, 5).Single(number => !heads.Contains((char)('0' + number)));
                return $"{missing}头";
            }
            return string.Join('+', heads.Select(head => $"{head}头"));
        }

        if (type == "半头")
        {
            Match halfHead = Regex.Match(beforeOpening, @"(?<!\d)(?<head>[0-4])\s*头\s*(?<parity>[单双])");
            return halfHead.Success
                ? $"{halfHead.Groups["head"].Value}头{halfHead.Groups["parity"].Value}"
                : null;
        }

        if (type == "色单双")
        {
            Match colorParity = Regex.Match(beforeOpening, "(?<color>[红蓝绿])(?:波)?(?<parity>[单双])");
            return colorParity.Success
                ? colorParity.Groups["color"].Value + colorParity.Groups["parity"].Value
                : null;
        }

        if (type == "段")
        {
            Match bracketedSegment = Regex.Match(beforeOpening, @"[【\[](?<segment>[0-7])\s*段[】\]]");
            if (bracketedSegment.Success)
                return $"{bracketedSegment.Groups["segment"].Value}段";
        }

        if (type == "尾")
        {
            Match bracketedTail = Regex.Match(beforeOpening, @"[【\[](?<tail>[0-9])\s*尾[】\]]");
            if (bracketedTail.Success)
                return $"{bracketedTail.Groups["tail"].Value}尾";
        }

        if (type == "头")
        {
            int delimiter = Math.Max(beforeOpening.LastIndexOf('：'), beforeOpening.LastIndexOf(':'));
            if (delimiter >= 0)
            {
                MatchCollection valueHeads = Regex.Matches(
                    beforeOpening[(delimiter + 1)..],
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头");
                if (valueHeads.Count > 0)
                {
                    Match valueHead = valueHeads[^1];
                    return $"{ToArabicDigit(valueHead.Groups["head"].Value[0])}头";
                }
            }
        }

        Match match = type switch
        {
            "生肖" => Regex.Match(beforeOpening, $"[{Zodiac}]"),
            "合" => Regex.Match(beforeOpening, @"(?<!\d)(0?[1-9]|1[0-3])合"),
            "段" => Regex.Match(beforeOpening, @"(?<!\d)[0-7]段"),
            "尾" => Regex.Match(beforeOpening, @"(?<!\d)[0-9]尾"),
            "头" => Regex.Match(beforeOpening, @"(?<!\d)[0-4]头"),
            "五行" => Regex.Match(beforeOpening, @"[金木水火土]{2,5}"),
            _ => Match.Empty
        };

        if (!match.Success && type == "尾")
        {
            int marker = beforeOpening.LastIndexOf('尾');
            if (marker >= 0)
            {
                match = Regex.Match(beforeOpening[(marker + 1)..], @"(?<!\d)[0-9](?!\d)");
                if (!match.Success)
                    match = Regex.Match(beforeOpening[(marker + 1)..], @"(?<!\d)(?<tail>[0-9])\k<tail>+(?!\d)");
            }
        }

        if (!match.Success && type == "头")
        {
            Match bracketedHead = Regex.Match(beforeOpening, @"[【\[](?<head>[0-4])\s*[】\]]\s*头");
            if (bracketedHead.Success)
                return $"{bracketedHead.Groups["head"].Value}头";
            Match chineseHead = Regex.Match(beforeOpening, @"买\s*(?<head>[零一二三四])头");
            if (chineseHead.Success)
                return $"{ToArabicDigit(chineseHead.Groups["head"].Value[0])}头";
            int marker = beforeOpening.LastIndexOf('头');
            if (marker >= 0)
                match = Regex.Match(beforeOpening[(marker + 1)..], @"(?<!\d)[0-4零一二三四](?!\d)");
        }

        if (!match.Success)
            return null;
        if (type == "合")
            return $"{int.Parse(match.Groups[1].Value):00}合";
        if (type == "尾" && match.Groups["tail"].Success)
            return $"{match.Groups["tail"].Value}尾";
        if (type == "尾" && match.Value.Length == 1)
            return $"{match.Value}尾";
        if (type == "头" && match.Value.Length == 1)
            return $"{ToArabicDigit(match.Value[0])}头";
        return match.Value;
    }

    private static string? ExtractNumbersAroundIssue(string[] lines, int scopeStart, int issue, int expectedCount)
    {
        for (int index = scopeStart; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;

            if (expectedCount == 36)
            {
                int previousIssue = index - 1;
                while (previousIssue >= scopeStart && !ContainsAnyIssue(lines[previousIssue]))
                    previousIssue--;
                string? precedingValue = ExtractNumbers(
                    string.Join(' ', lines[(previousIssue + 1)..index].Where(HasNumberPayload)),
                    expectedCount);
                if (precedingValue is not null)
                    return precedingValue;
            }

            if (index + 1 < lines.Length && !ContainsAnyIssue(lines[index + 1]))
            {
                string? forwardValue = ExtractNumbers(
                    lines[index] + " " + lines[index + 1],
                    expectedCount);
                if (forwardValue is not null)
                    return forwardValue;
            }

            int left = index;
            int right = index;
            bool leftBlocked = false;
            bool rightBlocked = false;
            bool scanToIssueBoundary = expectedCount == 36;
            int maximumDistance = scanToIssueBoundary ? lines.Length : 12;
            for (int distance = 0; distance <= maximumDistance; distance++)
            {
                if (distance > 0)
                {
                    int nextLeft = index - distance;
                    if (!leftBlocked && nextLeft >= scopeStart)
                    {
                        if (ContainsAnyIssue(lines[nextLeft]))
                            leftBlocked = true;
                        else
                        {
                            left = nextLeft;
                            if (IsNumberRowStart(lines[nextLeft])
                                && (!scanToIssueBoundary || HasNumberPayload(lines[nextLeft])))
                                leftBlocked = true;
                        }
                    }

                    int nextRight = index + distance;
                    if (!rightBlocked && nextRight < lines.Length)
                    {
                        if (ContainsAnyIssue(lines[nextRight]))
                            rightBlocked = true;
                        else if (IsNumberRowStart(lines[nextRight])
                            && (!scanToIssueBoundary || HasNumberPayload(lines[nextRight])))
                        {
                            if (nextRight == index + 1)
                                right = nextRight;
                            rightBlocked = true;
                        }
                        else
                            right = nextRight;
                    }
                }

                string candidate = string.Join(' ', lines[left..(right + 1)]
                    .Where((line, offset) => left + offset == index || HasNumberPayload(line)));
                string? value = ExtractNumbers(candidate, expectedCount);
                if (value is not null)
                    return value;
                if (leftBlocked && rightBlocked)
                    break;
            }
        }

        return null;
    }

    private static bool IsNumberRowStart(string line) =>
        Regex.IsMatch(line, @"\p{L}.*\d") && !ContainsAnyIssue(line);

    private static bool HasNumberPayload(string line)
    {
        string[]? numbers = ParseNumbers(RemoveIssue(line));
        return numbers is not null && (numbers.Length >= 2
            || numbers.Length == 1 && (!Regex.IsMatch(line, @"\p{L}")
                || line.Contains(':') || line.Contains('：') || line.Contains('←') || line.Contains('→')));
    }

    private static string? ExtractNumbers(string text, int expectedCount)
    {
        string beforeOpening = text.Split('开', 2)[0];
        foreach (Match bracket in Regex.Matches(beforeOpening, @"[【\[（(](?<value>[^】\]）)]*)[】\]）)]"))
        {
            string? bracketed = FormatNumbers(ParseNumbers(bracket.Groups["value"].Value), expectedCount);
            if (bracketed is not null)
                return bracketed;
        }
        string[]? numbers = ParseNumbers(RemoveIssue(beforeOpening));
        string? value = FormatNumbers(numbers, expectedCount);
        if (value is not null || beforeOpening.Length == text.Length)
            return value;

        numbers = ParseNumbers(RemoveIssue(text));
        return FormatNumbers(numbers, expectedCount);
    }

    private static string[]? ParseNumbers(string text)
    {
        text = Regex.Replace(text, @"(?<!\d)\d{1,2}\s*(?:个(?:中特码|特码)?|码|计|計)", "");
        text = Regex.Replace(text, @"(?<!\d)\d+\s*%", "");
        text = Regex.Replace(text, @"[开特發发]\s*\d{2}\s*准?", "");
        var numbers = new List<string>();
        foreach (Match match in Regex.Matches(text, @"(?<!\d)[0-9]+(?!\d)"))
        {
            string run = match.Value;
            if (run.Length % 2 != 0)
                return null;
            string[] pairs = Enumerable.Range(0, run.Length / 2)
                .Select(index => run.Substring(index * 2, 2))
                .ToArray();
            if (!pairs.All(pair => int.TryParse(pair, out int number) && number is >= 1 and <= 49))
                return null;
            numbers.AddRange(pairs);
        }
        return numbers.ToArray();
    }

    private static string? FormatNumbers(string[]? numbers, int expectedCount) =>
        numbers is null || numbers.Length != expectedCount || numbers.Distinct().Count() != expectedCount
            ? null
            : string.Join(' ', numbers);

    private static string RemoveIssue(string text) =>
        YearIssueRegex.Replace(
            BareIssueRegex.Replace(
                CompactYearIssueRegex.Replace(IssueRegex.Replace(text, ""), ""),
                match => match.Groups["issue"].Value.Length == 3 ? "" : match.Value),
            "");

    private static char ToArabicDigit(char value) => value switch
    {
        '零' => '0', '一' => '1', '二' => '2', '三' => '3', '四' => '4', _ => value
    };

    private static bool ContainsAnyIssue(string line)
    {
        if (IssueRegex.IsMatch(line))
            return true;
        if (CompactYearIssueRegex.IsMatch(line))
            return true;
        Match leading = BareIssueRegex.Match(line);
        return leading.Success && leading.Groups["issue"].Value.Length == 3
            || YearIssueRegex.IsMatch(line);
    }

    private static bool ContainsIssue(string line, int issue)
    {
        foreach (Match match in IssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return true;
        }

        foreach (Match match in CompactYearIssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return true;
        }

        Match leading = BareIssueRegex.Match(line);
        if (leading.Success
            && int.TryParse(leading.Groups["issue"].Value, out int leadingIssue)
            && leadingIssue == issue)
            return true;

        Match yearIssue = YearIssueRegex.Match(line);
        return yearIssue.Success
            && int.TryParse(yearIssue.Groups["issue"].Value, out int yearIssueNumber)
            && yearIssueNumber == issue;
    }

    private static string SimplifyFixedCardText(string text) => SimplifyOcrText(text)
        .Replace('圖', '图').Replace('帥', '帅').Replace('鐵', '铁')
        .Replace('殺', '杀').Replace('碼', '码').Replace('頭', '头')
        .Replace('開', '开').Replace('獎', '奖').Replace('結', '结')
        .Replace('報', '报').Replace('圍', '围').Replace('絕', '绝')
        .Replace('鎖', '锁').Replace('數', '数').Replace('穩', '稳')
        .Replace('買', '买').Replace('輸', '输').Replace('會', '会')
        .Replace('贏', '赢').Replace('慘', '惨').Replace('計', '计');

    private static string SimplifyOcrText(string text) => text
        .Replace('馬', '马')
        .Replace('龍', '龙')
        .Replace('雞', '鸡')
        .Replace('豬', '猪')
        .Replace('點', '点')
        .Replace('宵', '肖')
        .Replace('紅', '红')
        .Replace('藍', '蓝')
        .Replace('綠', '绿')
        .Replace('門', '门')
        .Replace('無', '无')
        .Replace('錯', '错')
        .Replace('雙', '双')
        .Replace('單', '单')
        .Replace('兰', '蓝');

    public static string Normalize(string text) => Regex.Replace(SimplifyOcrText(text), @"[^\p{L}\p{N}]", "")
        .Replace("梁薇薇", "梁微微", StringComparison.Ordinal)
        .Replace('①', '1')
        .Replace('②', '2')
        .Replace('③', '3')
        .Replace('④', '4')
        .Replace('⑤', '5')
        .Replace('⑥', '6')
        .Replace('⑦', '7')
        .Replace('⑧', '8')
        .Replace('⑨', '9');
}
