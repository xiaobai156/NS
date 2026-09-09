from pathlib import Path

rule_path = Path('OcrLineTool.App/RuleEngine.cs')
catalog_path = Path('OcrLineTool.App/RuleCatalog.cs')
rule = rule_path.read_text(encoding='utf-8')
catalog = catalog_path.read_text(encoding='utf-8')


def replace_rule(old: str, new: str) -> None:
    global rule
    count = rule.count(old)
    if count != 1:
        raise RuntimeError(f'RuleEngine anchor count={count}: {old[:140]!r}')
    rule = rule.replace(old, new, 1)


def replace_catalog(old: str, new: str) -> None:
    global catalog
    count = catalog.count(old)
    if count != 1:
        raise RuntimeError(f'RuleCatalog anchor count={count}: {old[:140]!r}')
    catalog = catalog.replace(old, new, 1)


# A per-rule strict flag is an actual behavior override, not ignored metadata.
replace_catalog(
'''                bool singleValuePerIssue = item.TryGetProperty("singleValuePerIssue", out JsonElement singleValueElement)
                    && singleValueElement.ValueKind == JsonValueKind.True;
                if (keyword.Length > 0 && type.Length > 0)''',
'''                bool singleValuePerIssue = item.TryGetProperty("singleValuePerIssue", out JsonElement singleValueElement)
                    && singleValueElement.ValueKind == JsonValueKind.True;
                bool itemStrictIssueBlock = item.TryGetProperty("strictIssueBlock", out JsonElement itemStrictElement)
                    ? itemStrictElement.ValueKind == JsonValueKind.True
                    : strictIssueBlock;
                if (keyword.Length > 0 && type.Length > 0)''')
replace_catalog(
'''                        allowNearbyValue,
                        allowValueWithoutKeyword,
                        strictIssueBlock,
                        singleValuePerIssue));''',
'''                        allowNearbyValue,
                        allowValueWithoutKeyword,
                        itemStrictIssueBlock,
                        singleValuePerIssue));''')

# Bare issues are accepted only as standalone issue cells. Arbitrary text such
# as “编号251” is not period evidence.
replace_rule(
'''    private static readonly Regex BareIssueRegex = new(@"^[^\\d]{0,8}(?<issue>\\d{3,6})(?!\\d)(?!\\s*\\*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);''',
'''    private static readonly Regex BareIssueRegex = new(
        @"^\\s*[【\\[（({]?\\s*(?<issue>\\d{3,6})(?!\\d)\\s*[】\\]）)}]?\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);''')

# IgnoreIssue means “the card may omit an issue”; it never means an explicitly
# different issue can be reassigned to the requested one.
replace_rule(
'''        string[] lines = cloudLines.Select(line => rule.StrictIssueBlock ? SimplifyFixedCardText(line) : line).ToArray();
        // A requested issue is a query, never evidence for repairing OCR.''',
'''        string[] lines = cloudLines.Select(line => rule.StrictIssueBlock ? SimplifyFixedCardText(line) : line).ToArray();
        if (rule.IgnoreIssue)
        {
            int[] explicitIssues = FindIssues(lines).Distinct().ToArray();
            if (explicitIssues.Length > 0 && !explicitIssues.Contains(issue))
                return null;
        }
        // A requested issue is a query, never evidence for repairing OCR.''')

# The special Jieshao nine-zodiac path must compare all complete observations.
replace_rule(
'''        if (rule.Id == "杰少九肖" && rule.Type == "九肖")
        {
            foreach (string candidate in candidates)
            {
                Match nine = Regex.Match(Normalize(candidate), $"九肖(?<value>[{Zodiac}]{{9}})(?![{Zodiac}])");
                if (nine.Success && nine.Groups["value"].Value.Distinct().Count() == 9)
                    return nine.Groups["value"].Value;
            }
            return null;
        }''',
'''        if (rule.Id == "杰少九肖" && rule.Type == "九肖")
        {
            var nineValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (string candidate in MaximalCandidates(candidates))
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
            return nineValues.Count == 1 ? nineValues.Single() : null;
        }''')

# For exact zodiac types, only maximal evidence blocks participate. A short
# successful prefix cannot win when a longer same-row block contains more data.
replace_rule(
'''        IEnumerable<string> relevant = keywordInTarget
            ? candidates.Where(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)))
            : candidates;
        foreach (string line in relevant)''',
'''        IEnumerable<string> relevant = keywordInTarget
            ? candidates.Where(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)))
            : candidates;
        if (rule.Type is "生肖" or "单生肖" or "生肖组合" or "九肖")
            relevant = MaximalCandidates(relevant);
        foreach (string line in relevant)''')

replace_rule(
'''    private static string? ExtractSingleHeadPerIssue(string[] lines, int issue)
    {''',
'''    private static IEnumerable<string> MaximalCandidates(IEnumerable<string> source)
    {
        string[] values = source.Distinct(StringComparer.Ordinal).ToArray();
        return values.Where(candidate => !values.Any(other =>
            other.Length > candidate.Length
            && other.StartsWith(candidate + " ", StringComparison.Ordinal)));
    }

    private static string? ExtractSingleHeadPerIssue(string[] lines, int issue)
    {''')

# Missing top frequency data invalidates the result; do not silently downgrade
# to a lower-frequency row just because that row was recognized more clearly.
replace_rule(
'''            if (rowValue.Length == 0)
                continue;
            rows.Add((count, rowValue));''',
'''            rows.Add((count, rowValue));''')
replace_rule(
'''        int maximum = rows.Max(row => row.Count);
        string result = string.Concat(rows
            .Where(row => row.Count == maximum)
            .Select(row => row.Value)''',
'''        int maximum = rows.Max(row => row.Count);
        (int Count, string Value)[] maximumRows = rows.Where(row => row.Count == maximum).ToArray();
        if (maximumRows.Any(row => row.Value.Length == 0))
            return null;
        string result = string.Concat(maximumRows
            .Select(row => row.Value)''')

# No hard-coded glyph-to-zodiac repair: OCR uncertainty stays missing.
replace_rule(
'''                if (Regex.IsMatch(normalized, $"^[{Zodiac}]$"))
                    standaloneValues.Add(SimplifyOcrText(normalized));
                else if (rule.Id == "包公肖肖" && line.Trim() == "￥")
                    standaloneValues.Add("羊");''',
'''                if (Regex.IsMatch(normalized, $"^[{Zodiac}]$"))
                    standaloneValues.Add(SimplifyOcrText(normalized));''')

# Strict fixed cards must not erase uncertainty punctuation inside the data field.
replace_rule(
'''        // Decorations are separators, not values. Keep unknown letters and
        // digits so malformed OCR cannot silently become a successful result.
        block = Regex.Replace(block, @"[^\\p{L}\\p{N}\\s]", " ").Trim();''',
'''        if (Regex.IsMatch(block, @"[?？]"))
            return null;
        // Decorations are separators, not values. Keep unknown letters and
        // digits so malformed OCR cannot silently become a successful result.
        block = Regex.Replace(block, @"[^\\p{L}\\p{N}\\s]", " ").Trim();''')

# Xiaosaohuo: only the target issue's explicit 九肖 field is evidence. Do not
# combine 六肖/三肖/一肖 tiers, and reject extra zodiac characters.
replace_rule(
'''        if (rule.Id == "小骚货" && rule.Type == "九肖")
        {
            Match heading = Regex.Match(block, @"[\\s\\S]*?快乐的骚货\\s*九肖\\s*(?:正\\s*)?");
            if (!heading.Success)
                return null;
            block = block[heading.Length..];
        }''',
'''        if (rule.Id == "小骚货" && rule.Type == "九肖")
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
            string value = string.Concat(zodiacs.Select(match => match.Value));
            return value.Distinct().Count() == 9 ? value : null;
        }''')

# Formula cards use the final =杀 result, not an earlier intermediate result.
replace_rule(
'''        Match marker = Regex.Match(block, @"[=＝]\\s*杀");
        if (!marker.Success)
            return false;
        string payload = block[(marker.Index + marker.Length)..];''',
'''        MatchCollection markers = Regex.Matches(block, @"[=＝]\\s*杀");
        if (markers.Count == 0)
            return false;
        Match marker = markers[^1];
        string payload = block[(marker.Index + marker.Length)..];''')
replace_rule(
'''        Match marker = Regex.Match(block, pattern, RegexOptions.Singleline);
        if (!marker.Success)
            return string.Empty;
        string payload = block[(marker.Index + marker.Length)..];
        Match framed = Regex.Match(payload,''',
'''        MatchCollection markers = Regex.Matches(block, pattern, RegexOptions.Singleline);
        if (markers.Count == 0)
            return string.Empty;
        Match marker = rule.Id is "公式杀两肖肖" or "公式杀两尾尾" ? markers[^1] : markers[0];
        string payload = block[(marker.Index + marker.Length)..];
        if (rule.Id == "绿格子双杀")
        {
            MatchCollection framedValues = Regex.Matches(payload,
                @"【(?<value>[^】]+)】|\\[(?<value>[^\\]]+)\\]|（(?<value>[^）]+)）|\\((?<value>[^\\)]+)\\)");
            if (framedValues.Count != 1)
                return string.Empty;
            return framedValues[0].Groups["value"].Value;
        }
        Match framed = Regex.Match(payload,''')

# Exact zodiac cardinality: no extra third zodiac and no duplicate-collapse.
replace_rule(
'''        if (type is "生肖组合" or "九肖")
        {
            int nineMarker = beforeOpening.LastIndexOf("解九肖", StringComparison.Ordinal);
            string zodiacText = type == "九肖" && nineMarker >= 0
                ? beforeOpening[(nineMarker + "解九肖".Length)..]
                : beforeOpening;
            string value = string.Concat(Regex.Matches(zodiacText, $"[{Zodiac}]").Select(match => match.Value).Distinct());
            return type == "九肖"
                ? value.Length == 9 ? value : null
                : value.Length >= 2 ? value : null;
        }''',
'''        if (type is "生肖组合" or "九肖")
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
        }''')

replace_rule(
'''        Match match = type switch
        {
            "生肖" => Regex.Match(beforeOpening, $"[{Zodiac}]"),''',
'''        if (type == "生肖")
            return ExtractSingleZodiac(beforeOpening);

        Match match = type switch
        {
            "生肖" => Match.Empty,''')

# A standalone 3-6 digit issue cell is a boundary regardless of width; with the
# stricter BareIssueRegex this no longer turns arbitrary labels into periods.
replace_rule(
'''        Match leading = BareIssueRegex.Match(line);
        return leading.Success && leading.Groups["issue"].Value.Length == 3
            || YearIssueRegex.IsMatch(line);''',
'''        Match leading = BareIssueRegex.Match(line);
        return leading.Success || YearIssueRegex.IsMatch(line);''')
replace_rule(
'''                match => match.Groups["issue"].Value.Length == 3 ? "" : match.Value),''',
'''                _ => ""),''')

rule_path.write_text(rule, encoding='utf-8')
catalog_path.write_text(catalog, encoding='utf-8')
print('Applied P1 extraction accuracy patch')
