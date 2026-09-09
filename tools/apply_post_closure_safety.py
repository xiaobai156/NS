from pathlib import Path
import re

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old_gate = '''        if (rule.AllowNearbyValue && rule.Type == "生肖" && rule.Id != "骁腾杀肖")
        {
            int[] issueValues = periods
                .Select(period => int.TryParse(period.Groups["issue"].Value, out int value) ? value : 0)
                .Where(value => value > 0)
                .Distinct()
                .ToArray();
            if (issueValues.Length == 1 && issueValues[0] == issue)
                return ExtractNearbySingleZodiac(lines, issue, rule);
        }'''
new_gate = '''        if (rule.AllowNearbyValue && rule.Type == "生肖" && rule.Id != "骁腾杀肖")
        {
            // Nearby poster values are valid only inside the selected issue's
            // physical neighborhood. ExtractNearbySingleZodiac owns the issue
            // boundaries, including bare 4-6 digit issue rows that the strict
            // period regex intentionally does not classify globally.
            string? nearbyValue = ExtractNearbySingleZodiac(lines, issue, rule);
            if (nearbyValue is not null)
                return nearbyValue;
        }'''
if text.count(old_gate) != 1:
    raise RuntimeError(f'nearby gate anchor mismatch: {text.count(old_gate)}')
text = text.replace(old_gate, new_gate, 1)

pattern = re.compile(
    r'    private static string\? ExtractNearbySingleZodiac\(string\[\] lines, int issue, OcrRule rule\)\n'
    r'    \{.*?\n    \}\n\n'
    r'    private static string\? ExtractVerticalIssueZodiac',
    re.S)
if not pattern.search(text):
    raise RuntimeError('ExtractNearbySingleZodiac method not found')
replacement = r'''    private static string? ExtractNearbySingleZodiac(string[] lines, int issue, OcrRule rule)
    {
        lines = lines.TakeWhile(line => !Regex.IsMatch(
            SimplifyFixedCardText(line), @"上期\s*开奖\s*结果")).ToArray();

        var standaloneValues = new HashSet<string>(StringComparer.Ordinal);
        for (int issueIndex = 0; issueIndex < lines.Length; issueIndex++)
        {
            if (!ContainsIssue(lines[issueIndex], issue))
                continue;
            foreach (string line in NearbyIssueWindow(lines, issueIndex, issue, before: 4, after: 4))
            {
                string normalized = Normalize(line);
                if (Regex.IsMatch(normalized, $"^[{Zodiac}]$"))
                    standaloneValues.Add(SimplifyOcrText(normalized));
            }
        }
        if (standaloneValues.Count > 0)
            return standaloneValues.Count == 1 ? standaloneValues.Single() : ConflictMarker;

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int issueIndex = 0; issueIndex < lines.Length; issueIndex++)
        {
            if (!ContainsIssue(lines[issueIndex], issue))
                continue;
            foreach (string line in NearbyIssueWindow(lines, issueIndex, issue, before: 1, after: 1))
            {
                string? value = ExtractSingleZodiac(line);
                if (value is not null)
                    values.Add(value);
            }
        }
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
    }

    private static IEnumerable<string> NearbyIssueWindow(
        string[] lines,
        int issueIndex,
        int issue,
        int before,
        int after)
    {
        bool hasEarlierIssue = lines.Take(issueIndex).Any(ContainsAnyIssue);
        int start = hasEarlierIssue ? issueIndex : Math.Max(0, issueIndex - before);
        int end = Math.Min(lines.Length, issueIndex + after + 1);

        for (int index = start; index < end; index++)
        {
            if (index != issueIndex
                && ContainsAnyIssue(lines[index])
                && !ContainsIssue(lines[index], issue))
            {
                if (index > issueIndex)
                    yield break;
                continue;
            }
            yield return lines[index];
        }
    }

    private static string? ExtractVerticalIssueZodiac'''
text = pattern.sub(lambda _: replacement, text, count=1)

old_strict_dragonfly = '''        else if (rule.Folder is "各种杀" or "公式杀料" or "一套组合拳" or "骁腾系列")
        {
            block = ExtractDragonflyPayload(Regex.Replace(block, @"\\s+", ""), rule);
            if (block.Length == 0)
                return null;
        }'''
new_strict_dragonfly = '''        else if (rule.Folder is "各种杀" or "公式杀料" or "一套组合拳" or "骁腾系列")
        {
            block = ExtractDragonflyPayload(Regex.Replace(block, @"\\s+", ""), rule);
            if (block == ConflictMarker)
                return ConflictMarker;
            if (block.Length == 0)
                return null;
        }'''
if text.count(old_strict_dragonfly) != 1:
    raise RuntimeError(f'dragonfly strict anchor mismatch: {text.count(old_strict_dragonfly)}')
text = text.replace(old_strict_dragonfly, new_strict_dragonfly, 1)

old_framed = '''        Match framed = Regex.Match(payload,
            @"^[：:，,、\\-]*(?:【(?<value>[^】]+)】|\\[(?<value>[^\\]]+)\\]|（(?<value>[^）]+)）|\\((?<value>[^\\)]+)\\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\\{(?<value>[^}]+)\\})");
        if (framed.Success)
            return framed.Groups["value"].Value;
        return Regex.Split(payload, @"发|發")[0].Trim();'''
new_framed = '''        string framedPayload = Regex.Replace(payload, @"^[：:，,、\\-\\s]*", "");
        var leadingFrames = new List<string>();
        int frameOffset = 0;
        while (frameOffset < framedPayload.Length)
        {
            Match frame = Regex.Match(framedPayload[frameOffset..],
                @"^(?:【(?<value>[^】]+)】|\\[(?<value>[^\\]]+)\\]|（(?<value>[^）]+)）|\\((?<value>[^\\)]+)\\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\\{(?<value>[^}]+)\\})");
            if (!frame.Success)
                break;
            leadingFrames.Add(frame.Groups["value"].Value);
            frameOffset += frame.Length;
            Match separator = Regex.Match(framedPayload[frameOffset..], @"^[：:，,、+\\-\\s]*");
            frameOffset += separator.Length;
        }
        if (leadingFrames.Count > 0)
        {
            string[] distinctFrames = leadingFrames.Distinct(StringComparer.Ordinal).ToArray();
            return distinctFrames.Length == 1 ? distinctFrames[0] : ConflictMarker;
        }
        return Regex.Split(payload, @"发|發")[0].Trim();'''
if text.count(old_framed) != 1:
    raise RuntimeError(f'dragonfly framed anchor mismatch: {text.count(old_framed)}')
text = text.replace(old_framed, new_framed, 1)

old_conflicts = r'''    private static bool HasConflictingSingleValues(string tail, string type)
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
            // Head cards often contain an instruction count ("杀一头" / "禁止1头")
            // before the actual value. Existing head extraction already resolves the
            // value field; do not reinterpret the instruction as a conflicting result.
            "头" => Array.Empty<string>(),
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
new_conflicts = r'''    private static bool HasConflictingSingleValues(string tail, string type)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(tail));
        IEnumerable<string> values = type switch
        {
            "合" => Regex.Matches(text, @"(?<!\d)(?:0?[1-9]|1[0-3])合")
                .Select(match => $"{int.Parse(match.Value[..^1]):00}合"),
            "段" => Regex.Matches(text, @"(?<!\d)[0-7]\s*段")
                .Select(match => Regex.Replace(match.Value, @"\s+", "")),
            "尾" => TailValueCandidates(text),
            "尾数组合" => TailPairCandidates(text),
            "头" => HeadValueCandidates(text),
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

    private static IEnumerable<string> HeadValueCandidates(string text)
    {
        var values = new List<string>();
        int delimiter = Math.Max(text.LastIndexOf('：'), text.LastIndexOf(':'));
        if (delimiter >= 0)
        {
            values.AddRange(Regex.Matches(text[(delimiter + 1)..],
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头")
                .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        }
        values.AddRange(Regex.Matches(text, @"[【\[](?<head>[0-4])\s*[】\]]\s*头")
            .Select(match => $"{match.Groups["head"].Value}头"));
        values.AddRange(Regex.Matches(text, @"买\s*(?<head>[0-4零一二三四])\s*头")
            .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        return values;
    }

    private static IEnumerable<string> TailValueCandidates(string text)
    {
        var values = Regex.Matches(text, @"(?<!\d)[0-9]\s*尾")
            .Select(match => Regex.Replace(match.Value, @"\s+", ""))
            .ToList();
        int marker = text.LastIndexOf('尾');
        if (marker < 0)
            return values;
        foreach (Match run in Regex.Matches(text[(marker + 1)..], @"(?<!\d)[0-9]+(?!\d)"))
        {
            if (run.Value.Distinct().Count() == 1)
                values.Add($"{run.Value[0]}尾");
        }
        return values;
    }

    private static IEnumerable<string> TailPairCandidates(string text)
    {
        var values = new List<string>();
        foreach (Match pair in Regex.Matches(text,
            @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?"))
        {
            char[] digits = [pair.Groups["first"].Value[0], pair.Groups["second"].Value[0]];
            Array.Sort(digits);
            values.Add(new string(digits));
        }
        foreach (Match compact in Regex.Matches(text, @"(?<!\d)(?<digits>[0-9]{2})\s*尾"))
        {
            char[] digits = compact.Groups["digits"].Value.ToCharArray();
            Array.Sort(digits);
            values.Add(new string(digits));
        }
        return values;
    }
'''
if text.count(old_conflicts) != 1:
    raise RuntimeError(f'generic conflict anchor mismatch: {text.count(old_conflicts)}')
text = text.replace(old_conflicts, new_conflicts, 1)

path.write_text(text, encoding='utf-8')
print('Applied bounded nearby windows and residual conflict handling')
