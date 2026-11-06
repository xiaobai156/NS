using System.Text.RegularExpressions;

namespace OcrLineTool;

public static partial class RuleEngine
{
    private static bool RuiErConfidence(OcrLineEvidence item) => HasTableConfidence(item);

    // Bounds are derived from the card's own heading and neighbouring headings, never from file names.
    internal sealed record RuiErArea(OcrBox Bounds, bool Summary, IReadOnlyList<OcrLineEvidence> Items);

    internal static IReadOnlyList<RuiErArea> RuiErAreas(OcrEvidence evidence, OcrRule rule)
    {
        var areas = new List<RuiErArea>();
        foreach (var region in (evidence.TokenItems ?? evidence.Items).GroupBy(t => (t.ViewId, t.RegionId)))
        {
            OcrLineEvidence[] items = region.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToArray();
            // Missing boxes cannot establish which author owns a value.
            if (items.Length == 0 || items.Any(t => t.Box is not { Width: > 0, Height: > 0 }))
                continue;
            var logos = items.Where(t => Normalize(t.Text) == "澳门六合彩").ToArray();
            foreach (var logo in logos.Where(RuiErConfidence))
            {
                OcrBox box = logo.Box!;
                var headings = items.Where(t => t.Box!.Bottom <= box.Y + box.Height * .15
                    && box.Y - t.Box.Bottom < box.Height * 1.6
                    && t.Box.CenterX >= box.X && t.Box.CenterX <= box.Right
                    && RuiErConfidence(t)).ToArray();
                if (headings.Count(t => Normalize(t.Text) == Normalize(rule.Keyword)) != 1)
                    continue;
                var peers = logos.Where(t => Math.Abs(t.Box!.CenterY - box.CenterY) < box.Height).ToArray();
                int left = peers.Where(t => t.Box!.CenterX < box.CenterX)
                    .Select(t => t.Box!.Right + t.Box.Height / 4).DefaultIfEmpty(0).Max();
                left = Math.Max(left, Math.Max(0, box.X - (int)(box.Width * 1.2)));
                int right = box.Right + box.Height / 4;
                int top = headings.Min(t => t.Box!.Y);
                int bottom = logos.Where(t => t.Box!.Y > box.Bottom
                        && Math.Abs(t.Box.CenterX - box.CenterX) < box.Width)
                    .Select(t => t.Box!.Y - (int)(t.Box.Height * 1.6))
                    .DefaultIfEmpty(items.Max(t => t.Box!.Bottom)).Min();
                bottom = items.Where(t => t.Box!.Y > box.Bottom && t.Box.CenterX >= box.X
                        && t.Box.CenterX <= box.Right
                        && !ContainsAnyIssue(t.Text)
                        && !RuiErDetachedPayload(t.Text)
                        && !Regex.IsMatch(Normalize(t.Text), @"^T[0-9O]")
                        && !Regex.IsMatch(Normalize(t.Text), @"^(NEW|S|天天开奖|6合彩|\d.*)$"))
                    .Select(t => t.Box!.Y).DefaultIfEmpty(bottom).Append(bottom).Min();
                Add(left, top, right, bottom, false);
            }

            // Only the explicitly labelled 新澳 table is eligible. 旧澳 and its author rows stay outside.
            // Uncertain headings cannot authorize data, but still close the preceding area.
            var titles = items.Where(t => Regex.IsMatch(Normalize(t.Text), @"^[新旧舊][澳奥]统计")).ToArray();
            foreach (var title in titles.Where(t => RuiErConfidence(t)
                && Regex.IsMatch(Normalize(t.Text), @"^新[澳奥]统计\d+期$")))
            {
                OcrBox b = title.Box!;
                var peers = titles.Where(t => Math.Abs(t.Box!.CenterY - b.CenterY) < b.Height * 3).ToArray();
                int left = peers.Where(t => t.Box!.CenterX < b.CenterX)
                    .Select(t => (int)((t.Box!.CenterX + b.CenterX) / 2)).DefaultIfEmpty(0).Max();
                left = Math.Max(left, b.X - (int)(b.Width * .6));
                int right = peers.Where(t => t.Box!.CenterX > b.CenterX)
                    .Select(t => (int)((t.Box!.CenterX + b.CenterX) / 2))
                    .DefaultIfEmpty(items.Max(t => t.Box!.Right)).Min();
                right = Math.Min(right, b.Right + b.Width);
                int bottom = items.Where(t => t.Box!.Y > b.Bottom && t.Box.CenterX > left && t.Box.CenterX < right
                    && (Normalize(t.Text).StartsWith("生肖统计", StringComparison.Ordinal)
                        || Regex.IsMatch(Normalize(t.Text), @"^\d+轮")
                        || Normalize(t.Text) == "澳门六合彩"))
                    .Select(t => t.Box!.Y).DefaultIfEmpty(items.Max(t => t.Box!.Bottom)).Min();
                bottom = titles.Where(t => t.Box!.Y > b.Bottom && t.Box.CenterX > left && t.Box.CenterX < right)
                    .Select(t => t.Box!.Y).DefaultIfEmpty(bottom).Append(bottom).Min();
                Add(left, b.Y, right, bottom, true);
            }

            void Add(int left, int top, int right, int bottom, bool summary)
            {
                if (right <= left || bottom <= top) return;
                var selected = items.Where(t => t.Box!.X >= left && t.Box.Right <= right
                    && t.Box.Y >= top && t.Box.Bottom <= bottom).ToArray();
                if (summary && !selected.Any(t => RuiErSummaryAuthor(t.Text, rule.Keyword))) return;
                areas.Add(new(new(left, top, right - left, bottom - top), summary, selected));
            }
        }
        return areas;
    }

    private static bool RuiErDetachedPayload(string text)
    {
        Match payload = RuiErPayload(text);
        return payload.Success && payload.Index == text.Length - text.TrimStart().Length
            && (payload.Groups["value"].Value == "+尾"
                || new[] { "生肖", "尾", "合", "半头" }.Any(type =>
                    ParseRuiErTeamPayload(type, payload.Groups["value"].Value) is not null));
    }

    private static bool RuiErSummaryAuthor(string text, string author)
    {
        string normalized = Normalize(text);
        string own = Normalize(author);
        return Regex.IsMatch(normalized, $@"^{Regex.Escape(own)}(?:杀肖|肖)?(?:\d.*)?$");
    }

    internal static (OcrBox Header, OcrBox Row)? RuiErSummaryCrop(RuiErArea area, OcrRule rule)
    {
        var authors = area.Items.Where(t => t.Text.TrimStart().StartsWith('【')
            && t.Box is not null && RuiErConfidence(t)).ToArray();
        var own = authors.Where(t => RuiErSummaryAuthor(t.Text, rule.Keyword)).ToArray();
        var titles = area.Items.Where(t => Regex.IsMatch(Normalize(t.Text), @"^新[澳奥]统计\d+期$")).ToArray();
        if (own.Length != 1 || titles.Length != 1) return null;
        OcrBox b = own[0].Box!;
        double pitch = authors.Where(t => t != own[0] && Math.Abs(t.Box!.X - b.X) < b.Height)
            .Select(t => Math.Abs(t.Box!.CenterY - b.CenterY)).DefaultIfEmpty(0).Min();
        if (pitch < b.Height * .7) return null;
        int top = (int)Math.Ceiling(b.CenterY - pitch * .48);
        int bottom = (int)Math.Floor(b.CenterY + pitch * .48);
        // Every requested row must retain its own heading. Overlapping names are ambiguous.
        if (authors.Any(t => t != own[0] && t.Box!.CenterY >= top && t.Box.CenterY <= bottom)) return null;
        OcrBox h = titles[0].Box!;
        return (new(area.Bounds.X, h.Y, area.Bounds.Width, h.Height),
            new(area.Bounds.X, top, area.Bounds.Width, bottom - top));
    }

    private static RuleExtractionResult ExtractRuiErTeamEvidence(OcrEvidence evidence, int issue, OcrRule rule)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in RuiErAreas(evidence, rule))
        {
            if (area.Summary)
            {
                if (rule.Type != "生肖") continue;
                if (!area.Items.Any(t => Regex.IsMatch(Normalize(t.Text), $@"^新[澳奥]统计{issue}期$")))
                    continue;
                foreach (var author in area.Items.Where(t => RuiErSummaryAuthor(t.Text, rule.Keyword)))
                {
                    OcrBox b = author.Box!;
                    var row = area.Items.Where(t => Math.Abs(t.Box!.CenterY - b.CenterY) <= b.Height * .45
                        && t.Box.Height <= b.Height * 1.4 && t.Box.X >= b.X).OrderBy(t => t.Box!.X).ToArray();
                    if (row.Any(t => !RuiErConfidence(t))) continue;
                    // The name, record counter and zodiac must occupy the same physical row.
                    string cellText = author.Text + string.Concat(row.Where(t => t != author
                        && t.Box!.X >= b.Right).Select(t => t.Text));
                    Match answer = RuiErSummaryCell(cellText, rule.Keyword);
                    if (answer.Success) values.Add(answer.Groups["value"].Value);
                }
                // A crop may contain the entire named cell in one token, including its counter.
                foreach (var item in area.Items.Where(t => RuiErSummaryAuthor(t.Text, rule.Keyword)))
                {
                    Match cell = RuiErSummaryCell(item.Text, rule.Keyword);
                    if (cell.Success && RuiErConfidence(item))
                        values.Add(cell.Groups["value"].Value);
                }
            }
            else
            {
                foreach (string row in BuildRowMajorReading(area.Items.Select(t => !RuiErConfidence(t)
                    ? t with { Text = "低置信度" } : t).ToArray()))
                {
                    if (row.Contains("低置信度", StringComparison.Ordinal)) continue;
                    if (!RowBelongsToIssue(row, issue)) continue;
                    if (!Regex.IsMatch(row, @"^\s*(?:新[菜莱澳奥])?(?:期\s*[:：]?\s*)?\d{1,6}")) continue;
                    // Rows are rebuilt inside one author card; a second explicit period invalidates the row.
                    if (FindIssues([row]).Any(found => found != issue)) continue;
                    Match payload = RuiErPayload(row);
                    if (!payload.Success) continue;
                    string prefix = Normalize(row[..payload.Index]);
                    bool correctType = rule.Type switch
                    {
                        "生肖" or "尾" => prefix.Contains("杀1肖", StringComparison.Ordinal),
                        "合" => prefix.Contains("杀1合", StringComparison.Ordinal),
                        "半头" => prefix.Contains("杀半头", StringComparison.Ordinal),
                        _ => false
                    };
                    if (correctType && ParseRuiErTeamPayload(rule.Type, payload.Groups["value"].Value) is { } value)
                        values.Add(value);
                }
            }
        }
        return values.Count > 1 ? RuleExtractionResult.Conflict
            : values.Count == 1 ? RuleExtractionResult.Success(values.Single()) : RuleExtractionResult.Missing;
    }

    private static Match RuiErSummaryCell(string text, string author) => Regex.Match(SimplifyOcrText(text),
        $@"^\s*[【\[]?{Regex.Escape(Normalize(author))}(?:杀肖|肖)?\s*\d{{1,3}}\s*[:：]\s*\d{{1,3}}\s*[\]】]?\s*(?<value>[{Zodiac}])\s*$");
}
