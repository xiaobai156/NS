using System.Text.RegularExpressions;

namespace OcrLineTool;

public static partial class RuleEngine
{
    // Column names distinguish sibling cards; one-edit matching would accept 七肖 as 九肖.
    private static bool? BlackTableIdentity(string text, OcrRule rule)
    {
        string? column = rule.Extractor switch
        {
            "nine_zodiacs_across_wrapped_rows" => "九肖",
            "heike_nine_tails" => "必中九尾",
            "kill_two_zodiacs" => "绝杀两肖",
            _ => null
        };
        return column is null ? null : Regex.IsMatch(SimplifyOcrText(text),
            @"黑客数据(?:数据分析|数据最准之)?" + column);
    }

    private static bool UsesBoundedTableCell(string path, OcrRule rule) =>
        rule.Id == "唐朝杀头" && rule.Type == "头" && RuleCatalog.PathBelongsToGroup(path, "嫣然六合彩大全")
        || RuleCatalog.PathBelongsToGroup(path, "黑色六合彩")
            && (rule.Id == "天宫杀头" && rule.Type == "头"
                || rule.Extractor is "nine_zodiacs_across_wrapped_rows" or "heike_nine_tails" or "kill_two_zodiacs");

    private const string TableIssuePrefix = @"^\s*[【\[（(]?\s*(?<issue>\d{1,6})\s*期\s*[】\]）)]?\s*[:：]?";
    private static bool IsTableOpening(string text) => Regex.IsMatch(SimplifyOcrText(text).Trim(),
        $@"^(?:开\s*[:：]?\s*[?？]+|[{Zodiac}]\s*\d{{1,2}}\s*[中错准赢√×✗])$");

    private static bool HasTableConfidence(OcrLineEvidence item)
    {
        double maximum = item.ViewId.Split('/').Contains("tencent", StringComparer.OrdinalIgnoreCase) ? 100 : 1;
        return item.Confidence is not double score
            || double.IsFinite(score) && score >= maximum * .8 && score <= maximum;
    }

    private static RuleExtractionResult ExtractBoundedTableCell(OcrEvidence evidence, int issue, OcrRule rule)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        bool unresolvedInline = false;
        foreach (var region in (evidence.TokenItems ?? evidence.Items).GroupBy(item => (item.ViewId, item.RegionId)))
        {
            OcrLineEvidence[] items = region.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
            // A whole row in one original token proves ownership without guessing adjacent boxes.
            foreach (var item in items)
            {
                string text = SimplifyOcrText(item.Text);
                Match prefix = Regex.Match(text, TableIssuePrefix);
                if (!prefix.Success || int.Parse(prefix.Groups["issue"].Value) != issue)
                    continue;
                string tail = text[prefix.Length..].Trim();
                if (tail.Length == 0 || tail.StartsWith('开'))
                    continue;
                Match row = Regex.Match(tail,
                    $@"^(?<field>.+?)(?<opening>开\s*[:：]?\s*[?？]+|[{Zodiac}]\s*\d{{1,2}}\s*[中错准赢√×✗])$");
                if (HasTableConfidence(item) && row.Success
                    && BoundedTableValue(row.Groups["field"].Value, rule) is string value)
                    observed.Add(value);
                else
                    unresolvedInline = true;
            }
            // Detached text and partial geometry cannot prove cell ownership.
            if (items.Any(item => item.Box is not { Width: > 0, Height: > 0 }))
                continue;
            var boundaries = items.Where(item => Regex.IsMatch(SimplifyOcrText(item.Text), TableIssuePrefix)).ToArray();
            foreach (var anchor in boundaries.Where(item => Regex.IsMatch(SimplifyOcrText(item.Text), TableIssuePrefix + @"\s*$")
                && ContainsIssue(item.Text, issue) && HasTableConfidence(item)))
            {
                OcrBox box = anchor.Box!;
                var openings = items.Where(item => item.Box!.X > box.Right
                    && Math.Abs(item.Box.CenterY - box.CenterY) <= Math.Min(box.Height, item.Box.Height) * .6
                    && IsTableOpening(item.Text)).ToArray();
                if (openings.Length != 1)
                    continue;
                double top = box.CenterY - box.Height * 1.5;
                double bottom = box.CenterY + box.Height * 1.5;
                foreach (var neighbor in boundaries.Where(item => item != anchor
                    && item.Box!.X < box.Right && item.Box.Right > box.X))
                {
                    double y = neighbor.Box!.CenterY;
                    if (y != box.CenterY)
                    {
                        double halfPitch = Math.Abs(y - box.CenterY) / 2;
                        top = Math.Max(top, box.CenterY - halfPitch);
                        bottom = Math.Min(bottom, box.CenterY + halfPitch);
                    }
                    else { top = bottom; break; }
                }
                int right = openings[0].Box!.X;
                var cell = items.Where(item => item != anchor && item != openings[0]
                    && item.Box!.Right > box.Right && item.Box.X < right
                    && item.Box.Bottom > top && item.Box.Y < bottom).ToArray();
                // A box straddling an edge cannot be silently discarded to obtain the expected count.
                if (cell.Any(item => item.Box!.X < box.Right || item.Box.Right > right
                    || item.Box.Y < top || item.Box.Bottom > bottom || !HasTableConfidence(item)))
                    continue;
                string field = string.Join(" ", GroupVisualRows(cell)
                    .Select(row => string.Join(" ", row.OrderBy(item => item.Box!.X).Select(item => item.Text))));
                if (BoundedTableValue(field, rule) is string value)
                    observed.Add(value);
            }
        }
        return observed.Count > 1 ? RuleExtractionResult.Conflict
            : observed.Count == 1 && !unresolvedInline ? RuleExtractionResult.Success(observed.Single()) : RuleExtractionResult.Missing;
    }

    private static string? BoundedTableValue(string field, OcrRule rule)
    {
        string text = Regex.Replace(SimplifyOcrText(field), @"\s+", "");
        if (rule.Id == "唐朝杀头")
        {
            Match m = Regex.Match(text, @"^唐朝绝杀(?<heads>(?:[零一二三四0-4]头){3})稳杀$");
            if (!m.Success) return null;
            char[] heads = m.Groups["heads"].Value.Where(c => c != '头').Select(ToArabicDigit).ToArray();
            return heads.Distinct().Count() == 1 ? $"{heads[0]}头" : null;
        }
        if (rule.Id == "天宫杀头")
        {
            Match m = Regex.Match(text, @"^天宫杀(?:[（(【\[](?<head>[0-4零一二三四])[）)】\]]|(?<head>[0-4零一二三四]))头$");
            return m.Success ? $"{ToArabicDigit(m.Groups["head"].Value[0])}头" : null;
        }
        if (rule.Extractor == "heike_nine_tails")
        {
            Match m = Regex.Match(text, @"^数据分析[:：]?(?<tails>[0-9]{9})尾$");
            string tails = m.Groups["tails"].Value;
            return m.Success && tails.Distinct().Count() == 9
                ? $"{"0123456789".Single(c => !tails.Contains(c))}尾" : null;
        }
        int count = rule.Extractor == "kill_two_zodiacs" ? 2 : 9;
        string label = count == 2 ? "数据杀特肖" : "数据分析";
        Match zodiacs = Regex.Match(text, "^" + label + $"[:：]?(?<value>[{Zodiac}]{{{count}}})$");
        string payload = zodiacs.Groups["value"].Value;
        return zodiacs.Success && payload.Distinct().Count() == count ? payload : null;
    }
}
