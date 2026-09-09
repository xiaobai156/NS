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
    bool SingleValuePerIssue = false,
    IReadOnlyList<string>? PeerKeywords = null)
{
    public string Id => Label ?? Keyword;
    public string OutputLabel => Label ?? Keyword;
}

public enum RuleExtractionStatus
{
    Missing,
    Success,
    Conflict
}

public sealed record RuleExtractionResult(RuleExtractionStatus Status, string? Value)
{
    public static RuleExtractionResult Missing { get; } = new(RuleExtractionStatus.Missing, null);
    public static RuleExtractionResult Conflict { get; } = new(RuleExtractionStatus.Conflict, null);
    public static RuleExtractionResult Success(string value) => new(RuleExtractionStatus.Success, value);
}

public static class RuleEngine
{
    private static readonly Regex IssueRegex = new(@"(?<!\d)(?<issue>\d{1,6})\s*期", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CompactYearIssueRegex = new(@"(?<!\d)20\d{2}(?<issue>\d{3})\s*期", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BareIssueRegex = new(
        @"^\s*[【\[（({]?\s*(?<issue>\d{3,6})(?!\d)(?!\s*\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex YearIssueRegex = new(@"^\s*\d{4}\s*[-—/]\s*(?<issue>\d{3,6})(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string Zodiac = "马蛇龙兔虎牛鼠猪狗鸡猴羊";
    private const string ConflictMarker = "OCR-CONFLICT";
    // Only reviewed layouts may place part of one physical number row before
    // its issue cell. Generic rules must never borrow heading/previous data.
    private static readonly IReadOnlySet<string> SplitIssueNumberRuleIds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "宝典", "心水", "内幕", "强哥", "锁妖", "赛马会", "龙王", "红人馆", "老人味",
            "表弟", "祥瑞阁", "小马哥", "杀料", "金钱网", "天线宝杀"
        };

    public static IReadOnlyList<OcrRule> FindMatches(IEnumerable<string> localLines, IEnumerable<OcrRule> rules)
    {
        string text = Normalize(string.Concat(localLines));
        return rules.Where(rule => MatchesText(text, rule)).ToArray();
    }

    public static IReadOnlyList<OcrRule> FindMatches(
        string imagePath,
        IEnumerable<string> localLines,
        IEnumerable<OcrRule> rules,
        IEnumerable<OcrRule>? completeRules = null)
    {
        string[] lines = localLines.ToArray();
        OcrRule[] requestedRules = rules.ToArray();
        OcrRule[] identityRules = completeRules?.ToArray() ?? [];
        bool hasCompleteIdentityCatalog = completeRules is not null;
        string text = Normalize(string.Concat(lines));
        string folder = Path.GetFileName(Path.GetDirectoryName(imagePath)) ?? "";
        return requestedRules.Where(rule =>
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
            bool explicitIdentity = MatchesText(text, rule)
                && (string.IsNullOrWhiteSpace(rule.Section)
                    || text.Contains(Normalize(rule.Section), StringComparison.Ordinal));
            if (explicitIdentity)
                return true;
            return HasValueForAnyIssue(lines, rule)
                || (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                    && !RuleCatalog.NormalizeGroupName(rule.RequiredKeyword).Equals(
                        RuleCatalog.NormalizeGroupName(expectedFolder), StringComparison.OrdinalIgnoreCase)
                    && ContainsKeyword(text, Normalize(rule.RequiredKeyword))
                    && (hasCompleteIdentityCatalog
                        && identityRules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
                            && Normalize(other.RequiredKeyword ?? other.Keyword) == Normalize(rule.RequiredKeyword)) == 1
                        || !string.IsNullOrWhiteSpace(rule.Section) && text.Contains(Normalize(rule.Section), StringComparison.Ordinal)))
                || (hasCompleteIdentityCatalog
                    && identityRules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)) == 1
                    && expectedFolder.Equals(rule.RequiredKeyword ?? rule.Keyword, StringComparison.OrdinalIgnoreCase));
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
    {
        string? raw = ExtractValueCore(cloudLines, issue, rule, requireCloudKeyword: true);
        return raw == ConflictMarker ? null : raw;
    }

    private static string? ExtractValueCore(IEnumerable<string> cloudLines, int issue, OcrRule rule, bool requireCloudKeyword)
    {
        if (!rule.IgnoreIssue)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issue);
        string[] lines = cloudLines.Select(line => rule.StrictIssueBlock ? SimplifyFixedCardText(line) : line).ToArray();
        if (rule.IgnoreIssue)
        {
            int[] explicitIssues = FindIssues(lines).Distinct().ToArray();
            // IgnoreIssue means the card may omit a printed issue in legacy data,
            // not that the selected issue is evidence. Until a trusted publication
            // identity exists, an unnumbered card stays unverified; mixed/other
            // explicit issues are also rejected.
            if (explicitIssues.Length == 0 || explicitIssues.Any(actual => actual != issue))
                return null;
        }
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
            // Shared multi-author sheets must identify the author on the selected
            // issue row itself. A heading attached to an older issue is not proof.
            if (rule.Folder == "天机阁杀料"
                && !aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)))
                continue;
            // Section ownership is resolved from the nearest active section.
            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !HasSectionForIssueRow(lines, index, scopeStart, issue, rule))
                continue;

            string scopedLine = ScopeCandidateToPeerBoundary(line, rule, aliases, out bool peerClosed);
            candidates.Add(scopedLine);
            if (peerClosed)
                continue;
            string combined = scopedLine;
            for (int next = index + 1; next < lines.Length && next <= index + 4; next++)
            {
                if (OcrLayoutMarkers.IsBoundary(lines[next])
                    || ContainsIssueBoundary(lines[next], issue)
                    || ContainsPeerIdentity(lines[next], rule)
                    || combined.Length + lines[next].Length >= 180)
                    break;
                combined += " " + lines[next];
                candidates.Add(combined);
            }
        }

        // Section ownership is enforced while each issue candidate is built; do
        // not reopen the scope later based on a sibling section.

        bool keywordInTarget = candidates.Any(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)));
        bool keywordInHeading = aliases.Any(alias => HasKeywordInHeading(lines, alias));
        if (requireCloudKeyword && !rule.AllowValueWithoutKeyword && !keywordInTarget && !keywordInHeading)
            return null;

        if (rule.SingleValuePerIssue && rule.Type == "头")
            return ExtractSingleHeadPerIssue(lines, issue);

        if (rule.Type == "统计生肖")
            return ExtractMostFrequentZodiac(lines, issue, rule);

        if (rule.StrictIssueBlock || rule.Folder == "公式杀料")
            return ExtractStrictIssueBlock(lines, issue, rule);

        // This sheet prints the opening result before a separate nine-zodiac row.
        // Match that row within the requested issue, never combine the smaller tiers.
        if (rule.Id == "杰少九肖" && rule.Type == "九肖")
        {
            var nineValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (string candidate in candidates)
            {
                string normalized = Normalize(candidate);
                MatchCollection matches = Regex.Matches(normalized, $"九肖(?<value>[{Zodiac}]{{9}})(?![{Zodiac}])");
                foreach (Match nine in matches)
                {
                    string value = nine.Groups["value"].Value;
                    if (value.Distinct().Count() == 9)
                        nineValues.Add(value);
                }
            }
            return nineValues.Count == 0 ? null : nineValues.Count == 1 ? nineValues.Single() : ConflictMarker;
        }

        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int expectedCount))
        {
            string? numberValue = rule.IgnoreIssue
                ? ExtractNumbers(string.Join(' ', lines), expectedCount)
                : ExtractNumbersAroundIssue(lines, scopeStart, issue, expectedCount, rule);
            // Number extraction is provenance-aware. Never fall through to the
            // generic candidate concatenation and accidentally rebuild a failed row.
            return numberValue;
        }

        var observed = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> relevant = keywordInTarget
            ? candidates.Where(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)))
            : candidates;
        if (rule.Type is "生肖" or "单生肖" or "生肖组合" or "九肖")
            relevant = MaximalCandidates(relevant);
        foreach (string line in relevant)
        {
            string? value = ExtractTypedForRule(line, rule);
            if (value is not null)
                observed.Add(value);
        }
        // Never let an earlier copy of the selected issue silently win.
        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : ConflictMarker;

        if (rule.AllowNearbyValue
            && rule.Type is ("生肖" or "单生肖"))
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

    private static IEnumerable<string> MaximalCandidates(IEnumerable<string> source)
    {
        string[] values = source.Distinct(StringComparer.Ordinal).ToArray();
        return values.Where(candidate => !values.Any(other =>
            other.Length > candidate.Length
            && other.StartsWith(candidate + " ", StringComparison.Ordinal)));
    }

    private static string ScopeCandidateToPeerBoundary(
        string line, OcrRule rule, IReadOnlyList<string> aliases, out bool closed)
    {
        closed = false;
        if (rule.PeerKeywords is not { Count: > 0 })
            return line;
        string normalized = Normalize(line);
        (int Index, int Length)[] own = aliases
            .Select(alias => (Index: normalized.IndexOf(alias, StringComparison.Ordinal), Length: alias.Length))
            .Where(item => item.Index >= 0)
            .ToArray();
        if (own.Length == 0)
            return line;

        // Keep only this author's own cell. Text before the target author may
        // belong to a sibling author just as text after it may do so.
        int ownIndex = own.Min(item => item.Index);
        int ownEnd = own.Where(item => item.Index == ownIndex).Max(item => item.Index + item.Length);
        int peerIndex = rule.PeerKeywords
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Select(peer => normalized.IndexOf(peer, ownEnd, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (peerIndex >= 0)
            closed = true;
        int end = peerIndex >= 0 ? peerIndex : normalized.Length;
        return normalized[ownIndex..end];
    }

    private static bool ContainsPeerIdentity(string line, OcrRule rule)
    {
        if (rule.PeerKeywords is not { Count: > 0 })
            return false;
        string normalized = Normalize(line);
        return rule.PeerKeywords
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Select(Normalize)
            .Any(peer => normalized.Contains(peer, StringComparison.Ordinal));
    }

    private static string TrimAtPeerBoundary(string normalizedTail, OcrRule rule)
    {
        if (rule.PeerKeywords is not { Count: > 0 })
            return normalizedTail;
        int peer = rule.PeerKeywords
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Select(value => normalizedTail.IndexOf(value, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        return peer >= 0 ? normalizedTail[..peer] : normalizedTail;
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
            while (end < lines.Length
                && !ContainsIssue(lines[end], issue)
                && !ContainsIssueBoundary(lines[end], issue))
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

        return !foundIssue || heads.Count == 0 ? null : heads.Count == 1 ? $"{heads.Single()}头" : ConflictMarker;
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

    private static bool HasSectionForIssueRow(
        string[] lines, int issueIndex, int scopeStart, int issue, OcrRule rule)
    {
        string section = Normalize(rule.Section ?? string.Empty);
        if (section.Length == 0)
            return true;
        if (Normalize(lines[issueIndex]).Contains(section, StringComparison.Ordinal))
            return true;

        string[] identity = new[] { rule.Keyword, rule.RequiredKeyword ?? string.Empty, rule.Folder ?? string.Empty }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (ContainsIssue(lines[index], issue))
                continue;
            if (ContainsIssueBoundary(lines[index], issue))
                break;
            string current = Normalize(lines[index]);
            if (current.Length == 0)
                continue;
            if (current.Contains(section, StringComparison.Ordinal))
                return true;
            if (identity.Any(item => current.Contains(item, StringComparison.Ordinal)))
                continue;
            // The nearest non-identity heading is a sibling section boundary.
            return false;
        }
        return false;
    }

    private static string[] SummaryRowsForIssue(IEnumerable<string> source, int issue)
    {
        var rows = new List<string>();
        bool inTarget = false;
        foreach (string line in SplitInlineIssueRows(source))
        {
            if (ContainsIssue(line, issue))
                inTarget = true;
            else if (ContainsIssueBoundary(line, issue))
                inTarget = false;
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

                string tail = TrimAtPeerBoundary(line[(aliasIndex + aliasText.Length)..], rule);
                int forbiddenIndex = tail.IndexOf('禁');
                if (forbiddenIndex >= 0)
                {
                    string statusPrefix = Normalize(tail[..forbiddenIndex]);
                    // Between this author and its 禁 field only status marks may
                    // appear; another author or “待更新” closes this cell.
                    if (statusPrefix.Length > 0
                        && Regex.IsMatch(statusPrefix, @"[^正准準中错錯对對0-9]"))
                        continue;
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

        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
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
            if (ContainsIssue(line, issue))
            {
                inTargetIssue = true;
            }
            else if (ContainsIssueBoundary(line, issue))
            {
                if (inTargetIssue)
                    break;
                continue;
            }

            if (!inTargetIssue || !TryExtractFrequencyRow(line, out int count, out string rowValue))
                continue;
            if (rowValue.Length == 0)
            {
                for (int next = index + 1; next < lines.Length; next++)
                {
                    if (ContainsIssue(lines[next], issue)
                        || ContainsIssueBoundary(lines[next], issue)
                        || Regex.IsMatch(lines[next], @"(?<!\d)\d{1,2}\s*次"))
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
            rows.Add((count, rowValue));
        }

        if (rows.Count == 0)
            return null;

        int maximum = rows.Max(row => row.Count);
        (int Count, string Value)[] maximumRows = rows.Where(row => row.Count == maximum).ToArray();
        if (maximumRows.Any(row => row.Value.Length == 0))
            return null;
        string result = string.Concat(maximumRows
            .Select(row => row.Value)
            .SelectMany(item => item)
            .Distinct());
        return result.Length > 0 ? result : null;
    }

    private static bool TryExtractFrequencyRow(string line, out int count, out string value)
    {
        string normalized = Normalize(line);
        if (normalized.Contains("总次数", StringComparison.Ordinal)
            || normalized.Contains("總次數", StringComparison.Ordinal))
        {
            count = 0;
            value = string.Empty;
            return false;
        }
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

    public static RuleExtractionResult ExtractFinalResult(
        IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        string? raw = ExtractValueCore(
            RejectCrossIssueNearbyValue(cloudLines, issue, rule), issue, rule, requireCloudKeyword: false);
        return ToExtractionResult(raw);
    }

    public static string? ExtractFinalValue(IEnumerable<string> cloudLines, int issue, OcrRule rule) =>
        ExtractFinalResult(cloudLines, issue, rule).Value;

    public static RuleExtractionResult ExtractFinalResult(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        bool conflict = false;
        foreach (IGrouping<string, OcrLineEvidence> region in evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId + "\u001f" + item.RegionId, StringComparer.Ordinal))
        {
            OcrLineEvidence[] items = region.ToArray();
            RuleExtractionResult regional;
            if (items.All(item => item.Box is not null))
            {
                regional = ExtractFinalResult(items.Select(item => item.Text), issue, rule);
            }
            else
            {
                RuleExtractionResult holistic = ExtractFinalResult(items.Select(item => item.Text), issue, rule);
                if (holistic.Status == RuleExtractionStatus.Conflict)
                {
                    conflict = true;
                    continue;
                }
                var atomic = new HashSet<string>(StringComparer.Ordinal);
                foreach (OcrLineEvidence item in items)
                {
                    RuleExtractionResult itemResult = ExtractFinalResult(new[] { item.Text }, issue, rule);
                    if (itemResult.Status == RuleExtractionStatus.Conflict)
                        conflict = true;
                    else if (itemResult.Status == RuleExtractionStatus.Success)
                        atomic.Add(itemResult.Value!);
                }
                if (conflict || atomic.Count > 1)
                {
                    conflict = true;
                    continue;
                }
                regional = holistic.Status == RuleExtractionStatus.Success
                    && atomic.Count == 1 && atomic.Contains(holistic.Value!)
                    ? holistic
                    : RuleExtractionResult.Missing;
            }
            if (regional.Status == RuleExtractionStatus.Conflict)
                conflict = true;
            else if (regional.Status == RuleExtractionStatus.Success)
                observed.Add(regional.Value!);
        }
        if (conflict || observed.Count > 1)
            return RuleExtractionResult.Conflict;
        return observed.Count == 1
            ? RuleExtractionResult.Success(observed.Single())
            : RuleExtractionResult.Missing;
    }

    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule) =>
        ExtractFinalResult(evidence, issue, rule).Value;

    private static RuleExtractionResult ToExtractionResult(string? raw) =>
        raw == ConflictMarker
            ? RuleExtractionResult.Conflict
            : raw is null
                ? RuleExtractionResult.Missing
                : RuleExtractionResult.Success(raw);

    private static IEnumerable<string> RejectCrossIssueNearbyValue(IEnumerable<string> source, int issue, OcrRule rule)
    {
        string[] lines = source.ToArray();
        if (!rule.StrictIssueBlock || !rule.AllowNearbyValue
            || (rule.Id, rule.Folder, rule.Type) == ("骁腾杀肖", "骁腾系列", "生肖"))
            return lines;
        int target = Array.FindIndex(lines, line => ContainsIssue(line, issue));
        if (target < 0)
            return lines;
        bool hasEarlierIssue = lines.Take(target).Any(line =>
            ContainsIssue(line, issue) || ContainsIssueBoundary(line, issue));
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
        if (rule.IgnoreIssue && !FindIssues(lines).Any())
            return "已找到资料，但资料本身没有可核验期数";
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
        if (SplitIssueNumberRuleIds.Contains(rule.Id)
            && rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int reviewedExpectedCount))
        {
            var reviewed = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;

                // The special adapter is only for a row that genuinely straddles
                // the issue cell: a numeric payload must already exist immediately
                // before this issue without crossing another issue. Ordinary
                // complete target blocks keep using the strict block validator.
                int previous = index - 1;
                // A real centred row has its left data cell immediately before
                // the target issue cell. Crossing an opening separator or any
                // earlier row would reintroduce the previous-period borrowing bug.
                if (previous < 0 || ContainsAnyIssue(lines[previous])
                    || IsOpeningOnlySeparator(lines[previous])
                    || !IsNumberContinuation(lines[previous], reviewedExpectedCount, rule, issue))
                    continue;

                string? split = ExtractStrictCenteredNumberWindow(
                    lines, index, issue, reviewedExpectedCount, rule);
                if (split is not null)
                    reviewed.Add(split);
            }
            if (reviewed.Count > 0)
                return reviewed.Count == 0 ? null : reviewed.Count == 1 ? reviewed.Single() : ConflictMarker;
        }

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
        // Do not infer a centred/split row from page-level leading numbers.
        // Reviewed wrapped cards are handled below by TryExtractWrappedCardRow,
        // which consumes explicit neighbouring physical rows instead of TakeLast
        // data from an earlier issue.
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
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
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
                .LastOrDefault(i => ContainsIssue(lines[i], issue)
                    || ContainsIssueBoundary(lines[i], issue), -1);
            int nearbyStart = previousIssueIndex >= 0 ? index : Math.Max(0, index - 4);
            foreach (string line in lines.Skip(nearbyStart).Take(9))
            {
                // Do not borrow a nearby value across another issue boundary.
                if (ContainsIssueBoundary(line, issue) && !ContainsIssue(line, issue))
                    break;
                string normalized = Normalize(line);
                if (Regex.IsMatch(normalized, $"^[{Zodiac}]$"))
                    standaloneValues.Add(SimplifyOcrText(normalized));
            }
        }
        if (standaloneValues.Count > 0)
            return standaloneValues.Count == 0 ? null : standaloneValues.Count == 1 ? standaloneValues.Single() : ConflictMarker;

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            int previousIssueIndex = Enumerable.Range(0, index)
                .LastOrDefault(i => ContainsIssue(lines[i], issue)
                    || ContainsIssueBoundary(lines[i], issue), -1);
            int nearbyStart = previousIssueIndex >= 0 ? index : Math.Max(0, index - 1);
            foreach (string line in lines.Skip(nearbyStart).Take(3))
            {
                if (ContainsIssueBoundary(line, issue) && !ContainsIssue(line, issue))
                    break;
                string? value = ExtractSingleZodiac(line);
                if (value is not null)
                    values.Add(value);
            }
        }
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
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
        return values.Count == 0 ? null : values.Count == 1 ? values[0].ToString() : ConflictMarker;
    }

    private static string? ExtractLeadingSplitNumberTable(
        string text, MatchCollection periods, int issue, OcrRule rule)
    {
        if (!SplitIssueNumberRuleIds.Contains(rule.Id))
            return null;
        // These explicitly reviewed cards have a physical row that can straddle
        // the issue/opening cells. Every other rule is forbidden from this repair.
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
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
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
            if (int.TryParse(run.Value, out _)
                && Regex.IsMatch(following, @"^\s*(?:个(?:中特码|特码)?|個(?:码中特碼|特碼)?|码|碼|计|計)"))
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
            if (block == ConflictMarker)
                return ConflictMarker;
            if (block.Length == 0)
                return null;
        }
        if (Regex.IsMatch(block, @"[?？]"))
            return null;
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
            string field = SimplifyOcrText(block);
            int marker = field.IndexOf("九肖", StringComparison.Ordinal);
            if (marker < 0)
                return null;
            field = field[(marker + "九肖".Length)..];
            int tierEnd = new[] { "六肖", "三肖", "一肖" }
                .Select(tier => field.IndexOf(tier, StringComparison.Ordinal))
                .Where(index => index >= 0)
                .DefaultIfEmpty(field.Length)
                .Min();
            field = field[..tierEnd];
            MatchCollection zodiacs = Regex.Matches(field, $"[{Zodiac}]");
            if (zodiacs.Count != 9)
                return null;
            string nineValue = string.Concat(zodiacs.Select(match => match.Value));
            return nineValue.Distinct().Count() == 9 ? nineValue : null;
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
            "半波" => @"(?:杀半波\s*)?(?:(?:红红半波|粉红半波|蓝黑半波)\s*)+",
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
        if (rule.Type is "色单双" or "半波")
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
        MatchCollection markers = Regex.Matches(block, @"[=＝]\s*杀");
        if (markers.Count == 0)
            return false;
        Match marker = markers[^1];
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
        MatchCollection markers = Regex.Matches(block, pattern, RegexOptions.Singleline);
        if (markers.Count == 0)
            return string.Empty;
        bool formulaRule = rule.Id is "公式杀两肖肖" or "公式杀两尾尾";
        if (!formulaRule && markers.Count > 1)
        {
            var repeatedValues = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < markers.Count; index++)
            {
                Match repeatedMarker = markers[index];
                int start = repeatedMarker.Index + repeatedMarker.Length;
                int end = index + 1 < markers.Count ? markers[index + 1].Index : block.Length;
                string candidate = ExtractImmediateDragonflyValue(block[start..end]);
                if (candidate.Length == 0 || candidate == ConflictMarker)
                    return ConflictMarker;
                repeatedValues.Add(candidate);
            }
            return repeatedValues.Count == 1 ? repeatedValues.Single() : ConflictMarker;
        }

        Match marker = formulaRule ? markers[^1] : markers[0];
        string payload = block[(marker.Index + marker.Length)..];
        if (rule.Id == "绿格子双杀")
        {
            MatchCollection framedValues = Regex.Matches(payload,
                @"【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)");
            if (framedValues.Count != 1)
                return string.Empty;
            return framedValues[0].Groups["value"].Value;
        }
        return ExtractImmediateDragonflyValue(payload);
    }

    private static string ExtractImmediateDragonflyValue(string payload)
    {
        string framedPayload = Regex.Replace(payload, @"^[：:，,、\-\s]*", "");
        var leadingFrames = new List<string>();
        int frameOffset = 0;
        while (frameOffset < framedPayload.Length)
        {
            Match frame = Regex.Match(framedPayload[frameOffset..],
                @"^(?:【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\{(?<value>[^}]+)\})");
            if (!frame.Success)
                break;
            leadingFrames.Add(frame.Groups["value"].Value);
            frameOffset += frame.Length;
            Match separator = Regex.Match(framedPayload[frameOffset..], @"^[：:，,、+\-\s]*");
            frameOffset += separator.Length;
        }
        if (leadingFrames.Count > 0)
        {
            string[] distinct = leadingFrames.Distinct(StringComparer.Ordinal).ToArray();
            return distinct.Length == 1 ? distinct[0] : ConflictMarker;
        }
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
        rules.Select(rule => ResultValues.IsConflict(values, rule.Id)
            ? $"缺失（同一期结果冲突，待核对） {rule.OutputLabel}"
            : values.TryGetValue(rule.Id, out string? value)
            ? $"{FormatForOutput(rule, value)} {rule.OutputLabel}"
            : missingReasons is not null && missingReasons.TryGetValue(rule.Id, out string? reason)
                ? $"缺失（{reason}） {rule.OutputLabel}"
                : $"缺失 {rule.OutputLabel}").ToArray();

    public static bool IsCanonicalValueValid(OcrRule rule, string value)
    {
        value = SimplifyOcrText(value.Trim());
        if (rule.Type == "五行")
        {
            return value.Length == 1 && "金木水火土".Contains(value[0])
                || value.Length == 4 && value.All("金木水火土".Contains)
                    && value.Distinct().Count() == 4;
        }
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal))
            return IsFormattedOutputValueValid(rule, Regex.Replace(value, @"\s+", ","));
        if (rule.Type is "尾数组合" or "头数组合")
            return IsFormattedOutputValueValid(rule, value.Replace('+', ' '));
        return IsFormattedOutputValueValid(rule, value);
    }

    public static bool IsFormattedOutputValueValid(OcrRule rule, string value)
    {
        value = SimplifyOcrText(value.Trim());
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int count))
        {
            string[] numbers = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return numbers.Length == count && numbers.Distinct(StringComparer.Ordinal).Count() == count
                && numbers.All(number => number.Length == 2 && int.TryParse(number, out int parsed)
                    && parsed is >= 1 and <= 49);
        }
        if (rule.Type is "生肖" or "单生肖")
            return value.Length == 1 && Zodiac.Contains(value[0]);
        if (rule.Type == "生肖组合")
            return value.Length == 2 && value.All(Zodiac.Contains) && value.Distinct().Count() == 2;
        if (rule.Type == "九肖")
            return value.Length == 9 && value.All(Zodiac.Contains) && value.Distinct().Count() == 9;
        if (rule.Type == "统计生肖")
            return value.Length > 0 && value.Length <= Zodiac.Length && value.All(Zodiac.Contains)
                && value.Distinct().Count() == value.Length;
        if (rule.Type == "头")
            return Regex.IsMatch(value, "^[0-4]头$");
        if (rule.Type == "尾")
        {
            if (rule.Id is "亚太尾" or "战澳尾")
            {
                string[] tails = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return tails.Length == 2 && tails.Distinct(StringComparer.Ordinal).Count() == 2
                    && tails.All(item => Regex.IsMatch(item, "^[0-9]尾$"));
            }
            return Regex.IsMatch(value, "^[0-9]尾$");
        }
        if (rule.Type == "尾数组合")
        {
            string[] tails = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tails.Length == 2 && tails.Distinct(StringComparer.Ordinal).Count() == 2
                && tails.All(item => Regex.IsMatch(item, "^[0-9]尾$"));
        }
        if (rule.Type == "头数组合")
        {
            string[] heads = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return heads.Length > 0 && heads.Length <= 5 && heads.Distinct(StringComparer.Ordinal).Count() == heads.Length
                && heads.All(item => Regex.IsMatch(item, "^[0-4]头$"));
        }
        if (rule.Type == "缺尾") return Regex.IsMatch(value, "^[0-9]尾$");
        if (rule.Type == "缺头") return Regex.IsMatch(value, "^[0-4]头$");
        if (rule.Type is "五行" or "单五行") return value.Length == 1 && "金木水火土".Contains(value[0]);
        if (rule.Type is "色单双" or "半波") return Regex.IsMatch(value, "^[红蓝绿][单双]$");
        if (rule.Type == "合") return Regex.IsMatch(value, "^(?:0[1-9]|1[0-3])合$");
        if (rule.Type == "段") return Regex.IsMatch(value, "^[0-7]段$");
        if (rule.Type == "半头") return Regex.IsMatch(value, "^[0-4]头[单双]$");
        return false;
    }

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

    private static string? ExtractTypedForRule(string tail, OcrRule rule)
    {
        if (rule.Id == "翩翩公子肖" && rule.Type == "生肖")
        {
            string beforeOpening = BeforeOpeningResult(SimplifyOcrText(tail));
            MatchCollection matches = Regex.Matches(beforeOpening, $"[{Zodiac}]");
            string[] distinct = matches.Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
            return distinct.Length > 1 ? ConflictMarker
                : matches.Count > 0 && distinct.Length == 1 ? distinct[0] : null;
        }
        if (HasConflictingSingleValues(tail, rule.Type))
            return ConflictMarker;
        return ExtractTyped(tail, rule.Type);
    }

    private static string? ExactSemanticField(string text, params string[] markers)
    {
        string simplified = SimplifyOcrText(text);
        int markerIndex = -1;
        int markerLength = 0;
        foreach (string markerText in markers.Select(SimplifyOcrText))
        {
            int found = simplified.IndexOf(markerText, StringComparison.Ordinal);
            if (found >= 0 && (markerIndex < 0 || found < markerIndex))
            {
                markerIndex = found;
                markerLength = markerText.Length;
            }
        }
        if (markerIndex < 0)
            return null;
        string raw = simplified[(markerIndex + markerLength)..].Trim();
        raw = raw.TrimStart(':', '：').Trim();
        return raw.Length == 0 ? null : raw;
    }

    private static bool HasConflictingSingleValues(string tail, string type)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(tail));
        IEnumerable<string> values = type switch
        {
            "合" => Regex.Matches(text, @"(?<!\d)(?:0?[1-9]|1[0-3])合")
                .Select(match => $"{int.Parse(match.Value[..^1]):00}合"),
            "段" => Regex.Matches(text, @"(?<!\d)[0-7]\s*段")
                .Select(match => Regex.Replace(match.Value, @"\s+", "")),
            "尾" => TailValueCandidates(text),
            "尾数组合" => TailPairCandidates(text),
            "头" => HeadValueCandidates(text),
            "单五行" => Regex.Matches(text, @"[金木水火土]").Select(match => match.Value),
            "半头" => Regex.Matches(text, @"(?<!\d)[0-4]\s*头\s*[单双]")
                .Select(match => Regex.Replace(match.Value, @"\s+", "")),
            "色单双" or "半波" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
                .Select(match => match.Value.Replace("波", "", StringComparison.Ordinal)),
            "生肖" or "单生肖" => Regex.Matches(text, $"[{Zodiac}]").Select(match => match.Value),
            _ => Array.Empty<string>()
        };
        return values.Distinct(StringComparer.Ordinal).Take(2).Count() > 1;
    }

    private static IEnumerable<string> HeadValueCandidates(string text)
    {
        var values = new List<string>();
        int delimiter = Math.Max(text.LastIndexOf('：'), text.LastIndexOf(':'));
        if (delimiter >= 0)
        {
            string field = text[(delimiter + 1)..];
            values.AddRange(Regex.Matches(field,
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头")
                .Where(match => !Regex.IsMatch(
                    field[..match.Index], @"(?:杀|殺|禁|禁止)\s*$"))
                .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        }
        values.AddRange(Regex.Matches(text, @"[【\[](?<head>[0-4])\s*[】\]]\s*头")
            .Select(match => $"{match.Groups["head"].Value}头"));
        values.AddRange(Regex.Matches(text, @"买\s*(?<head>[0-4零一二三四])\s*头")
            .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        return values;
    }

    private static IEnumerable<string> TailValueCandidates(string text)
    {
        var values = Regex.Matches(text, @"(?<!\d)[0-9]\s*尾")
            .Select(match => Regex.Replace(match.Value, @"\s+", ""))
            .ToList();
        int marker = text.LastIndexOf('尾');
        if (marker < 0)
            return values;
        foreach (Match run in Regex.Matches(text[(marker + 1)..], @"(?<!\d)[0-9]+(?!\d)"))
        {
            if (run.Value.Distinct().Count() == 1)
                values.Add($"{run.Value[0]}尾");
        }
        return values;
    }

    private static IEnumerable<string> TailPairCandidates(string text)
    {
        var values = new List<string>();
        foreach (Match pair in Regex.Matches(text,
            @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?"))
        {
            char[] digits = [pair.Groups["first"].Value[0], pair.Groups["second"].Value[0]];
            Array.Sort(digits);
            values.Add(new string(digits));
        }
        foreach (Match compact in Regex.Matches(text, @"(?<!\d)(?<digits>[0-9]{2})\s*尾"))
        {
            char[] digits = compact.Groups["digits"].Value.ToCharArray();
            Array.Sort(digits);
            values.Add(new string(digits));
        }
        return values;
    }

    private static string? ExtractTyped(string tail, string type)
    {
        tail = SimplifyOcrText(tail);
        string beforeOpening = BeforeOpeningResult(tail);
        string withoutIssueBeforeOpening = RemoveIssue(beforeOpening);
        if (type == "尾" && (tail.Contains("亚太地区八尾", StringComparison.Ordinal) || tail.Contains("团队八尾", StringComparison.Ordinal)))
        {
            string? raw = ExactSemanticField(withoutIssueBeforeOpening, "亚太地区八尾", "团队八尾");
            if (raw is null || !Regex.IsMatch(raw, @"^[0-9](?:\s*[0-9]){7}$"))
                return null;
            string digits = string.Concat(raw.Where(char.IsDigit));
            if (digits.Distinct().Count() != 8)
                return null;
            int[] present = digits.Select(c => c - '0').ToArray();
            int[] missing = Enumerable.Range(0, 10).Where(n => !present.Contains(n)).ToArray();
            return missing.Length == 2
                ? string.Join(' ', missing.Select(n => $"{n}尾"))
                : null;
        }
        if (type == "五行" && (tail.Contains("亚太地区四行", StringComparison.Ordinal) || tail.Contains("团队四行", StringComparison.Ordinal)))
        {
            string? raw = ExactSemanticField(withoutIssueBeforeOpening, "亚太地区四行", "团队四行");
            if (raw is null || !Regex.IsMatch(raw, @"^[金木水火土](?:\s*[金木水火土]){3}$"))
                return null;
            string elements = string.Concat(raw.Where("金木水火土".Contains));
            return elements.Distinct().Count() == 4
                ? string.Concat("金木水火土".Where(e => !elements.Contains(e)))
                : null;
        }
        if (type == "生肖组合" && (tail.Contains("亚太地区十肖", StringComparison.Ordinal) || tail.Contains("团队十肖", StringComparison.Ordinal)))
        {
            string? raw = ExactSemanticField(withoutIssueBeforeOpening, "亚太地区十肖", "团队十肖");
            if (raw is null || !Regex.IsMatch(raw, $@"^[{Zodiac}](?:\s*[{Zodiac}]){{9}}$"))
                return null;
            string zodiacs = string.Concat(raw.Where(Zodiac.Contains));
            return zodiacs.Distinct().Count() == 10
                ? string.Concat(Zodiac.Where(z => !zodiacs.Contains(z)))
                : null;
        }
        if (type == "头" && (tail.Contains("亚太地区四头", StringComparison.Ordinal) || tail.Contains("团队四头", StringComparison.Ordinal)))
        {
            string? raw = ExactSemanticField(withoutIssueBeforeOpening, "亚太地区四头", "团队四头");
            if (raw is null || !Regex.IsMatch(raw, @"^[0-4](?:\s*[0-4]){3}$"))
                return null;
            string heads = string.Concat(raw.Where(c => c is >= '0' and <= '4'));
            return heads.Distinct().Count() == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !heads.Contains((char)('0' + n)))}头"
                : null;
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
            int[] rawTails = Regex.Matches(tailText, @"(?<!\d)[0-9]+(?!\d)")
                .SelectMany(match => marker >= 0 || match.Value.Length == 1 || match.Value.Length == 9
                    ? match.Value.Select(character => character - '0')
                    : [])
                .ToArray();
            int[] tails = rawTails.Distinct().ToArray();
            if (rawTails.Length != 9 || tails.Length != 9)
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
            MatchCollection matches = Regex.Matches(zodiacText, $"[{Zodiac}]");
            int expected = type == "九肖" ? 9 : 2;
            if (matches.Count != expected)
                return null;
            string value = string.Concat(matches.Select(match => match.Value));
            return value.Distinct().Count() == expected ? value : null;
        }

        if (type == "单五行")
        {
            Match element = Regex.Match(beforeOpening, @"[【\[]?(?<element>[金木水火土])[】\]]?");
            return element.Success ? element.Groups["element"].Value : null;
        }

        if (type == "五行")
        {
            string[] elements = Regex.Matches(beforeOpening, "[金木水火土]")
                .Select(match => match.Value)
                .ToArray();
            return elements.Length == 4 && elements.Distinct(StringComparer.Ordinal).Count() == 4
                ? string.Concat(elements)
                : null;
        }

        if (type == "尾数组合")
        {
            Match combination = Regex.Match(beforeOpening, @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?");
            if (!combination.Success)
                combination = Regex.Match(beforeOpening, @"[【\[](?<first>[0-9])\s*(?<second>[0-9])\s*[】\]]?尾");
            if (combination.Success)
            {
                string first = combination.Groups["first"].Value;
                string second = combination.Groups["second"].Value;
                return first != second ? $"{first}尾+{second}尾" : null;
            }

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\d)(?<digits>[0-9]{2})\s*尾");
            return compact.Success && compact.Groups["digits"].Value[0] != compact.Groups["digits"].Value[1]
                ? $"{compact.Groups["digits"].Value[0]}尾+{compact.Groups["digits"].Value[1]}尾"
                : null;
        }

        if (type is "头数组合" or "缺头")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("四头出特", StringComparison.Ordinal);
            string valueText = marker >= 0 ? withoutIssue[(marker + "四头出特".Length)..] : withoutIssue;
            char[] rawHeads = valueText
                .Where(character => character is >= '0' and <= '4')
                .ToArray();
            char[] heads = rawHeads.Distinct().ToArray();
            if (heads.Length != 4 || type == "缺头" && rawHeads.Length != 4)
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

        if (type is "色单双" or "半波")
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

        if (type == "生肖")
            return ExtractSingleZodiac(beforeOpening);

        Match match = type switch
        {
            "生肖" => Match.Empty,
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

    private static string? ExtractNumbersAroundIssue(
        string[] lines, int scopeStart, int issue, int expectedCount, OcrRule rule)
    {
        if (SplitIssueNumberRuleIds.Contains(rule.Id))
        {
            var splitObserved = new HashSet<string>(StringComparer.Ordinal);
            for (int index = scopeStart; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;
                string? split = ExtractReviewedSplitNumberWindow(
                    lines, scopeStart, index, issue, expectedCount, rule);
                if (split is not null)
                    splitObserved.Add(split);
            }
            if (splitObserved.Count > 0)
                return splitObserved.Count == 1 ? splitObserved.Single() : ConflictMarker;
        }

        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = scopeStart; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;

            string target = Normalize(lines[index]);
            bool followingTable = rule.Id == "蓝色" || target.Contains("特码开在", StringComparison.Ordinal);
            bool precedingTable = rule.Id == "彩图" || target.Contains("开奖结果", StringComparison.Ordinal);
            string? directional = followingTable
                ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: true, rule)
                : precedingTable
                    ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: false, rule)
                    : null;
            if (directional is not null)
            {
                observed.Add(directional);
                continue;
            }
            if (followingTable || precedingTable)
                continue;

            var parts = new List<string>();
            string firstText = TextAfterIssue(lines[index], issue);
            string first = ScopeNumberPayload(firstText, rule, expectedCount);
            string firstSimplified = SimplifyOcrText(firstText);
            bool explicitIssueField = Regex.IsMatch(firstSimplified,
                @"^\s*(?:(?:杀|殺)(?:\s*[:：]\s*(?:杀|殺))?|(?:绝杀|絕殺)[一二三四五六七八九十0-9]*[码碼])");
            bool firstOwned = explicitIssueField
                || IsNumberContinuation(firstText, expectedCount, rule, issue)
                || ExtractNumbers(first, expectedCount) is not null;
            if (firstOwned && !string.IsNullOrWhiteSpace(first))
                parts.Add(first);
            for (int next = index + 1; next < lines.Length; next++)
            {
                if (ContainsIssueBoundary(lines[next], issue))
                    break;
                if (SplitIssueNumberRuleIds.Contains(rule.Id) && IsOpeningOnlySeparator(lines[next]))
                    continue;
                if (!IsNumberContinuation(lines[next], expectedCount, rule, issue))
                    break;
                parts.Add(ScopeNumberPayload(lines[next], rule, expectedCount));
            }

            string? value = ExtractNumbers(string.Join(' ', parts), expectedCount);
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private static bool HasCurrentFieldStartAfterEarlierIssue(
        string[] lines, int scopeStart, int issueIndex, int issue, OcrRule rule, int expectedCount)
    {
        int previousIssue = -1;
        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (!ContainsIssue(lines[index], issue)
                && !ContainsIssueBoundary(lines[index], issue))
                continue;
            previousIssue = index;
            break;
        }
        if (previousIssue < 0)
            return true;

        for (int index = previousIssue + 1; index < issueIndex; index++)
        {
            if (IsNumberRowStart(lines[index]) || ContainsOwnNumberIdentity(lines[index], rule))
                return true;
        }

        if (expectedCount >= 35
            && Enumerable.Range(previousIssue + 1, issueIndex - previousIssue - 1)
                .Any(index => IsOpeningOnlySeparator(lines[index])))
        {
            int immediate = issueIndex - 1;
            if (immediate > previousIssue
                && !ContainsAnyIssue(lines[immediate])
                && IsNumberContinuation(lines[immediate], expectedCount, rule))
                return true;
        }
        return false;
    }

    private static bool ContainsOwnNumberIdentity(string line, OcrRule rule)
    {
        string normalized = Normalize(line);
        return new[] { rule.Keyword, rule.Label ?? string.Empty, rule.RequiredKeyword ?? string.Empty, rule.Section ?? string.Empty }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Any(alias => normalized.Contains(alias, StringComparison.Ordinal));
    }

    private static string? ExtractStrictCenteredNumberWindow(
        string[] lines, int issueIndex, int issue, int expectedCount, OcrRule rule)
    {
        int previous = issueIndex - 1;
        if (previous < 0 || ContainsAnyIssue(lines[previous])
            || IsOpeningOnlySeparator(lines[previous])
            || !HasCurrentFieldStartAfterEarlierIssue(lines, 0, issueIndex, issue, rule, expectedCount))
            return null;

        string immediate = ScopeNumberPayload(lines[previous], rule, expectedCount);
        if (!IsNumberContinuation(lines[previous], expectedCount, rule, issue))
            return null;

        var leftCandidates = new List<List<string>>
        {
            new() { immediate }
        };

        var expanded = new List<string> { immediate };
        bool foundFieldStart = IsNumberRowStart(lines[previous]);
        for (int index = previous - 1; !foundFieldStart && index >= 0; index--)
        {
            if (ContainsIssueBoundary(lines[index], issue) || IsOpeningOnlySeparator(lines[index])
                || Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (!IsNumberContinuation(lines[index], expectedCount, rule, issue))
                break;
            expanded.Insert(0, ScopeNumberPayload(lines[index], rule, expectedCount));
            if (IsNumberRowStart(lines[index]))
                foundFieldStart = true;
        }
        if (foundFieldStart && expanded.Count > 1)
            leftCandidates.Add(expanded);

        string centreText = SimplifyOcrText(TextAfterIssue(lines[issueIndex], issue));
        centreText = Regex.Split(centreText,
            $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]")[0];
        string centre = ScopeNumberPayload(centreText, rule, expectedCount);

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
            if (!IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }

            string right = ScopeNumberPayload(lines[index], rule, expectedCount);
            foreach (List<string> candidate in candidates)
                candidate.Add(right);
            sawRightPayload = true;
            resolved = ResolveComplete();
            if (resolved is not null)
                return resolved;
        }
        return null;
    }

    private static string? ExtractReviewedSplitNumberWindow(
        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount, OcrRule rule)
    {
        bool mayUseLeft = HasCurrentFieldStartAfterEarlierIssue(lines, scopeStart, issueIndex, issue, rule, expectedCount);
        int left = issueIndex;
        if (mayUseLeft)
        {
            for (int index = issueIndex - 1; index >= scopeStart; index--)
            {
                if (ContainsIssueBoundary(lines[index], issue))
                    break;
                left = index;
                if (IsNumberRowStart(lines[index]))
                    break;
            }
        }

        var parts = new List<string>();
        for (int index = left; index < issueIndex; index++)
        {
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                return null;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (IsNumberContinuation(lines[index], expectedCount, rule, issue))
                parts.Add(ScopeNumberPayload(lines[index], rule, expectedCount));
        }
        string centre = ScopeNumberPayload(TextAfterIssue(lines[issueIndex], issue), rule, expectedCount);
        if (!string.IsNullOrWhiteSpace(centre))
            parts.Add(centre);
        string? alreadyComplete = ExtractNumbers(string.Join(' ', parts), expectedCount);
        if (alreadyComplete is not null)
            return alreadyComplete;

        bool sawRightPayload = false;
        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            if (ContainsIssueBoundary(lines[index], issue))
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (IsNumberRowStart(lines[index]) && sawRightPayload)
                break;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (!IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }
            parts.Add(ScopeNumberPayload(lines[index], rule, expectedCount));
            sawRightPayload = true;
            string? complete = ExtractNumbers(string.Join(' ', parts), expectedCount);
            if (complete is not null)
                return complete;
        }
        return ExtractNumbers(string.Join(' ', parts), expectedCount);
    }

    private static string? ExtractDirectionalNumberTable(
        string[] lines, int issueIndex, int issue, int expectedCount, bool forward, OcrRule rule)
    {
        int start;
        int end;
        if (forward)
        {
            start = issueIndex + 1;
            end = start;
            while (end < lines.Length && !ContainsIssueBoundary(lines[end], issue))
                end++;
        }
        else
        {
            end = issueIndex;
            start = issueIndex - 1;
            while (start >= 0 && !ContainsIssueBoundary(lines[start], issue))
                start--;
            start++;
        }

        var payload = new List<string>();
        bool started = false;
        for (int index = start; index < end; index++)
        {
            if (ContainsIssueBoundary(lines[index], issue))
                break;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (!IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (started)
                    break;
                continue;
            }
            payload.Add(ScopeNumberPayload(RemoveIssue(lines[index]), rule, expectedCount));
            started = true;
        }
        return payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);
    }

    private static string ScopeNumberPayload(
        string line, OcrRule rule, int expectedCount = 0)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(RemoveIssue(line)));

        // Printed cardinalities are layout metadata, not lottery numbers.
        // Examples: 35码赛马会, 36计, 12码→, [12个特码].
        text = Regex.Replace(text,
            @"(?<!\d)\d{1,2}\s*(?:个(?:中特码|特码)?|個(?:码中特碼|特碼)?|码|碼|计|計)",
            " ");

        // If OCR left title noise (including 100/888 etc.) before a complete
        // bracketed field, prefer the bracket only when its own cardinality is
        // exactly the configured rule count. This does not make a foreign bracket
        // valid: IsNumberContinuation still proves ownership before accepting it.
        if (expectedCount > 0)
        {
            string[] exact = Regex.Matches(text,
                    @"[【\[](?<payload>[^】\]]+)[】\]]")
                .Select(match => match.Groups["payload"].Value)
                .Where(payload =>
                {
                    string[]? parsed = ParseNumbers(payload);
                    return parsed is not null && parsed.Length == expectedCount
                        && parsed.Distinct(StringComparer.Ordinal).Count() == expectedCount;
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (exact.Length == 1)
                return exact[0];
            // More than one complete bracket is deliberately left intact so the
            // normal complete-field conflict checks can reject ambiguity.
        }

        Match firstNumber = Regex.Match(text, @"\d");
        if (!firstNumber.Success)
            return text;

        foreach (Match word in Regex.Matches(text, @"\p{L}+"))
        {
            if (word.Index <= firstNumber.Index)
                continue;
            if (word.Value.All(Zodiac.Contains))
                continue;
            return text[..word.Index].TrimEnd();
        }
        return text;
    }

    private static bool IsNumberContinuation(
        string line, int expectedCount, OcrRule rule, int selectedIssue = 0)
    {
        string simplified = SimplifyOcrText(line);
        if (ContainsAnyIssue(line)
            || selectedIssue > 0 && IsBareIssueBoundary(line, selectedIssue)
            || ContainsPeerIdentity(line, rule)
            || Regex.IsMatch(simplified, @"参考|旁栏|排行|统计|说明"))
            return false;

        string scoped = ScopeNumberPayload(simplified, rule, expectedCount);
        string[]? numbers = ParseNumbers(scoped);
        if (numbers is null || numbers.Length == 0)
            return false;

        // Opening/result suffixes are not part of field ownership. Evaluate the
        // decoration on the data side of “开”, so “杀02...开??” remains a proven
        // kill-number field and “48开888” remains a pure numeric continuation.
        string ownershipText = BeforeOpeningResult(simplified);
        bool hasLetters = Regex.IsMatch(ownershipText, @"\p{L}");
        if (!hasLetters)
            return true;

        string normalized = Normalize(line);
        bool ownIdentity = ContainsOwnNumberIdentity(line, rule);
        string decoration = Regex.Replace(
            ownershipText, @"[0-9\s,，.。:：*【】\[\]()（）?？←→]+", string.Empty);
        bool structuralField = Regex.IsMatch(decoration,
            @"^(?:(?:杀|殺){1,3}|开|開|禁|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺|绝杀[一二三四五六七八九十0-9]+码|絕殺[一二三四五六七八九十0-9]+碼|封杀|封殺)$");
        bool explicitBracketField = HasExactBracketPayload(simplified, expectedCount)
            && Regex.IsMatch(simplified,
                @"(?:杀码|殺碼|杀码|杀(?:特)?码|殺(?:特)?碼|绝杀|絕殺|禁码|禁碼)[^【\[]*[【\[]");
        if (!ownIdentity && !structuralField && !explicitBracketField)
            return false;

        // Brackets prove value grouping only after the line itself has been
        // proven to belong to this field; a foreign bracket cannot claim it.
        if (HasExactBracketPayload(scoped, expectedCount)
            || explicitBracketField)
            return true;
        return true;
    }

    private static bool IsBareIssueBoundary(string line, int selectedIssue)
    {
        string trimmed = SimplifyOcrText(line).Trim();
        if (!Regex.IsMatch(trimmed, @"^\d{4,6}$")
            || !int.TryParse(trimmed, out int actual)
            || actual == selectedIssue)
            return false;
        string selected = selectedIssue.ToString();
        if (trimmed.Length != selected.Length)
            return false;
        long difference = Math.Abs((long)actual - selectedIssue);
        // Ambiguous compact digits near the selected period are treated as an
        // issue boundary. Under the accuracy-first contract, ambiguity is missing.
        return difference <= Math.Max(100L, selectedIssue / 10L);
    }

    private static bool HasExactBracketPayload(string line, int expectedCount)
    {
        foreach (Match bracket in Regex.Matches(
            BeforeOpeningResult(line), @"[【\[（(](?<value>[^】\]）)]*)[】\]）)]"))
        {
            if (FormatNumbers(ParseNumbers(bracket.Groups["value"].Value), expectedCount) is not null)
                return true;
        }
        return false;
    }

    private static bool IsOpeningOnlySeparator(string line) =>
        Regex.IsMatch(SimplifyOcrText(line).Trim(), @"^开[?？中错錯对對准準]*$");

    private static bool IsNumberRowStart(string line) =>
        Regex.IsMatch(line, @"\p{L}.*\d") && !ContainsAnyIssue(line);

    private static string? ExtractNumbers(string text, int expectedCount)
    {
        string beforeOpening = BeforeOpeningResult(text);
        Match[] numericBrackets = Regex.Matches(
                beforeOpening, @"[【\[（(](?<value>[^】\]）)]*)[】\]）)]")
            .Cast<Match>()
            .Where(bracket =>
            {
                string payload = bracket.Groups["value"].Value;
                if (!Regex.IsMatch(payload, @"^\s*[0-9 ,，.。]+\s*$"))
                    return false;
                string[]? parsed = ParseNumbers(payload);
                return parsed is { Length: > 1 };
            })
            .ToArray();
        if (numericBrackets.Length > 0)
        {
            var bracketValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match bracket in numericBrackets)
            {
                string? bracketed = FormatNumbers(
                    ParseNumbers(bracket.Groups["value"].Value), expectedCount);
                // Once a numeric bracket establishes the field boundary, an
                // incomplete/malformed bracket must not widen to text outside it.
                if (bracketed is null)
                    return null;
                bracketValues.Add(bracketed);
            }
            // Multiple complete fields are evidence of ambiguity/conflict, not
            // permission to return the first one.
            return bracketValues.Count == 0 ? null : bracketValues.Count == 1 ? bracketValues.Single() : ConflictMarker;
        }
        return FormatNumbers(ParseNumbers(RemoveIssue(beforeOpening)), expectedCount);
    }

    private static string BeforeOpeningResult(string text)
    {
        string simplified = SimplifyOcrText(text);
        Match opening = Regex.Match(simplified,
            $@"(?<!不)(?<!不会)开(?=\s*(?:[?？]+|[0-9]+|[{Zodiac}]))");
        return opening.Success ? simplified[..opening.Index] : simplified;
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

    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line))
            return true;
        return IsBareIssueBoundary(line, selectedIssue);
    }

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
