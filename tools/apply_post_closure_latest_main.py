from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

def replace_once(old: str, new: str, label: str):
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 occurrence, found {count}')
    text = text.replace(old, new, 1)

# 1) Single-value conflict detection must cover the real fallback forms for
# heads/tails and pair-valued tails instead of letting ExtractTyped pick one.
old = r'''    private static bool HasConflictingSingleValues(string tail, string type)
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
new = r'''    private static bool HasConflictingSingleValues(string tail, string type)
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
            "色单双" or "半波" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
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
            string field = text[(delimiter + 1)..];
            values.AddRange(Regex.Matches(field,
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头")
                .Where(match => !Regex.IsMatch(
                    field[..match.Index], @"(?:杀|殺|禁|禁止)\s*$"))
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
replace_once(old, new, 'single-value conflict helpers')

# 2) Complement fields validate the raw field, not a Distinct()-cleaned subset.
old = r'''        if (type == "缺尾")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("公子送尾数", StringComparison.Ordinal);
            string tailText = marker >= 0
                ? withoutIssue[(marker + "公子送尾数".Length)..]
                : withoutIssue;
            int[] tails = Regex.Matches(tailText, @"(?<!\d)[0-9]+(?!\d)")
                .SelectMany(match => marker >= 0 || match.Value.Length == 1 || match.Value.Length == 9
                    ? match.Value.Select(character => character - '0')
                    : [])
                .Distinct()
                .ToArray();
            if (tails.Length != 9)
                return null;
            int missing = Enumerable.Range(0, 10).Single(number => !tails.Contains(number));
            return $"{missing}尾";
        }'''
new = r'''        if (type == "缺尾")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("公子送尾数", StringComparison.Ordinal);
            string tailText = marker >= 0
                ? withoutIssue[(marker + "公子送尾数".Length)..]
                : withoutIssue;
            int[] rawTails = Regex.Matches(tailText, @"(?<!\d)[0-9]+(?!\d)")
                .SelectMany(match => marker >= 0 || match.Value.Length == 1 || match.Value.Length == 9
                    ? match.Value.Select(character => character - '0')
                    : [])
                .ToArray();
            int[] tails = rawTails.Distinct().ToArray();
            if (rawTails.Length != 9 || tails.Length != 9)
                return null;
            int missing = Enumerable.Range(0, 10).Single(number => !tails.Contains(number));
            return $"{missing}尾";
        }'''
replace_once(old, new, 'missing-tail raw cardinality')

old = '''        if (type == "五行")
        {
            string value = string.Concat(Regex.Matches(beforeOpening, "[金木水火土]").Select(match => match.Value).Distinct());
            return value.Length == 4 ? value : null;
        }'''
new = '''        if (type == "五行")
        {
            string[] elements = Regex.Matches(beforeOpening, "[金木水火土]")
                .Select(match => match.Value)
                .ToArray();
            return elements.Length == 4 && elements.Distinct(StringComparer.Ordinal).Count() == 4
                ? string.Concat(elements)
                : null;
        }'''
replace_once(old, new, 'five-element raw cardinality')

old = r'''        if (type == "尾数组合")
        {
            Match combination = Regex.Match(beforeOpening, @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?");
            if (!combination.Success)
                combination = Regex.Match(beforeOpening, @"[【\[](?<first>[0-9])\s*(?<second>[0-9])\s*[】\]]?尾");
            if (combination.Success)
                return $"{combination.Groups["first"].Value}尾+{combination.Groups["second"].Value}尾";

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\d)(?<digits>[0-9]{2})\s*尾");
            return compact.Success
                ? $"{compact.Groups["digits"].Value[0]}尾+{compact.Groups["digits"].Value[1]}尾"
                : null;
        }'''
new = r'''        if (type == "尾数组合")
        {
            Match combination = Regex.Match(beforeOpening, @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?");
            if (!combination.Success)
                combination = Regex.Match(beforeOpening, @"[【\[](?<first>[0-9])\s*(?<second>[0-9])\s*[】\]]?尾");
            if (combination.Success)
            {
                string first = combination.Groups["first"].Value;
                string second = combination.Groups["second"].Value;
                return first != second ? $"{first}尾+{second}尾" : null;
            }

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\d)(?<digits>[0-9]{2})\s*尾");
            return compact.Success && compact.Groups["digits"].Value[0] != compact.Groups["digits"].Value[1]
                ? $"{compact.Groups["digits"].Value[0]}尾+{compact.Groups["digits"].Value[1]}尾"
                : null;
        }'''
replace_once(old, new, 'tail pair distinctness')

old = '''        if (type is "头数组合" or "缺头")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("四头出特", StringComparison.Ordinal);
            string valueText = marker >= 0 ? withoutIssue[(marker + "四头出特".Length)..] : withoutIssue;
            char[] heads = valueText
                .Where(character => character is >= '0' and <= '4')
                .Distinct()
                .ToArray();
            if (heads.Length != 4)
                return null;
            if (type == "缺头")
            {
                int missing = Enumerable.Range(0, 5).Single(number => !heads.Contains((char)('0' + number)));
                return $"{missing}头";
            }
            return string.Join('+', heads.Select(head => $"{head}头"));
        }'''
new = '''        if (type is "头数组合" or "缺头")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("四头出特", StringComparison.Ordinal);
            string valueText = marker >= 0 ? withoutIssue[(marker + "四头出特".Length)..] : withoutIssue;
            char[] rawHeads = valueText
                .Where(character => character is >= '0' and <= '4')
                .ToArray();
            char[] heads = rawHeads.Distinct().ToArray();
            if (heads.Length != 4 || type == "缺头" && rawHeads.Length != 4)
                return null;
            if (type == "缺头")
            {
                int missing = Enumerable.Range(0, 5).Single(number => !heads.Contains((char)('0' + number)));
                return $"{missing}头";
            }
            return string.Join('+', heads.Select(head => $"{head}头"));
        }'''
replace_once(old, new, 'missing-head raw cardinality')

# 3) The production "半波" rule is the same color/parity value contract.
replace_once(
'''        if (type == "色单双")
        {
            Match colorParity = Regex.Match(beforeOpening, "(?<color>[红蓝绿])(?:波)?(?<parity>[单双])");
            return colorParity.Success
                ? colorParity.Groups["color"].Value + colorParity.Groups["parity"].Value
                : null;
        }''',
'''        if (type is "色单双" or "半波")
        {
            Match colorParity = Regex.Match(beforeOpening, "(?<color>[红蓝绿])(?:波)?(?<parity>[单双])");
            return colorParity.Success
                ? colorParity.Groups["color"].Value + colorParity.Groups["parity"].Value
                : null;
        }''',
'generic half-wave type')

replace_once(
'''            "色单双" => @"公子秒杀半波",''',
'''            "色单双" => @"公子秒杀半波",
            "半波" => @"(?:(?:红红半波|粉红半波|蓝黑半波)\\s*)+",''',
'strict half-wave prefix')

replace_once(
'''        if (rule.Type == "色单双")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;''',
'''        if (rule.Type is "色单双" or "半波")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;''',
'strict half-wave validation')

replace_once(
'''        if (rule.Type == "色单双") return Regex.IsMatch(value, "^[红蓝绿][单双]$");''',
'''        if (rule.Type is "色单双" or "半波") return Regex.IsMatch(value, "^[红蓝绿][单双]$");''',
'formatted half-wave validation')

# 4) Strict dragonfly/xiaoteng cards preserve conflict instead of sanitizing it.
replace_once(
'''        else if (rule.Folder is "各种杀" or "公式杀料" or "一套组合拳" or "骁腾系列")
        {
            block = ExtractDragonflyPayload(Regex.Replace(block, @"\\s+", ""), rule);
            if (block.Length == 0)
                return null;
        }''',
'''        else if (rule.Folder is "各种杀" or "公式杀料" or "一套组合拳" or "骁腾系列")
        {
            block = ExtractDragonflyPayload(Regex.Replace(block, @"\\s+", ""), rule);
            if (block == ConflictMarker)
                return ConflictMarker;
            if (block.Length == 0)
                return null;
        }''',
'dragonfly conflict propagation')

replace_once(
'''        Match marker = rule.Id is "公式杀两肖肖" or "公式杀两尾尾" ? markers[^1] : markers[0];
        string payload = block[(marker.Index + marker.Length)..];''',
'''        bool formulaRule = rule.Id is "公式杀两肖肖" or "公式杀两尾尾";
        if (!formulaRule && markers.Count > 1)
        {
            var repeatedValues = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < markers.Count; index++)
            {
                Match repeatedMarker = markers[index];
                int start = repeatedMarker.Index + repeatedMarker.Length;
                int end = index + 1 < markers.Count ? markers[index + 1].Index : block.Length;
                string candidate = ExtractImmediateDragonflyValue(block[start..end]);
                if (candidate.Length == 0 || candidate == ConflictMarker)
                    return ConflictMarker;
                repeatedValues.Add(candidate);
            }
            return repeatedValues.Count == 1 ? repeatedValues.Single() : ConflictMarker;
        }

        Match marker = formulaRule ? markers[^1] : markers[0];
        string payload = block[(marker.Index + marker.Length)..];''',
'repeated dragonfly markers')

replace_once(
'''        Match framed = Regex.Match(payload,
            @"^[：:，,、\\-]*(?:【(?<value>[^】]+)】|\\[(?<value>[^\\]]+)\\]|（(?<value>[^）]+)）|\\((?<value>[^\\)]+)\\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\\{(?<value>[^}]+)\\})");
        if (framed.Success)
            return framed.Groups["value"].Value;
        return Regex.Split(payload, @"发|發")[0].Trim();''',
'''        return ExtractImmediateDragonflyValue(payload);''',
'leading dragonfly frames')

anchor = '''    private static string ExtractHuangdaxianPayload(string block, OcrRule rule)
    {'''
helper = r'''    private static string ExtractImmediateDragonflyValue(string payload)
    {
        string framedPayload = Regex.Replace(payload, @"^[：:，,、\-\s]*", "");
        var leadingFrames = new List<string>();
        int frameOffset = 0;
        while (frameOffset < framedPayload.Length)
        {
            Match frame = Regex.Match(framedPayload[frameOffset..],
                @"^(?:【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\{(?<value>[^}]+)\})");
            if (!frame.Success)
                break;
            leadingFrames.Add(frame.Groups["value"].Value);
            frameOffset += frame.Length;
            Match separator = Regex.Match(framedPayload[frameOffset..], @"^[：:，,、+\-\s]*");
            frameOffset += separator.Length;
        }
        if (leadingFrames.Count > 0)
        {
            string[] distinct = leadingFrames.Distinct(StringComparer.Ordinal).ToArray();
            return distinct.Length == 1 ? distinct[0] : ConflictMarker;
        }
        return Regex.Split(payload, @"发|發")[0].Trim();
    }

'''
replace_once(anchor, helper + anchor, 'dragonfly helper insertion')

path.write_text(text, encoding='utf-8')
print('Applied latest-main post-closure safety fixes')
