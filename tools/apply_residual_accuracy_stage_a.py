from pathlib import Path
import re

ROOT = Path('.')


def read(path):
    return (ROOT / path).read_text(encoding='utf-8')


def write(path, text):
    (ROOT / path).write_text(text, encoding='utf-8')


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 anchor, found {count}')
    return text.replace(old, new, 1)


def sub_once(text, pattern, repl, label, flags=0):
    out, count = re.subn(pattern, repl, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 regex anchor, found {count}')
    return out

# ---------------------------------------------------------------------------
# RuleEngine: F01/F03/F06/F08/F09/F10/F11 residual correctness paths.
# ---------------------------------------------------------------------------
path = 'OcrLineTool.App/RuleEngine.cs'
text = read(path)

text = replace_once(text,
'''    private static readonly Regex BareIssueRegex = new(
        @"^\\s*[【\\[（({]?\\s*(?<issue>\\d{3,6})(?!\\d)(?!\\s*\\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);''',
'''    private static readonly Regex BareIssueRegex = new(
        @"^\\s*(?:【|\\[|（|\\(|\\{)?\\s*(?<issue>\\d{3,6})\\s*(?:】|\\]|）|\\)|\\})?\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);''',
'F11 bare issue parser')

text = replace_once(text,
'''        if (rule.IgnoreIssue)
        {
            int[] explicitIssues = FindIssues(lines).Distinct().ToArray();
            if (explicitIssues.Length > 0 && !explicitIssues.Contains(issue))
                return null;
        }''',
'''        if (rule.IgnoreIssue)
        {
            int[] explicitIssues = FindIssues(lines).Distinct().ToArray();
            // IgnoreIssue means the card may omit a printed issue in legacy data,
            // not that the selected issue is evidence. Until a trusted publication
            // identity exists, an unnumbered card stays unverified; mixed/other
            // explicit issues are also rejected.
            if (explicitIssues.Length == 0 || explicitIssues.Any(actual => actual != issue))
                return null;
        }''',
'F10 ignore issue proof')

text = replace_once(text,
'''        string? splitTable = ExtractLeadingSplitNumberTable(text, periods, issue, rule);
        if (splitTable is not null)
            return splitTable;''',
'''        // Do not infer a centred/split row from page-level leading numbers.
        // Reviewed wrapped cards are handled below by TryExtractWrappedCardRow,
        // which consumes explicit neighbouring physical rows instead of TakeLast
        // data from an earlier issue.''',
'F01 strict global split removal')

text = replace_once(text,
'''        if (SplitIssueNumberRuleIds.Contains(rule.Id))
        {
            string? reviewedSplit = ExtractLeadingSplitNumberTable(joined, periods, issue, rule);
            if (reviewedSplit is not null)
                return reviewedSplit;
        }
''',
'''        // Page-level leading-number reconstruction is intentionally disabled.
        // The line-bounded reviewed window above is the only generic split repair.
''',
'F01 generic global split removal')

old_extract_numbers = '''    private static string? ExtractNumbers(string text, int expectedCount)
    {
        string beforeOpening = BeforeOpeningResult(text);
        foreach (Match bracket in Regex.Matches(beforeOpening, @"[【\\[（(](?<value>[^】\\]）)]*)[】\\]）)]"))
        {
            string? bracketed = FormatNumbers(ParseNumbers(bracket.Groups["value"].Value), expectedCount);
            if (bracketed is not null)
                return bracketed;
        }
        return FormatNumbers(ParseNumbers(RemoveIssue(beforeOpening)), expectedCount);
    }'''
new_extract_numbers = '''    private static string? ExtractNumbers(string text, int expectedCount)
    {
        string beforeOpening = BeforeOpeningResult(text);
        Match[] numericBrackets = Regex.Matches(
                beforeOpening, @"[【\\[（(](?<value>[^】\\]）)]*)[】\\]）)]")
            .Cast<Match>()
            .Where(bracket => Regex.IsMatch(bracket.Groups["value"].Value, @"\\d"))
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
            return bracketValues.Count == 1 ? bracketValues.Single() : null;
        }
        return FormatNumbers(ParseNumbers(RemoveIssue(beforeOpening)), expectedCount);
    }'''
text = replace_once(text, old_extract_numbers, new_extract_numbers, 'F03/F09 bracket completeness')

# Replace complement special cases with exact immediate-field validation.
text = sub_once(text,
    r'''        if \(type == "尾" && \(tail\.Contains\("亚太地区八尾", StringComparison\.Ordinal\) \|\| tail\.Contains\("团队八尾", StringComparison\.Ordinal\)\)\)\n        \{.*?\n        \}\n        if \(type == "五行" && \(tail\.Contains\("亚太地区四行", StringComparison\.Ordinal\) \|\| tail\.Contains\("团队四行", StringComparison\.Ordinal\)\)\)\n        \{.*?\n        \}\n        if \(type == "生肖组合" && \(tail\.Contains\("亚太地区十肖", StringComparison\.Ordinal\) \|\| tail\.Contains\("团队十肖", StringComparison\.Ordinal\)\)\)\n        \{.*?\n        \}\n        if \(type == "头" && \(tail\.Contains\("亚太地区四头", StringComparison\.Ordinal\) \|\| tail\.Contains\("团队四头", StringComparison\.Ordinal\)\)\)\n        \{.*?\n        \}''',
'''        if (type == "尾" && (tail.Contains("亚太地区八尾", StringComparison.Ordinal) || tail.Contains("团队八尾", StringComparison.Ordinal)))
        {
            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区八尾|团队八尾)\\s*[:：]?\\s*(?<value>[0-9](?:\\s*[0-9])*)");
            if (!field.Success)
                return null;
            string raw = string.Concat(field.Groups["value"].Value.Where(char.IsDigit));
            if (raw.Length != 8 || raw.Distinct().Count() != 8)
                return null;
            int[] present = raw.Select(c => c - '0').ToArray();
            int[] missing = Enumerable.Range(0, 10).Where(n => !present.Contains(n)).ToArray();
            return missing.Length == 2
                ? string.Join(' ', missing.Select(n => $"{n}尾"))
                : null;
        }
        if (type == "五行" && (tail.Contains("亚太地区四行", StringComparison.Ordinal) || tail.Contains("团队四行", StringComparison.Ordinal)))
        {
            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区四行|团队四行)\\s*[:：]?\\s*(?<value>[金木水火土](?:\\s*[金木水火土])*)");
            if (!field.Success)
                return null;
            string raw = string.Concat(field.Groups["value"].Value.Where("金木水火土".Contains));
            return raw.Length == 4 && raw.Distinct().Count() == 4
                ? string.Concat("金木水火土".Where(e => !raw.Contains(e)))
                : null;
        }
        if (type == "生肖组合" && (tail.Contains("亚太地区十肖", StringComparison.Ordinal) || tail.Contains("团队十肖", StringComparison.Ordinal)))
        {
            Match field = Regex.Match(withoutIssueBeforeOpening,
                $@"(?:亚太地区十肖|团队十肖)\\s*[:：]?\\s*(?<value>[{Zodiac}](?:\\s*[{Zodiac}])*)");
            if (!field.Success)
                return null;
            string raw = string.Concat(field.Groups["value"].Value.Where(Zodiac.Contains));
            return raw.Length == 10 && raw.Distinct().Count() == 10
                ? string.Concat(Zodiac.Where(z => !raw.Contains(z)))
                : null;
        }
        if (type == "头" && (tail.Contains("亚太地区四头", StringComparison.Ordinal) || tail.Contains("团队四头", StringComparison.Ordinal)))
        {
            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区四头|团队四头)\\s*[:：]?\\s*(?<value>[0-4](?:\\s*[0-4])*)");
            if (!field.Success)
                return null;
            string raw = string.Concat(field.Groups["value"].Value.Where(c => c is >= '0' and <= '4'));
            return raw.Length == 4 && raw.Distinct().Count() == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !raw.Contains((char)('0' + n)))}头"
                : null;
        }''',
    'F06 exact complement fields', flags=re.S)

text = replace_once(text,
'''        Match leading = BareIssueRegex.Match(line);
        return leading.Success && leading.Groups["issue"].Value.Length == 3
            || YearIssueRegex.IsMatch(line);''',
'''        return BareIssueRegex.IsMatch(line) || YearIssueRegex.IsMatch(line);''',
'F11 unified bare boundary')

text = replace_once(text,
'''    private static string RemoveIssue(string text) =>
        YearIssueRegex.Replace(
            BareIssueRegex.Replace(
                CompactYearIssueRegex.Replace(IssueRegex.Replace(text, ""), ""),
                match => match.Groups["issue"].Value.Length == 3 ? "" : match.Value),
            "");''',
'''    private static string RemoveIssue(string text) =>
        YearIssueRegex.Replace(
            BareIssueRegex.Replace(
                CompactYearIssueRegex.Replace(IssueRegex.Replace(text, ""), ""),
                ""),
            "");''',
'F11 unified issue removal')

text = replace_once(text,
'''        if (!FindIssues(lines).Contains(issue))
            return $"已找到图片和文字，但未识别到第{issue}期";''',
'''        if (rule.IgnoreIssue && !FindIssues(lines).Any())
            return "已找到资料，但资料本身没有可核验期数";
        if (!FindIssues(lines).Contains(issue))
            return $"已找到图片和文字，但未识别到第{issue}期";''',
'F10 missing reason')

old_evidence = '''    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, OcrLineEvidence> region in evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId + "\\u001f" + item.RegionId, StringComparer.Ordinal))
        {
            string? value = ExtractFinalValue(region.Select(item => item.Text), issue, rule);
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 1 ? observed.Single() : null;
    }'''
new_evidence = '''    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, OcrLineEvidence> region in evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId + "\\u001f" + item.RegionId, StringComparer.Ordinal))
        {
            OcrLineEvidence[] items = region.ToArray();
            string? value;
            if (items.All(item => item.Box is not null))
            {
                value = ExtractFinalValue(items.Select(item => item.Text), issue, rule);
            }
            else
            {
                // Without geometry, adjacency is not proof that lines may be
                // concatenated, but neither is it proof that a short valid line
                // is a complete field. Require the whole opaque region to agree
                // with one independently complete physical OCR item.
                string? holistic = ExtractFinalValue(items.Select(item => item.Text), issue, rule);
                var atomic = new HashSet<string>(StringComparer.Ordinal);
                foreach (OcrLineEvidence item in items)
                {
                    string? itemValue = ExtractFinalValue(new[] { item.Text }, issue, rule);
                    if (itemValue is not null)
                        atomic.Add(itemValue);
                }
                value = holistic is not null && atomic.Count == 1 && atomic.Contains(holistic)
                    ? holistic
                    : null;
            }
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 1 ? observed.Single() : null;
    }'''
text = replace_once(text, old_evidence, new_evidence, 'F08/F18 opaque completeness')

write(path, text)

# ---------------------------------------------------------------------------
# OcrEvidence: preserve known regions; infer staggered columns across rows.
# ---------------------------------------------------------------------------
path = 'OcrLineTool.App/OcrEvidence.cs'
text = read(path)
pattern = r'''    internal static IReadOnlyList<OcrLineEvidence> Partition\(IReadOnlyList<OcrLineEvidence> source\)\n    \{.*?\n    \}\n\n    private static List<List<OcrLineEvidence>> BuildRows'''
replacement = '''    internal static IReadOnlyList<OcrLineEvidence> Partition(IReadOnlyList<OcrLineEvidence> source)
    {
        var output = new List<OcrLineEvidence>();
        foreach (IGrouping<string, OcrLineEvidence> view in source
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId, StringComparer.Ordinal))
        {
            OcrLineEvidence[] items = view.ToArray();

            // A caller/template may already know physical regions. Never erase
            // that stronger identity by repartitioning everything as "main".
            string[] declaredRegions = items
                .Select(item => item.RegionId)
                .Where(region => !string.IsNullOrWhiteSpace(region) && region != "main")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (declaredRegions.Length > 0)
            {
                foreach (IGrouping<string, OcrLineEvidence> region in items
                    .GroupBy(item => item.RegionId, StringComparer.Ordinal))
                {
                    output.AddRange(region.OrderBy(item => item.Box?.CenterY ?? double.MaxValue)
                        .ThenBy(item => item.Box?.X ?? int.MaxValue));
                }
                continue;
            }

            if (items.Any(item => item.Box is null))
            {
                // Unknown geometry remains one opaque region. The business
                // extractor separately requires holistic + atomic agreement.
                output.AddRange(items.Select(item => item with { RegionId = "unpositioned" }));
                continue;
            }

            List<List<OcrLineEvidence>> rows = BuildRows(items);
            RowSegment[] segments = rows.SelectMany(SplitRow).ToArray();
            List<List<RowSegment>> columns = BuildHorizontalColumns(segments);
            if (columns.Count <= 1)
            {
                output.AddRange(segments.OrderBy(segment => segment.Box.CenterY)
                    .ThenBy(segment => segment.Box.X)
                    .Select(segment => segment.ToEvidence(view.Key, "main")));
                continue;
            }

            for (int column = 0; column < columns.Count; column++)
            {
                foreach (RowSegment segment in columns[column]
                    .OrderBy(segment => segment.Box.CenterY)
                    .ThenBy(segment => segment.Box.X))
                {
                    output.Add(segment.ToEvidence(view.Key, $"column-{column}"));
                }
            }
        }
        return output;
    }

    private static List<List<RowSegment>> BuildHorizontalColumns(IEnumerable<RowSegment> source)
    {
        var columns = new List<List<RowSegment>>();
        foreach (RowSegment segment in source.OrderBy(item => item.Box.CenterX))
        {
            int chosen = -1;
            int bestGap = int.MaxValue;
            for (int index = 0; index < columns.Count; index++)
            {
                int left = columns[index].Min(item => item.Box.X);
                int right = columns[index].Max(item => item.Box.Right);
                int gap = segment.Box.Right < left ? left - segment.Box.Right
                    : segment.Box.X > right ? segment.Box.X - right
                    : 0;
                int averageHeight = (int)Math.Round(columns[index].Average(item => Math.Max(1, item.Box.Height)));
                int threshold = Math.Max(48, Math.Max(averageHeight, Math.Max(1, segment.Box.Height)) * 4);
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
        return columns.OrderBy(column => column.Average(item => item.Box.CenterX)).ToList();
    }

    private static List<List<OcrLineEvidence>> BuildRows'''
text = sub_once(text, pattern, replacement, 'F18 partition', flags=re.S)
write(path, text)

# ---------------------------------------------------------------------------
# Paddle metadata: ndarray-safe, aligned boxes only.
# ---------------------------------------------------------------------------
path = 'OcrLineTool.App/paddle_local_ocr.py'
text = read(path)
old = '''def _prediction_items(first, view_id: str) -> list[dict]:
    if not hasattr(first, "get"):
        return []
    raw_texts = first.get("rec_texts") or []
    raw_scores = first.get("rec_scores") or []
    raw_boxes = first.get("rec_boxes")
    if raw_boxes is None:
        raw_boxes = first.get("dt_polys") or []
    items = []
    for index, raw_text in enumerate(raw_texts):'''
new = '''def _prediction_items(first, view_id: str) -> list[dict]:
    if not hasattr(first, "get"):
        return []
    raw_texts = first.get("rec_texts")
    if raw_texts is None:
        raw_texts = []
    raw_scores = first.get("rec_scores")
    if raw_scores is None:
        raw_scores = []
    # rec_boxes/rec_polys are aligned with filtered rec_texts. dt_polys may
    # describe the pre-filter detection set and must not be indexed as if it
    # were guaranteed to have the same semantic rows.
    raw_boxes = first.get("rec_boxes")
    if raw_boxes is None:
        raw_boxes = first.get("rec_polys")
    if raw_boxes is None:
        raw_boxes = []
    items = []
    for index, raw_text in enumerate(raw_texts):'''
text = replace_once(text, old, new, 'F18 ndarray metadata')
write(path, text)

print('Applied residual accuracy stage A')
