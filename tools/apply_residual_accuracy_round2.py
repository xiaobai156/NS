from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)


rule_path = Path("OcrLineTool.App/RuleEngine.cs")
rule = rule_path.read_text(encoding="utf-8")

rule = replace_once(
    rule,
    '''    private static bool IsBareIssueBoundary(string line, int selectedIssue)
    {
        string trimmed = SimplifyOcrText(line).Trim();
        if (!Regex.IsMatch(trimmed, @"^\\d{4,6}$")
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
    }''',
    '''    private static bool IsBareIssueBoundary(string line, int selectedIssue)
    {
        string trimmed = SimplifyOcrText(line).Trim();
        if (!Regex.IsMatch(trimmed, @"^\\d{4,6}$")
            || !int.TryParse(trimmed, out int actual)
            || actual == selectedIssue)
            return false;
        string selected = selectedIssue.ToString();
        if (trimmed.Length != selected.Length)
            return false;

        // A same-width bare 4-6 digit row is ambiguous: it can be another issue
        // marker or compact two-digit lottery pairs. There is no textual proof
        // that lets a generic field safely choose the latter. Accuracy wins over
        // recall here, so every such row closes the selected issue block.
        return true;
    }''',
    "R01 deterministic bare issue boundary")

old_ownership = '''        bool ownIdentity = ContainsOwnNumberIdentity(line, rule);
        string decoration = Regex.Replace(
            ownershipText, @"[0-9\\s,，.。:：*【】\\[\\]()（）?？←→]+", string.Empty);
        bool structuralField = Regex.IsMatch(decoration,
            @"^(?:(?:杀|殺){1,3}|开|開|禁|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺|绝杀[一二三四五六七八九十0-9]+码|絕殺[一二三四五六七八九十0-9]+碼|封杀|封殺)$");
        bool explicitBracketField = HasExactBracketPayload(simplified, expectedCount)
            && Regex.IsMatch(simplified,
                @"(?:杀码|殺碼|杀码|杀(?:特)?码|殺(?:特)?碼|绝杀|絕殺|禁码|禁碼)[^【\\[]*[【\\[]");
        if (!ownIdentity && !structuralField && !explicitBracketField)
            return false;

        // Brackets prove value grouping only after the line itself has been
        // proven to belong to this field; a foreign bracket cannot claim it.
        if (HasExactBracketPayload(scoped, expectedCount)
            || explicitBracketField)
            return true;
        return true;'''
new_ownership = '''        bool ownIdentity = ContainsOwnNumberIdentity(line, rule);
        string decoration = Regex.Replace(
            ownershipText, @"[0-9\\s,，.。:：*【】\\[\\]()（）?？←→]+", string.Empty);
        bool structuralField = Regex.IsMatch(decoration,
            @"^(?:(?:杀|殺){1,3}|开|開|禁|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺|绝杀[一二三四五六七八九十0-9]+码|絕殺[一二三四五六七八九十0-9]+碼|封杀|封殺)$");
        if (!ownIdentity && !structuralField)
            return false;

        // Brackets prove grouping only after ownership has been established.
        // Generic kill words plus a complete bracket can never claim a foreign field.
        return true;'''
rule = replace_once(rule, old_ownership, new_ownership, "R02 bracket ownership")

rule = replace_once(
    rule,
    '''            string digits = new string(heads.Groups["values"].Value.Where(c => c is >= '0' and <= '4').Distinct().ToArray());
            return digits.Length == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !digits.Contains((char)('0' + n)))}头"
                : null;''',
    '''            char[] rawDigits = heads.Groups["values"].Value
                .Where(c => c is >= '0' and <= '4')
                .ToArray();
            if (rawDigits.Length != 4 || rawDigits.Distinct().Count() != 4)
                return null;
            string digits = new(rawDigits);
            return $"{Enumerable.Range(0, 5).Single(n => !digits.Contains((char)('0' + n)))}头";''',
    "R04 treasure head raw cardinality")

rule = replace_once(
    rule,
    '''        Match opening = Regex.Match(middle, $@"(?:开\\s*[?？]+|[{Zodiac}]\\s*\\d{{2}}\\s*[中错])\\s*$");
        if (!opening.Success || ContainsAnyIssue(before) || ContainsAnyIssue(after))
            return false;''',
    '''        Match opening = Regex.Match(middle, $@"(?:开\\s*[?？]+|[{Zodiac}]\\s*\\d{{2}}\\s*[中错])\\s*$");
        if (!int.TryParse(period.Groups["issue"].Value, out int selectedIssue)
            || !opening.Success
            || ContainsIssueBoundary(before, selectedIssue)
            || ContainsIssueBoundary(after, selectedIssue))
            return false;''',
    "R01 wrapped row issue boundary")

rule_path.write_text(rule, encoding="utf-8")

layout_path = Path("OcrLineTool.App/OcrEvidence.cs")
layout = layout_path.read_text(encoding="utf-8")
pattern = re.compile(
    r'    private static List<List<RowSegment>> BuildHorizontalColumns\(IEnumerable<RowSegment> source\)\n'
    r'    \{.*?\n    \}\n\n'
    r'    private static List<List<OcrLineEvidence>> BuildRows',
    re.S)
if not pattern.search(layout):
    raise RuntimeError("R03 BuildHorizontalColumns method not found")
replacement = r'''    private static List<List<RowSegment>> BuildHorizontalColumns(IEnumerable<RowSegment> source)
    {
        RowSegment[] segments = source.OrderBy(item => item.Box.CenterX).ToArray();
        if (segments.Length == 0)
            return [];

        int pageLeft = segments.Min(item => item.Box.X);
        int pageRight = segments.Max(item => item.Box.Right);
        int pageWidth = Math.Max(1, pageRight - pageLeft);

        static int HorizontalGap(RowSegment segment, IReadOnlyList<RowSegment> column)
        {
            int left = column.Min(item => item.Box.X);
            int right = column.Max(item => item.Box.Right);
            return segment.Box.Right < left ? left - segment.Box.Right
                : segment.Box.X > right ? segment.Box.X - right
                : 0;
        }

        static int MergeThreshold(RowSegment segment, IReadOnlyList<RowSegment> column)
        {
            int averageHeight = (int)Math.Round(column.Average(item => Math.Max(1, item.Box.Height)));
            return Math.Max(48, Math.Max(averageHeight, Math.Max(1, segment.Box.Height)) * 4);
        }

        static List<List<RowSegment>> Cluster(IEnumerable<RowSegment> candidates)
        {
            var columns = new List<List<RowSegment>>();
            foreach (RowSegment segment in candidates.OrderBy(item => item.Box.CenterX))
            {
                int chosen = -1;
                int bestGap = int.MaxValue;
                for (int index = 0; index < columns.Count; index++)
                {
                    int gap = HorizontalGap(segment, columns[index]);
                    int threshold = MergeThreshold(segment, columns[index]);
                    if (gap <= threshold && gap < bestGap)
                    {
                        chosen = index;
                        bestGap = gap;
                    }
                }
                if (chosen < 0)
                    columns.Add([segment]);
                else
                    columns[chosen].Add(segment);
            }
            return columns;
        }

        // Detect bridging banners by asking what the body columns look like when
        // the candidate is removed. If the candidate can touch two otherwise
        // separate body columns, it is page/header provenance and must never
        // expand either body's horizontal envelope.
        var spanning = new List<RowSegment>();
        foreach (RowSegment candidate in segments.Where(segment =>
            segments.Length >= 3 && segment.Box.Width >= pageWidth * 0.50))
        {
            List<List<RowSegment>> provisional = Cluster(
                segments.Where(segment => !ReferenceEquals(segment, candidate)));
            if (provisional.Count < 2)
                continue;
            int touchedColumns = provisional.Count(column =>
                HorizontalGap(candidate, column) <= MergeThreshold(candidate, column));
            if (touchedColumns >= 2)
                spanning.Add(candidate);
        }

        List<List<RowSegment>> columns = Cluster(segments.Where(segment =>
            !spanning.Any(item => ReferenceEquals(item, segment))));
        foreach (RowSegment banner in spanning)
            columns.Add([banner]);
        return columns.OrderBy(column => column.Average(item => item.Box.CenterX)).ToList();
    }

    private static List<List<OcrLineEvidence>> BuildRows'''
layout = pattern.sub(lambda _: replacement, layout, count=1)
layout_path.write_text(layout, encoding="utf-8")

print("Applied R01-R04 residual data-accuracy repairs")
