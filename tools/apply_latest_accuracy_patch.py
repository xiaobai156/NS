from pathlib import Path
import re


def replace_method(text: str, start_pattern: str, end_pattern: str, replacement: str, label: str) -> str:
    pattern = re.compile(start_pattern + r".*?(?=" + end_pattern + r")", re.S)
    match = pattern.search(text)
    if not match:
        raise RuntimeError(f"{label}: method block not found")
    return text[:match.start()] + replacement.rstrip() + "\n\n" + text[match.end():]


rule_path = Path("OcrLineTool.App/RuleEngine.cs")
rule = rule_path.read_text(encoding="utf-8")

scope_replacement = r'''    private static string ScopeCandidateToPeerBoundary(
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
    }'''
rule = replace_method(
    rule,
    r"    private static string ScopeCandidateToPeerBoundary\(",
    r"    private static bool ContainsPeerIdentity\(",
    scope_replacement,
    "peer scope")

# Make raw inferred-set fields validate the complete field rather than filtering
# illegal/duplicate suffixes out of the payload.
old_special_start = rule.index('        if (type == "尾" && (tail.Contains("亚太地区八尾"')
old_special_end = rule.index('        if (type.StartsWith("号码:", StringComparison.Ordinal)', old_special_start)
new_special = r'''        if (type == "尾" && (tail.Contains("亚太地区八尾", StringComparison.Ordinal) || tail.Contains("团队八尾", StringComparison.Ordinal)))
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
'''
rule = rule[:old_special_start] + new_special + rule[old_special_end:]

# Insert exact-field helper immediately before ExtractTyped.
anchor = '    private static string? ExtractTyped(string tail, string type)\n'
if anchor not in rule:
    raise RuntimeError("ExtractTyped anchor not found")
helper = r'''    private static string? ExactSemanticField(string text, params string[] markers)
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
            "尾" => Regex.Matches(text, @"(?<!\d)[0-9]\s*尾")
                .Select(match => Regex.Replace(match.Value, @"\s+", "")),
            "头" => Regex.Matches(text, @"(?<!\d)[0-4零一二三四]\s*头")
                .Select(match => $"{ToArabicDigit(match.Value.First(c => c is >= '0' and <= '4' or '零' or '一' or '二' or '三' or '四'))}头"),
            "单五行" => Regex.Matches(text, @"[金木水火土]").Select(match => match.Value),
            "半头" => Regex.Matches(text, @"(?<!\d)[0-4]\s*头\s*[单双]")
                .Select(match => Regex.Replace(match.Value, @"\s+", "")),
            "色单双" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
                .Select(match => match.Value.Replace("波", "", StringComparison.Ordinal)),
            "生肖" or "单生肖" => Regex.Matches(text, $"[{Zodiac}]").Select(match => match.Value),
            _ => Array.Empty<string>()
        };
        return values.Distinct(StringComparer.Ordinal).Take(2).Count() > 1;
    }

'''
rule = rule.replace(anchor, helper + anchor, 1)

# Single-valued fields must not silently return the first of multiple values.
old_extract_rule = r'''    private static string? ExtractTypedForRule(string tail, OcrRule rule)
    {
        if (rule.Id == "翩翩公子肖" && rule.Type == "生肖")
        {
            string beforeOpening = BeforeOpeningResult(SimplifyOcrText(tail));
            MatchCollection matches = Regex.Matches(beforeOpening, $"[{Zodiac}]");
            string[] distinct = matches.Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
            return matches.Count > 0 && distinct.Length == 1 ? distinct[0] : null;
        }
        return ExtractTyped(tail, rule.Type);
    }
'''
new_extract_rule = r'''    private static string? ExtractTypedForRule(string tail, OcrRule rule)
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
'''
if old_extract_rule not in rule:
    raise RuntimeError("ExtractTypedForRule block not found")
rule = rule.replace(old_extract_rule, new_extract_rule, 1)

number_block = r'''    private static string? ExtractNumbersAroundIssue(
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
            string first = ScopeNumberPayload(TextAfterIssue(lines[index], issue), rule);
            if (!string.IsNullOrWhiteSpace(first))
                parts.Add(first);
            for (int next = index + 1; next < lines.Length; next++)
            {
                if (ContainsIssueBoundary(lines[next], issue))
                    break;
                if (SplitIssueNumberRuleIds.Contains(rule.Id) && IsOpeningOnlySeparator(lines[next]))
                    continue;
                if (!IsNumberContinuation(lines[next], expectedCount, rule, issue))
                    break;
                parts.Add(ScopeNumberPayload(lines[next], rule));
            }

            string? value = ExtractNumbers(string.Join(' ', parts), expectedCount);
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private static bool HasCurrentFieldStartAfterEarlierIssue(
        string[] lines, int scopeStart, int issueIndex, OcrRule rule)
    {
        int previousIssue = -1;
        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (!ContainsAnyIssue(lines[index]))
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
            || !HasCurrentFieldStartAfterEarlierIssue(lines, 0, issueIndex, rule))
            return null;

        string immediate = ScopeNumberPayload(lines[previous], rule);
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
            expanded.Insert(0, ScopeNumberPayload(lines[index], rule));
            if (IsNumberRowStart(lines[index]))
                foundFieldStart = true;
        }
        if (foundFieldStart && expanded.Count > 1)
            leftCandidates.Add(expanded);

        string centre = ScopeNumberPayload(TextAfterIssue(lines[issueIndex], issue), rule);
        centre = Regex.Split(centre,
            $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]")[0];

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

            string right = ScopeNumberPayload(lines[index], rule);
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
        bool mayUseLeft = HasCurrentFieldStartAfterEarlierIssue(lines, scopeStart, issueIndex, rule);
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
                parts.Add(ScopeNumberPayload(lines[index], rule));
        }
        string centre = ScopeNumberPayload(TextAfterIssue(lines[issueIndex], issue), rule);
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
            parts.Add(ScopeNumberPayload(lines[index], rule));
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
            payload.Add(ScopeNumberPayload(RemoveIssue(lines[index]), rule));
            started = true;
        }
        return payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);
    }

    private static string ScopeNumberPayload(string line, OcrRule rule)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(RemoveIssue(line)));
        Match firstNumber = Regex.Match(text, @"\d");
        if (!firstNumber.Success)
            return text;

        foreach (Match word in Regex.Matches(text, @"\p{L}+"))
        {
            if (word.Index <= firstNumber.Index)
                continue;
            // Zodiac annotations such as 马(49) / 鸡(34) remain part of the
            // number field. Any other word after data starts closes the field.
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

        string scoped = ScopeNumberPayload(simplified, rule);
        string[]? numbers = ParseNumbers(scoped);
        if (numbers is null || numbers.Length == 0)
            return false;

        bool hasLetters = Regex.IsMatch(line, @"\p{L}");
        if (!hasLetters)
            return true;

        string normalized = Normalize(line);
        bool ownIdentity = ContainsOwnNumberIdentity(line, rule);
        string decoration = Regex.Replace(
            simplified, @"[0-9\s,，.。:：*【】\[\]()（）?？←→]+", string.Empty);
        bool structuralField = Regex.IsMatch(decoration,
            @"^(?:开|開|禁|杀|殺|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺)$");
        if (!ownIdentity && !structuralField)
            return false;

        // Brackets prove value grouping only after the line itself has been
        // proven to belong to this field; a foreign bracket cannot claim it.
        if (HasExactBracketPayload(scoped, expectedCount))
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
    }'''
rule = replace_method(
    rule,
    r"    private static string\? ExtractNumbersAroundIssue\(",
    r"    private static bool HasExactBracketPayload\(",
    number_block,
    "number provenance block")

# Update the strict precheck to use the selected issue when deciding whether a
# bare compact row is actually another issue boundary.
rule = rule.replace(
    '!IsNumberContinuation(lines[previous], reviewedExpectedCount, rule))',
    '!IsNumberContinuation(lines[previous], reviewedExpectedCount, rule, issue))',
    1)

old_boundary = r'''    private static bool ContainsIssueBoundary(string line, int selectedIssue)
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
new_boundary = r'''    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line))
            return true;
        return IsBareIssueBoundary(line, selectedIssue);
    }
'''
if old_boundary not in rule:
    raise RuntimeError("issue boundary block not found")
rule = rule.replace(old_boundary, new_boundary, 1)

rule_path.write_text(rule, encoding="utf-8")

# Collapse unknown-geometry cloud rows into one opaque region, while preserving
# genuinely declared physical regions. Also keep full-width banners from joining
# separate body columns.
ev_path = Path("OcrLineTool.App/OcrEvidence.cs")
ev = ev_path.read_text(encoding="utf-8")
ev = ev.replace(
    '.Where(region => !string.IsNullOrWhiteSpace(region) && region != "main")',
    '.Where(region => !string.IsNullOrWhiteSpace(region) && region != "main"\n                    && !region.StartsWith("unpositioned-", StringComparison.Ordinal))',
    1)

old_columns = re.compile(r"    private static List<List<RowSegment>> BuildHorizontalColumns\(IEnumerable<RowSegment> source\)\n    \{.*?\n    \}\n\n    private static List<List<OcrLineEvidence>> BuildRows", re.S)
match = old_columns.search(ev)
if not match:
    raise RuntimeError("BuildHorizontalColumns block not found")
new_columns = r'''    private static List<List<RowSegment>> BuildHorizontalColumns(IEnumerable<RowSegment> source)
    {
        RowSegment[] segments = source.OrderBy(item => item.Box.CenterX).ToArray();
        if (segments.Length == 0)
            return [];
        int pageLeft = segments.Min(item => item.Box.X);
        int pageRight = segments.Max(item => item.Box.Right);
        int pageWidth = Math.Max(1, pageRight - pageLeft);
        double medianWidth = segments.Select(item => Math.Max(1, item.Box.Width))
            .OrderBy(value => value).ElementAt(segments.Length / 2);
        bool IsSpanning(RowSegment segment) => segments.Length >= 3
            && segment.Box.Width >= pageWidth * 0.60
            && segment.Box.Width >= medianWidth * 1.75;

        var columns = new List<List<RowSegment>>();
        foreach (RowSegment segment in segments.Where(item => !IsSpanning(item)))
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

        // A full-width title/header is provenance for the page, not a bridge
        // between physically separated body columns. Keep it in its own region.
        foreach (RowSegment spanning in segments.Where(IsSpanning))
            columns.Add([spanning]);
        return columns.OrderBy(column => column.Average(item => item.Box.CenterX)).ToList();
    }

    private static List<List<OcrLineEvidence>> BuildRows'''
ev = ev[:match.start()] + new_columns + ev[match.end():]
ev_path.write_text(ev, encoding="utf-8")

clients_path = Path("OcrLineTool.App/OcrClients.cs")
clients = clients_path.read_text(encoding="utf-8")
clients = clients.replace(
    'return new(text, null, confidence, "tencent", $"unpositioned-{index}");',
    'return new(text, null, confidence, "tencent", "main");',
    1)
clients = clients.replace(
    'items.Add(new(text, box, confidence, "baidu", box is null ? $"unpositioned-{index}" : "main"));',
    'items.Add(new(text, box, confidence, "baidu", "main"));',
    1)
clients_path.write_text(clients, encoding="utf-8")

print("Applied latest accuracy closure patch")
