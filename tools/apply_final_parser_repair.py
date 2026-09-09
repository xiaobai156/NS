from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)

# RuleEngine
path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

text = replace_once(text,
'''        if (peerIndex < 0)
            return line;
        closed = true;
        return normalized[..peerIndex];''',
'''        // The target author's field begins at its own identity. Text belonging
        // to an earlier peer on the same OCR line can never be evidence for it.
        if (peerIndex < 0)
            return normalized[ownIndex..];
        closed = true;
        return normalized[ownIndex..peerIndex];''',
'peer field start')

text = replace_once(text,
'''    private static string? ExtractTyped(string tail, string type)
    {
        tail = SimplifyOcrText(tail);
        string beforeOpening = BeforeOpeningResult(tail);
        string withoutIssueBeforeOpening = RemoveIssue(beforeOpening);''',
'''    private static bool HasConflictingExplicitSingleValues(string text, string type)
    {
        string source = SimplifyOcrText(BeforeOpeningResult(text));
        IEnumerable<string> values = type switch
        {
            "合" => Regex.Matches(source, @"(?<!\\d)(?<value>0?[1-9]|1[0-3])合")
                .Select(match => int.Parse(match.Groups["value"].Value).ToString("00")),
            "尾" => Regex.Matches(source, @"(?<!\\d)(?<value>[0-9])\\s*尾")
                .Select(match => match.Groups["value"].Value),
            "头" => Regex.Matches(source, @"(?<!\\d)(?<value>[0-4零一二三四])\\s*头")
                .Select(match => ToArabicDigit(match.Groups["value"].Value[0]).ToString()),
            "段" => Regex.Matches(source, @"(?<!\\d)(?<value>[0-7])\\s*段")
                .Select(match => match.Groups["value"].Value),
            "单五行" => Regex.Matches(source, "[金木水火土]")
                .Select(match => match.Value),
            "色单双" => Regex.Matches(source, "(?<color>[红蓝绿])(?:波)?(?<parity>[单双])")
                .Select(match => match.Groups["color"].Value + match.Groups["parity"].Value),
            _ => Enumerable.Empty<string>()
        };
        return values.Distinct(StringComparer.Ordinal).Skip(1).Any();
    }

    private static string? ExtractTyped(string tail, string type)
    {
        tail = SimplifyOcrText(tail);
        string beforeOpening = BeforeOpeningResult(tail);
        string withoutIssueBeforeOpening = RemoveIssue(beforeOpening);
        if (HasConflictingExplicitSingleValues(withoutIssueBeforeOpening, type))
            return ConflictMarker;''',
'explicit single conflicts')

text = replace_once(text,
'''        if (type == "头" && (tail.Contains("亚太地区四头", StringComparison.Ordinal) || tail.Contains("团队四头", StringComparison.Ordinal)))
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
'''        if (type == "头" && (tail.Contains("亚太地区四头", StringComparison.Ordinal) || tail.Contains("团队四头", StringComparison.Ordinal)))
        {
            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区四头|团队四头)\\s*[:：]?\\s*(?<value>[0-9](?:\\s*[0-9])*)");
            if (!field.Success)
                return null;
            string raw = string.Concat(field.Groups["value"].Value.Where(char.IsDigit));
            return raw.Length == 4 && raw.Distinct().Count() == 4
                && raw.All(c => c is >= '0' and <= '4')
                ? $"{Enumerable.Range(0, 5).Single(n => !raw.Contains((char)('0' + n)))}头"
                : null;
        }''',
'four head raw validation')

text = replace_once(text,
'''            var parts = new List<string>
            {
                BeforeOpeningResult(TextAfterIssue(lines[index], issue))
            };''',
'''            // The target issue row itself must still be a valid field line after
            // removing the issue marker. This rejects inline foreign labels such as
            // “其他栏目06” instead of letting their digits complete the target field.
            string issueField = RemoveIssue(lines[index]);
            if (!IsNumberContinuation(issueField, expectedCount, rule))
                continue;
            var parts = new List<string>
            {
                BeforeOpeningResult(TextAfterIssue(lines[index], issue))
            };''',
'number issue field ownership')

text = replace_once(text,
'''                if (previous < 0 || ContainsAnyIssue(lines[previous])
                    || IsOpeningOnlySeparator(lines[previous])
                    || !IsNumberContinuation(lines[previous], reviewedExpectedCount, rule))
                    continue;''',
'''                if (previous < 0 || ContainsAnyIssue(lines[previous])
                    || IsOpeningOnlySeparator(lines[previous])
                    || !IsNumberContinuation(lines[previous], reviewedExpectedCount, rule)
                    || previous > 0 && ContainsIssueBoundary(lines[previous - 1], issue)
                        && !IsNumberRowStart(lines[previous]))
                    continue;''',
'strict split previous issue guard')

text = replace_once(text,
'''        if (previous < 0 || ContainsAnyIssue(lines[previous])
            || IsOpeningOnlySeparator(lines[previous]))
            return null;

        string immediate = BeforeOpeningResult(lines[previous]);''',
'''        if (previous < 0 || ContainsAnyIssue(lines[previous])
            || IsOpeningOnlySeparator(lines[previous])
            || previous > 0 && ContainsIssueBoundary(lines[previous - 1], issue)
                && !IsNumberRowStart(lines[previous]))
            return null;

        string immediate = BeforeOpeningResult(lines[previous]);''',
'centered previous issue guard')

text = replace_once(text,
'''        var parts = new List<string>();
        for (int index = left; index < issueIndex; index++)''',
'''        if (left == issueIndex - 1 && left > scopeStart
            && ContainsIssueBoundary(lines[left - 1], issue)
            && !IsNumberRowStart(lines[left]))
            return null;

        var parts = new List<string>();
        for (int index = left; index < issueIndex; index++)''',
'reviewed split previous issue guard')

text = replace_once(text,
'''        if (forward)
        {
            start = issueIndex + 1;
            end = start;
            while (end < lines.Length && !ContainsAnyIssue(lines[end]))
                end++;
        }
        else
        {
            end = issueIndex;
            start = issueIndex - 1;
            while (start >= 0 && !ContainsAnyIssue(lines[start]))
                start--;
            start++;
        }

        var payload = new List<string>();
        for (int index = start; index < end; index++)
        {
            if (!IsNumberContinuation(lines[index], expectedCount, rule))
                continue;
            payload.Add(BeforeOpeningResult(RemoveIssue(lines[index])));
        }''',
'''        if (forward)
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
        for (int index = start; index < end; index++)
        {
            if (!IsNumberContinuation(lines[index], expectedCount, rule))
            {
                // Once the table payload has started, an unknown/non-owned row is
                // a field boundary. Do not skip its heading and then consume bare
                // numbers that follow in another column/section.
                if (payload.Count > 0)
                    break;
                continue;
            }
            payload.Add(BeforeOpeningResult(RemoveIssue(lines[index])));
        }''',
'directional field boundary')

old_method = '''    private static bool IsNumberContinuation(string line, int expectedCount, OcrRule rule)
    {
        string simplified = SimplifyOcrText(line);
        if (ContainsAnyIssue(line)
            || ContainsPeerIdentity(line, rule)
            || Regex.IsMatch(simplified, @"参考|旁栏|排行|统计|说明"))
            return false;

        // A complete numeric bracket is an explicit field boundary even when
        // OCR leaves unrelated title fragments around it.
        if (HasExactBracketPayload(line, expectedCount))
            return true;

        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(simplified)));
        if (numbers is null || numbers.Length == 0)
            return false;

        bool hasLetters = Regex.IsMatch(line, @"\\p{L}");
        if (hasLetters)
        {
            string normalized = Normalize(line);
            bool ownIdentity = new[]
            {
                rule.Keyword,
                rule.Label ?? string.Empty,
                rule.RequiredKeyword ?? string.Empty,
                rule.Section ?? string.Empty
            }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Normalize)
                .Distinct(StringComparer.Ordinal)
                .Any(alias => normalized.Contains(alias, StringComparison.Ordinal));

            // Remove numbers and layout punctuation, then allow only explicit
            // number-field decorations already used by reviewed production cards.
            // Arbitrary labels such as “其他栏目 10 02” cannot become continuation.
            string decoration = Regex.Replace(
                simplified, @"[0-9\\s,，.。:：*【】\\[\\]()（）?？←→]+", string.Empty);
            bool structuralField = Regex.IsMatch(decoration,
                @"^(?:开|開|禁|杀|殺|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺)$");
            if (!ownIdentity && !structuralField)
                return false;
            // A proven field-start label may legitimately carry only the first
            // number; subsequent pure-number lines complete the same field.
            return true;
        }

        if (numbers.Length >= 2 || !hasLetters
            || line.Contains('←') || line.Contains('→'))
            return true;
        return Regex.IsMatch(
            simplified, @"^\\s*[0-9 ,，.。【】\\[\\]]+\\s*开(?=\\s*(?:[?？]+|[0-9]+|$))");
    }'''
new_method = '''    private static bool IsNumberContinuation(string line, int expectedCount, OcrRule rule)
    {
        string simplified = SimplifyOcrText(line);
        if (ContainsAnyIssue(line)
            || IsStandaloneBareIssueCell(line)
            || ContainsPeerIdentity(line, rule)
            || Regex.IsMatch(simplified, @"参考|旁栏|排行|统计|说明"))
            return false;

        bool hasLetters = Regex.IsMatch(line, @"\\p{L}");
        if (hasLetters)
        {
            string normalized = Normalize(line);
            string[] aliases = new[]
            {
                rule.Keyword,
                rule.Label ?? string.Empty,
                rule.RequiredKeyword ?? string.Empty,
                rule.Section ?? string.Empty
            }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Normalize)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(value => value.Length)
                .ToArray();
            bool ownIdentity = aliases.Any(alias => normalized.Contains(alias, StringComparison.Ordinal));

            string residual = normalized;
            foreach (string alias in aliases)
                residual = residual.Replace(alias, string.Empty, StringComparison.Ordinal);
            residual = Regex.Replace(residual, @"[0-9]+", string.Empty);

            string decoration = Regex.Replace(
                simplified, @"[0-9\\s,，.。:：*【】\\[\\]()（）?？←→]+", string.Empty);
            const string structuralPattern =
                @"^(?:开|開|禁|杀|殺|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺)$";
            bool structuralField = Regex.IsMatch(decoration, structuralPattern);
            bool residualOwned = ownIdentity && (residual.Length == 0 || Regex.IsMatch(residual, structuralPattern));
            if (!residualOwned && !structuralField)
                return false;
        }

        // Brackets prove a value container only after the row itself has been
        // proven to belong to this field. A foreign label cannot bypass ownership
        // merely by wrapping a complete number list in brackets.
        if (HasExactBracketPayload(line, expectedCount))
            return true;

        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(simplified)));
        if (numbers is null || numbers.Length == 0)
            return false;
        return true;
    }'''
text = replace_once(text, old_method, new_method, 'number continuation method')

text = replace_once(text,
'''    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line))
            return true;
        Match leading = BareIssueRegex.Match(line);
        if (!leading.Success || leading.Groups["issue"].Value.Length <= 3
            || !int.TryParse(leading.Groups["issue"].Value, out int actual))
            return false;
        return actual != selectedIssue && Math.Abs((long)actual - selectedIssue) <= 10;
    }''',
'''    private static bool IsStandaloneBareIssueCell(string line)
    {
        string simplified = SimplifyOcrText(line).Trim();
        if (!Regex.IsMatch(simplified, @"^[0-9]{3,6}$"))
            return false;
        return int.TryParse(simplified, out int value) && value > 49;
    }

    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line) || IsStandaloneBareIssueCell(line))
            return true;
        return false;
    }''',
'issue boundary method')

path.write_text(text, encoding='utf-8')

# OcrClients: unpositioned lines are one opaque region, never invented per-line regions.
path = Path('OcrLineTool.App/OcrClients.cs')
text = path.read_text(encoding='utf-8')
text = replace_once(text,
'            return new(text, null, confidence, "tencent", $"unpositioned-{index}");',
'            return new(text, null, confidence, "tencent", "unpositioned");',
'tencent unpositioned region')
text = replace_once(text,
'                items.Add(new(text, box, confidence, "baidu", box is null ? $"unpositioned-{index}" : "main"));',
'                items.Add(new(text, box, confidence, "baidu", box is null ? "unpositioned" : "main"));',
'baidu unpositioned region')
path.write_text(text, encoding='utf-8')

# OcrEvidence: keep full-width banners out of body-column clustering.
path = Path('OcrLineTool.App/OcrEvidence.cs')
text = path.read_text(encoding='utf-8')
old_columns = '''    private static List<List<RowSegment>> BuildHorizontalColumns(IEnumerable<RowSegment> source)
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
    }'''
new_columns = '''    private static List<List<RowSegment>> BuildHorizontalColumns(IEnumerable<RowSegment> source)
    {
        RowSegment[] all = source.ToArray();
        if (all.Length <= 1)
            return all.Select(segment => new List<RowSegment> { segment }).ToList();

        double medianWidth = all.Select(item => (double)Math.Max(1, item.Box.Width))
            .OrderBy(value => value).ElementAt(all.Length / 2);
        double spanningThreshold = Math.Max(300d, medianWidth * 1.8d);
        RowSegment[] body = all.Where(item => item.Box.Width <= spanningThreshold).ToArray();
        RowSegment[] spanning = all.Where(item => item.Box.Width > spanningThreshold).ToArray();

        List<List<RowSegment>> Cluster(IEnumerable<RowSegment> segments)
        {
            var columns = new List<List<RowSegment>>();
            foreach (RowSegment segment in segments.OrderBy(item => item.Box.CenterX))
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

        List<List<RowSegment>> bodyColumns = Cluster(body.Length == 0 ? all : body);
        if (bodyColumns.Count <= 1 || spanning.Length == 0)
            return Cluster(all);

        // A page-wide title/banner is useful identity evidence but is not a body
        // column and must never widen one column until it swallows another.
        bodyColumns.AddRange(spanning.Select(segment => new List<RowSegment> { segment }));
        return bodyColumns.OrderBy(column => column.Average(item => item.Box.CenterX)).ToList();
    }'''
text = replace_once(text, old_columns, new_columns, 'horizontal column clustering')
path.write_text(text, encoding='utf-8')

print('Applied final parser/evidence repair')
