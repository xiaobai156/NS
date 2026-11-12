using System.Text.RegularExpressions;

namespace OcrLineTool;

public static partial class RuleEngine
{
    private static bool IsZhanShaHalfWave(OcrRule rule) =>
        rule.Id == "斩杀半波" && rule.Type == "色单双"
        && rule.Folder == "斩杀系列" && rule.StrictIssueBlock;

    private static string[]? ZhanShaHalfWaveRows(IEnumerable<string> source, OcrRule rule)
    {
        string[] lines = source.Select(line => SimplifyFixedCardText(line).Replace('斬', '斩').Trim())
            .Where(line => line.Length > 0).ToArray();
        if (lines.Length == 0)
            return null;
        bool titled = Normalize(lines[0]) == "斩杀半波";
        string[] rows = titled ? lines[1..] : lines;
        // A title belongs only to its own strip; another heading/region cannot donate rows.
        if (rows.Length == 0 || rows.Any(row => row.Contains(OcrLayoutMarkers.RegionBoundary, StringComparison.Ordinal)
                || !Regex.IsMatch(row,
                    @"^\d{2,4}\s*期\s*[:：]?\s*(?:[（(][红蓝绿单双研究?？\s]*[）)]|[红蓝绿单双研究?？\s]*)[√×✓✗✔✘?？\s]*$")))
            return null;
        return titled || RowStructureMatches(rows, rule) ? rows : null;
    }

    private static string? ExtractZhanShaHalfWave(string[] lines, int issue, OcrRule rule)
    {
        string[]? rows = ZhanShaHalfWaveRows(lines, rule);
        if (rows is null)
            return null;
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (string row in rows)
        {
            Match head = Regex.Match(row, @"^\s*(?<issue>\d{2,4})\s*期");
            if (!int.TryParse(head.Groups["issue"].Value, out int rowIssue) || rowIssue != issue)
                continue;
            // The field must be complete in this row; placeholders and duplicate payloads fail closed.
            string field = Regex.Replace(row[head.Length..], @"\s+", "");
            Match value = Regex.Match(field,
                @"^[:：]?(?:[（(](?<value>[红蓝绿][单双])[）)]|(?<value>[红蓝绿][单双]))[√×✓✗✔✘]*$");
            if (!value.Success)
                return null;
            values.Add(value.Groups["value"].Value);
        }
        return values.Count > 1 ? ConflictMarker : values.SingleOrDefault();
    }

    private static RuleExtractionResult ExtractZhanShaHalfWaveEvidence(OcrEvidence evidence, int issue, OcrRule rule)
    {
        if (!RuleCatalog.PathBelongsToGroup(evidence.SourcePath, "新澳高手")
            || !MatchesRuleFolder(evidence.SourcePath, rule))
            return RuleExtractionResult.Missing;
        var regions = evidence.Items.Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => (item.ViewId, item.RegionId)).ToArray();
        bool titled = regions.Any(region => region.Any(item =>
            Normalize(SimplifyFixedCardText(item.Text).Replace('斬', '斩')) == "斩杀半波"));
        var values = new HashSet<string>(StringComparer.Ordinal);
        bool hasOwnedValue = false;
        foreach (var region in regions)
        {
            string[] rows = region.All(item => item.Box is not null)
                ? BuildRowMajorReading(region.ToArray()).ToArray()
                : region.Select(item => item.Text).ToArray();
            if (ZhanShaHalfWaveRows(rows, rule) is null)
                return RuleExtractionResult.Missing;
            string? value = ExtractZhanShaHalfWave(rows, issue, rule);
            if (value is null && rows.Any(row => ContainsIssue(row, issue)))
                return RuleExtractionResult.Missing;
            if (value == ConflictMarker)
                return RuleExtractionResult.Conflict;
            if (value is not null)
            {
                values.Add(value);
                // Unnamed views may contradict a named card, but cannot supply its answer.
                hasOwnedValue |= !titled || rows.Any(row =>
                    Normalize(SimplifyFixedCardText(row).Replace('斬', '斩')) == "斩杀半波");
            }
        }
        return values.Count > 1 ? RuleExtractionResult.Conflict
            : values.Count == 1 && hasOwnedValue ? RuleExtractionResult.Success(values.Single()) : RuleExtractionResult.Missing;
    }
}
