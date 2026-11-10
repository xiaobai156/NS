using System.Text.RegularExpressions;

namespace OcrLineTool;

public static partial class RuleEngine
{
    private sealed record StatisticsTable(Dictionary<char, int> Counts, int? PrintedTotal, int RowCount)
    {
        internal int Total => Counts.Values.Sum();
        internal bool TotalMismatch => PrintedTotal is int printed && printed != Total;
        internal string Winners => string.Concat(Counts.Where(p => p.Value == Counts.Values.Max()).Select(p => p.Key));
    }

    private static string? ExtractMostFrequentZodiac(string[] lines, int issue, OcrRule rule) =>
        ReadMostFrequentZodiac(lines, issue, rule, out _);

    private static string? ReadMostFrequentZodiac(string[] lines, int issue, OcrRule rule, out string? reason)
    {
        StatisticsTable? table = ReadStatisticsTable(lines, issue, rule, out reason);
        if (table is null) return null;
        if (table.TotalMismatch)
        {
            reason = StatisticsTotalReason(table, issue);
            return null;
        }
        return table.Winners;
    }

    private static string StatisticsTotalReason(StatisticsTable table, int issue) =>
        $"已找到{issue}期文字，但次数合计与卡面总量不符（卡面{table.PrintedTotal}，合计{table.Total}，{table.RowCount}行）；需独立完整统计复核";

    private static StatisticsTable? ReadStatisticsTable(string[] lines, int issue, OcrRule rule, out string? reason)
    {
        reason = null;
        int section = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            section = Array.FindIndex(lines, line => ContainsSectionMarker(line, rule.Section));
            if (section < 0)
            {
                reason = $"已找到{issue}期文字，但未定位到本期统计块（未找到「{rule.Section}」区段词）";
                return null;
            }
        }
        int start = Array.FindIndex(lines, section, line => ContainsIssue(line, issue));
        if (start < 0)
        {
            reason = $"未定位到{issue}期统计块";
            return null;
        }
        if (HasOtherStatisticsIssue(lines[start], issue))
        {
            reason = $"统计表头同时包含{issue}期和其他期号，不能确认期属";
            return null;
        }
        var rows = new List<(int Count, string Value)>();
        var totals = new HashSet<int>();
        bool interrupted = false;
        for (int i = start; i < lines.Length; i++)
        {
            string line = SimplifyOcrText(lines[i]);
            if (ContainsIssue(line, issue) && HasOtherStatisticsIssue(line, issue))
            {
                reason = "统计表头包含混合期号";
                return null;
            }
            if (OcrLayoutMarkers.IsBoundary(line) || i > start && !ContainsIssue(line, issue) && ContainsIssueBoundary(line, issue)) break;
            if (i > start && ContainsIssue(line, issue) && rows.Count > 0)
            {
                reason = $"已找到{issue}期文字，但存在多个统计块，不能合并取值";
                return null;
            }
            MatchCollection printed = Regex.Matches(line,
                @"(?:总次数|總次數)\s*[:：]?\s*(?<n>\d{1,3})\s*次|(?:共\s*)?(?<!\d)(?<n>\d{1,3})\s*人");
            foreach (Match m in printed) totals.Add(int.Parse(m.Groups["n"].Value));
            if (TryExtractFrequencyRow(line, out int count, out string value))
            {
                var labels = Regex.Matches(line, @"(?<!\d)\d{1,2}\s*次");
                string payload = line[(labels[0].Index + labels[0].Length)..];
                payload = Regex.Replace(payload, @"开\s*\d{1,2}\s*$", "");
                string unknown = Regex.Replace(payload, $"[{Zodiac}祝禁杀xX×\\s\\p{{P}}\\p{{S}}]", "");
                if (labels.Count != 1 || unknown.Length != 0)
                {
                    reason = $"已找到{issue}期文字，但次数行包含多个档位或无法确认的内容";
                    return null;
                }
                if (interrupted)
                {
                    reason = $"已找到{issue}期文字，但统计行被其他内容中断";
                    return null;
                }
                rows.Add((count, value));
                continue;
            }
            // Only a plain zodiac continuation belongs to the preceding bucket.
            // An author/header/footer closes the table, never lends it a value.
            string remainder = Regex.Replace(line, $"[{Zodiac}祝禁×xX*\\s，,、:：\\[\\]【】()（）]", "");
            string continuation = string.Concat(Regex.Matches(line, $"[{Zodiac}]").Select(m => m.Value));
            if (rows.Count > 0 && remainder.Length == 0 && !interrupted)
            {
                var previous = rows[^1];
                rows[^1] = (previous.Count, previous.Value + continuation);
            }
            else if (printed.Count == 0 && !string.IsNullOrWhiteSpace(line)
                && i != start && !ContainsIssue(line, issue)
                && !(rows.Count == 0 && ContainsRuleIdentity([line], rule))
                && !Normalize(line).Contains("生肖统计", StringComparison.Ordinal)) interrupted = true;
        }
        if (totals.Count > 1)
        {
            reason = $"已找到{issue}期文字，但本期统计块出现不同的卡面总量";
            return null;
        }
        if (rows.Count == 0)
        {
            reason = $"已找到{issue}期文字，但统计块内没有可解析的次数行";
            return null;
        }
        var counts = new Dictionary<char, int>();
        foreach (var row in rows)
            foreach (char zodiac in row.Value.Distinct())
            {
                if (counts.TryGetValue(zodiac, out int old) && old != row.Count)
                {
                    reason = $"已找到{issue}期文字，但{zodiac}出现在不同次数档位";
                    return null;
                }
                counts[zodiac] = row.Count;
            }
        if (counts.Count == 0)
        {
            reason = $"已找到{issue}期文字，但统计行里没有生肖";
            return null;
        }
        int maximum = counts.Values.Max();
        int lastValuedIndex = rows.FindLastIndex(r => r.Value.Length > 0);
        foreach (var row in rows.Take(lastValuedIndex).Where(r => r.Value.Length == 0 && r.Count > maximum))
        {
            reason = $"已找到{issue}期文字，但最高次数之上存在空档（第{row.Count}次无生肖）";
            return null;
        }
        return new(counts, totals.Count == 1 ? totals.Single() : null, rows.Count(r => r.Value.Length > 0));
    }

    internal static bool NeedsStatisticsVerification(OcrEvidence evidence, int issue, OcrRule rule) =>
        rule.Type == "统计生肖" && evidence.StatisticsCheck is null
        && ReadPositionedStatistics(evidence.TokenItems ?? evidence.Items, issue, rule, out _, out _) is { TotalMismatch: true, Counts.Count: 12 };

    internal static RuleExtractionResult ExtractStatisticsEvidence(
        OcrEvidence evidence, int issue, OcrRule rule, out string? reason)
    {
        var items = evidence.TokenItems ?? evidence.Items;
        StatisticsTable? table = ReadPositionedStatistics(items, issue, rule, out OcrBox? bounds, out reason);
        // Legacy text-only evidence keeps the strict total check.
        if (table is null) return RuleExtractionResult.Missing;
        if (!table.TotalMismatch && evidence.StatisticsCheck is null)
            return RuleExtractionResult.Success(table.Winners);
        reason = StatisticsTotalReason(table, issue);
        var check = evidence.StatisticsCheck;
        if (check is null || check.Version != StatisticsVerification.Version
            || evidence.Reader is not ("paddle/medium" or "cloud") || check.Reader != "paddle/small"
            || check.SourceHash != evidence.SourceHash || check.InputHash != evidence.InputHash
            || string.IsNullOrWhiteSpace(evidence.SourceHash) || string.IsNullOrWhiteSpace(evidence.InputHash)
            || table.Counts.Count != 12 || bounds is null || !HasStatisticsIdentity(items, rule))
            return RuleExtractionResult.Missing;
        StatisticsTable? other = ReadPositionedStatistics(check.Items, issue, rule, out OcrBox? otherBounds, out _);
        if (other is null || other.Counts.Count != 12 || otherBounds is null
            || !SameStatisticsBounds(bounds, otherBounds)
            || table.PrintedTotal != other.PrintedTotal
            || !table.Counts.All(pair => other.Counts.TryGetValue(pair.Key, out int count) && count == pair.Value))
        {
            reason = $"已找到{issue}期文字，但两次独立识别的统计表不完整或次数不一致";
            return RuleExtractionResult.Missing;
        }
        reason = null;
        return RuleExtractionResult.Success(table.Winners);
    }

    private static bool HasStatisticsIdentity(IReadOnlyList<OcrLineEvidence> items, OcrRule rule) =>
        ContainsRuleIdentity(items.Select(i => i.Text), rule)
        || rule.Id == "灰灰团" && items.Any(i => Normalize(i.Text).Contains("wilie985团队", StringComparison.OrdinalIgnoreCase));

    private static bool SameStatisticsBounds(OcrBox a, OcrBox b)
    {
        int intersectionWidth = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X));
        int intersectionHeight = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
        return (double)intersectionWidth * intersectionHeight >= .85 * Math.Max((double)a.Width * a.Height, (double)b.Width * b.Height);
    }

    private static StatisticsTable? ReadPositionedStatistics(
        IReadOnlyList<OcrLineEvidence>? source, int issue, OcrRule rule, out OcrBox? bounds, out string? reason)
    {
        bounds = null;
        reason = null;
        if (source is null || source.Count == 0 || source.Any(i => i is null)) return null;
        var items = source.Where(i => !string.IsNullOrWhiteSpace(i.Text)).ToArray();
        if (items.Select(i => i.ViewId).Distinct().Count() != 1)
        {
            reason = "统计表包含多个识别视图";
            return null;
        }
        if (items.Any(i => i.Box is not { Width: > 0, Height: > 0 }))
            return ReadStatisticsTable(OcrEvidence.SafeLines(items).ToArray(), issue, rule, out reason);
        var headers = items.Where(i => ContainsIssue(i.Text, issue)).OrderBy(i => i.Box!.Y).ToArray();
        if (headers.Length == 0) return null;
        var header = headers[^1];
        int top = header.Box!.Y;
        if (headers.Length > 1 && items.Any(i => i.Box!.Y >= headers[0].Box!.Bottom && i.Box.Bottom <= top
            && TryExtractFrequencyRow(i.Text, out _, out _))) return null;
        int bottom = items.Where(i => i.Box!.Y > top && ContainsIssueBoundary(i.Text, issue))
            .Select(i => i.Box!.Y).DefaultIfEmpty(int.MaxValue).Min();
        var block = items.Where(i => i.Box!.Y >= top && i.Box.Bottom <= bottom).ToArray();
        var rows = block.Where(i => TryExtractFrequencyRow(i.Text, out _, out _)).ToArray();
        if (rows.Length == 0 || block.Select(i => i.RegionId).Distinct().Count() != 1) return null;
        double rowHeight = rows.Average(i => i.Box!.Height);
        if (rows.Any(i => Math.Abs(i.Box!.X - header.Box.X) > Math.Max(48, rowHeight * 3))) return null;
        var identities = items.Where(i => i.Box!.Bottom <= rows.Min(r => r.Box!.Y)
            && i.RegionId == header.RegionId && HorizontalStatisticsOverlap(i.Box, header.Box))
            .OrderBy(i => i.Box!.CenterY).ThenBy(i => i.Box!.X).ToArray();
        // Author identity must be in this column, before its rows. A title in a
        // neighbouring card cannot authorize a target-period table here.
        if (!HasStatisticsIdentity(identities, rule)) return null;
        var lastTitle = identities.LastOrDefault(i => StatisticsAuthor(i.Text) is not null);
        if (lastTitle is not null && (StatisticsAuthor(lastTitle.Text) != rule.Id
            || identities.Any(i => StatisticsAuthor(i.Text) is { } author && author != rule.Id
                && Math.Abs(i.Box!.CenterY - lastTitle.Box!.CenterY) < Math.Min(i.Box.Height, lastTitle.Box.Height)))) return null;
        StatisticsTable? table = ReadStatisticsTable(BuildRowMajorReading(block).ToArray(), issue, rule, out reason);
        if (table is null) return null;
        int y = rows.Min(i => i.Box!.Y), end = rows.Max(i => i.Box!.Bottom);
        var cells = block.Where(i => i.Box!.CenterY >= y && i.Box.CenterY <= end).ToArray();
        // Scores include decorative crosses/brackets; complete map agreement
        // and physical ownership are stronger checks than their average score.
        int left = cells.Min(i => i.Box!.X), right = cells.Max(i => i.Box!.Right);
        bounds = new(left, y, right-left, end-y);
        return table;
    }

    private static bool HorizontalStatisticsOverlap(OcrBox a, OcrBox b) =>
        Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X) >= Math.Min(a.Width, b.Width) * .5;

    private static bool HasOtherStatisticsIssue(string line, int issue) =>
        FindIssues([CompactYearIssueRegex.Replace(line, m => m.Groups["issue"].Value + "期")])
            .Any(found => found != issue);

    private static string? StatisticsAuthor(string text)
    {
        foreach (string name in new[] { "品鉴", "小慧慧", "灰灰团", "借花献佛" })
            if (ContainsKeyword(Normalize(text), Normalize(name))) return name;
        return Normalize(text).Contains("wilie985团队", StringComparison.OrdinalIgnoreCase) ? "灰灰团" : null;
    }
}
