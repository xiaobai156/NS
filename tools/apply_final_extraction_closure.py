from pathlib import Path
import re

# RuleEngine: close residual field/period/single-value paths.
path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old = '''    private static string ScopeCandidateToPeerBoundary(
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
        if (peerIndex < 0)
            return line;
        closed = true;
        return normalized[..peerIndex];
    }
'''
new = '''    private static string ScopeCandidateToPeerBoundary(
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

        // A shared author row belongs to the target author only from its own
        // identity onward. Content before the author may belong to a peer.
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
        closed = peerIndex >= 0;
        return peerIndex >= 0
            ? normalized[ownIndex..peerIndex]
            : normalized[ownIndex..];
    }
'''
if text.count(old) != 1:
    raise RuntimeError('peer scope anchor mismatch')
text = text.replace(old, new)

old = '''    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line))
            return true;
        Match leading = BareIssueRegex.Match(line);
        if (!leading.Success || leading.Groups["issue"].Value.Length <= 3
            || !int.TryParse(leading.Groups["issue"].Value, out int actual))
            return false;
        return actual != selectedIssue && Math.Abs((long)actual - selectedIssue) <= 10;
    }
'''
new = '''    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line))
            return true;
        Match leading = BareIssueRegex.Match(line);
        if (!leading.Success || leading.Groups["issue"].Value.Length <= 3
            || !int.TryParse(leading.Groups["issue"].Value, out int actual))
            return false;

        // Four-or-more digit standalone cells are period-shaped when the active
        // data set itself uses four-or-more digit issues. Never split them into
        // two lottery numbers merely because the numeric pairs are 01-49.
        if (selectedIssue >= 1000)
            return actual != selectedIssue;
        return actual != selectedIssue && Math.Abs((long)actual - selectedIssue) <= 10;
    }
'''
if text.count(old) != 1:
    raise RuntimeError('issue boundary anchor mismatch')
text = text.replace(old, new)

# Target issue row: stop at unknown textual field labels after numeric payload begins.
old = '''            var parts = new List<string>
            {
                BeforeOpeningResult(TextAfterIssue(lines[index], issue))
            };'''
new = '''            var parts = new List<string>
            {
                ScopeNumberFieldText(TextAfterIssue(lines[index], issue), rule)
            };'''
if text.count(old) != 1:
    raise RuntimeError('target number field anchor mismatch')
text = text.replace(old, new)

# Directional tables must stop at the first non-field row rather than skipping its heading.
old = '''        var payload = new List<string>();
        for (int index = start; index < end; index++)
        {
            if (!IsNumberContinuation(lines[index], expectedCount, rule))
                continue;
            payload.Add(BeforeOpeningResult(RemoveIssue(lines[index])));
        }
        return payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);'''
new = '''        var payload = new List<string>();
        for (int index = start; index < end; index++)
        {
            if (!IsNumberContinuation(lines[index], expectedCount, rule))
            {
                if (!string.IsNullOrWhiteSpace(lines[index]))
                    break;
                continue;
            }
            payload.Add(BeforeOpeningResult(RemoveIssue(lines[index])));
        }
        return payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);'''
if text.count(old) != 1:
    raise RuntimeError('directional loop anchor mismatch')
text = text.replace(old, new)

# Exact brackets prove shape only after ownership is proven.
old = '''        // A complete numeric bracket is an explicit field boundary even when
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
            return true;'''
new = '''        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(simplified)));
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

            string decoration = Regex.Replace(
                simplified, @"[0-9\\s,，.。:：*【】\\[\\]()（）?？←→]+", string.Empty);
            bool structuralField = Regex.IsMatch(decoration,
                @"^(?:开|開|禁|杀|殺|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺)$");
            if (!ownIdentity && !structuralField)
                return false;
        }

        // Brackets prove cardinality, not ownership. By this point any textual
        // identity on the row has already been proven to belong to this field.
        if (HasExactBracketPayload(line, expectedCount))
            return true;
        if (hasLetters)
            return true;

        if (numbers.Length >= 2 || !hasLetters
            || line.Contains('←') || line.Contains('→'))
            return true;'''
if text.count(old) != 1:
    raise RuntimeError('number continuation anchor mismatch')
text = text.replace(old, new)

# Prevent a pure numeric line owned by an immediately preceding issue from becoming
# the left half of the current centred row.
old = '''        string immediate = BeforeOpeningResult(lines[previous]);
        if (!IsNumberContinuation(lines[previous], expectedCount, rule))
            return null;

        // Candidate A: the immediate left cell.'''
new = '''        string immediate = BeforeOpeningResult(lines[previous]);
        if (!IsNumberContinuation(lines[previous], expectedCount, rule)
            || IsPrecedingNumberRunOwnedByEarlierIssue(lines, previous, expectedCount, rule))
            return null;

        // Candidate A: the immediate left cell.'''
if text.count(old) != 1:
    raise RuntimeError('strict centred anchor mismatch')
text = text.replace(old, new)

# Raw derived fields: consume the whole field payload and reject trailing garbage.
repls = {
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区八尾|团队八尾)\\s*[:：]?\\s*(?<value>[0-9](?:\\s*[0-9])*)");''':
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区八尾|团队八尾)\\s*[:：]?\\s*(?<value>[0-9\\s]+)\\s*$");''',
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区四行|团队四行)\\s*[:：]?\\s*(?<value>[金木水火土](?:\\s*[金木水火土])*)");''':
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区四行|团队四行)\\s*[:：]?\\s*(?<value>[金木水火土\\s]+)\\s*$");''',
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                $@"(?:亚太地区十肖|团队十肖)\\s*[:：]?\\s*(?<value>[{Zodiac}](?:\\s*[{Zodiac}])*)");''':
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                $@"(?:亚太地区十肖|团队十肖)\\s*[:：]?\\s*(?<value>[{Zodiac}\\s]+)\\s*$");''',
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区四头|团队四头)\\s*[:：]?\\s*(?<value>[0-4](?:\\s*[0-4])*)");''':
'''            Match field = Regex.Match(withoutIssueBeforeOpening,
                @"(?:亚太地区四头|团队四头)\\s*[:：]?\\s*(?<value>[0-9\\s]+)\\s*$");'''
}
for old_s, new_s in repls.items():
    if text.count(old_s) != 1:
        raise RuntimeError('derived field anchor mismatch: ' + old_s[:40])
    text = text.replace(old_s, new_s)

# Four-head range validation must inspect raw digits before deriving the missing head.
old = '''            string raw = string.Concat(field.Groups["value"].Value.Where(c => c is >= '0' and <= '4'));
            return raw.Length == 4 && raw.Distinct().Count() == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !raw.Contains((char)('0' + n)))}头"
                : null;'''
new = '''            string raw = string.Concat(field.Groups["value"].Value.Where(char.IsDigit));
            return raw.Length == 4 && raw.All(c => c is >= '0' and <= '4')
                && raw.Distinct().Count() == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !raw.Contains((char)('0' + n)))}头"
                : null;'''
if text.count(old) != 1:
    raise RuntimeError('four head raw validation anchor mismatch')
text = text.replace(old, new)

# Explicitly conflicting single-value fields must not silently choose the first.
old = '''    private static string? ExtractTypedForRule(string tail, OcrRule rule)
    {
        if (rule.Id == "翩翩公子肖" && rule.Type == "生肖")'''
new = '''    private static string? ExtractTypedForRule(string tail, OcrRule rule)
    {
        if (HasMultipleExplicitSingleValues(tail, rule.Type))
            return ConflictMarker;
        if (rule.Id == "翩翩公子肖" && rule.Type == "生肖")'''
if text.count(old) != 1:
    raise RuntimeError('typed rule anchor mismatch')
text = text.replace(old, new)

# Insert helper methods immediately before IsNumberContinuation.
anchor = '''    private static bool IsNumberContinuation(string line, int expectedCount, OcrRule rule)
    {'''
helpers = r'''    private static string ScopeNumberFieldText(string text, OcrRule rule)
    {
        string scoped = BeforeOpeningResult(SimplifyOcrText(text));
        // Ignore numeric count decorations ("10码", "36计") while looking for
        // the first actual data number.
        string probe = Regex.Replace(scoped,
            @"(?<!\d)\d{1,2}\s*(?:个(?:中特码|特码)?|码|计|計)",
            match => new string(' ', match.Length));
        Match firstNumber = Regex.Match(probe, @"(?<!\d)(?:0?[1-9]|[1-4]\d)(?!\d)");
        if (!firstNumber.Success)
            return scoped;
        int afterFirst = firstNumber.Index + firstNumber.Length;
        Match foreign = Regex.Match(probe[afterFirst..], @"\p{L}+");
        if (!foreign.Success)
            return scoped;
        int boundary = afterFirst + foreign.Index;
        return scoped[..boundary];
    }

    private static bool IsPrecedingNumberRunOwnedByEarlierIssue(
        string[] lines, int index, int expectedCount, OcrRule rule)
    {
        if (IsNumberRowStart(lines[index]))
            return false;
        for (int previous = index - 1; previous >= 0; previous--)
        {
            if (ContainsAnyIssue(lines[previous]))
                return true;
            if (IsNumberRowStart(lines[previous]))
                return false;
            if (IsOpeningOnlySeparator(lines[previous]))
                continue;
            if (!IsNumberContinuation(lines[previous], expectedCount, rule))
                return false;
        }
        return false;
    }

    private static bool HasMultipleExplicitSingleValues(string text, string type)
    {
        string beforeOpening = BeforeOpeningResult(SimplifyOcrText(text));
        IEnumerable<string> values = type switch
        {
            "尾" => Regex.Matches(beforeOpening, @"(?<!\d)(?<v>[0-9])\s*尾")
                .Select(match => match.Groups["v"].Value + "尾"),
            "头" => Regex.Matches(beforeOpening, @"(?<!\d)(?<v>[0-4])\s*头")
                .Select(match => match.Groups["v"].Value + "头"),
            "合" => Regex.Matches(beforeOpening, @"(?<!\d)(?<v>0?[1-9]|1[0-3])\s*合")
                .Select(match => $"{int.Parse(match.Groups["v"].Value):00}合"),
            "段" => Regex.Matches(beforeOpening, @"(?<!\d)(?<v>[0-7])\s*段")
                .Select(match => match.Groups["v"].Value + "段"),
            "半头" => Regex.Matches(beforeOpening, @"(?<!\d)(?<v>[0-4]\s*头\s*[单双])")
                .Select(match => Regex.Replace(match.Groups["v"].Value, @"\s+", string.Empty)),
            "生肖" or "单生肖" => Regex.Matches(beforeOpening, $"[{Zodiac}]")
                .Select(match => match.Value),
            _ => Array.Empty<string>()
        };
        return values.Distinct(StringComparer.Ordinal).Take(2).Count() > 1;
    }

'''
if text.count(anchor) != 1:
    raise RuntimeError('helper insertion anchor mismatch')
text = text.replace(anchor, helpers + anchor)

path.write_text(text, encoding='utf-8')

# OcrEvidence: full-width banners must not bridge separated body columns.
path = Path('OcrLineTool.App/OcrEvidence.cs')
text = path.read_text(encoding='utf-8')
old = '''            List<List<OcrLineEvidence>> rows = BuildRows(items);
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
            }'''
new = '''            List<List<OcrLineEvidence>> rows = BuildRows(items);
            RowSegment[] segments = rows.SelectMany(SplitRow).ToArray();
            int pageLeft = segments.Min(segment => segment.Box.X);
            int pageRight = segments.Max(segment => segment.Box.Right);
            int pageWidth = Math.Max(1, pageRight - pageLeft);
            RowSegment[] banners = segments
                .Where(segment => segment.Box.Width >= pageWidth * 0.75)
                .ToArray();
            RowSegment[] body = banners.Length > 0
                ? segments.Except(banners).ToArray()
                : segments;
            List<List<RowSegment>> columns = BuildHorizontalColumns(body);
            if (columns.Count <= 1)
            {
                output.AddRange(segments.OrderBy(segment => segment.Box.CenterY)
                    .ThenBy(segment => segment.Box.X)
                    .Select(segment => segment.ToEvidence(view.Key, "main")));
                continue;
            }

            // Wide page titles are identity evidence, not a bridge between body
            // columns. Keep them in a separate region so they cannot merge data.
            foreach (RowSegment banner in banners.OrderBy(segment => segment.Box.CenterY))
                output.Add(banner.ToEvidence(view.Key, "banner"));
            for (int column = 0; column < columns.Count; column++)
            {
                foreach (RowSegment segment in columns[column]
                    .OrderBy(segment => segment.Box.CenterY)
                    .ThenBy(segment => segment.Box.X))
                {
                    output.Add(segment.ToEvidence(view.Key, $"column-{column}"));
                }
            }'''
if text.count(old) != 1:
    raise RuntimeError('column partition anchor mismatch')
text = text.replace(old, new)
path.write_text(text, encoding='utf-8')

# Real cloud clients: missing geometry is unknown geometry, not a trusted region per line.
path = Path('OcrLineTool.App/OcrClients.cs')
text = path.read_text(encoding='utf-8')
old = '''        if (!item.TryGetProperty("ItemPolygon", out JsonElement polygon))
            return new(text, null, confidence, "tencent", $"unpositioned-{index}");'''
new = '''        if (!item.TryGetProperty("ItemPolygon", out JsonElement polygon))
            return new(text, null, confidence, "tencent", "main");'''
if text.count(old) != 1:
    raise RuntimeError('tencent unpositioned anchor mismatch')
text = text.replace(old, new)
old = '''                items.Add(new(text, box, confidence, "baidu", box is null ? $"unpositioned-{index}" : "main"));'''
new = '''                items.Add(new(text, box, confidence, "baidu", "main"));'''
if text.count(old) != 1:
    raise RuntimeError('baidu unpositioned anchor mismatch')
text = text.replace(old, new)
path.write_text(text, encoding='utf-8')

print('Applied final extraction/evidence closure patch')
