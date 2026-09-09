from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 occurrence, found {count}')
    text = text.replace(old, new, 1)

# A head title may legitimately say “杀一头”, so do not scan the whole line.
# Once an explicit value delimiter exists, however, two different head values in
# that value scope are unresolved evidence and must never become “last one wins”.
anchor = '''    private static bool HasConflictingSingleValues(string tail, string type)
    {'''
helper = r'''    private static bool HasConflictingExplicitHeadValues(string tail)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(tail));
        int delimiter = Math.Max(text.LastIndexOf('：'), text.LastIndexOf(':'));
        if (delimiter < 0)
            return false;
        string valueScope = text[(delimiter + 1)..];
        string[] heads = Regex.Matches(
                valueScope,
                @"(?<!\d)(?<head>[0-4零一二三四])\s*头")
            .Select(match => ToArabicDigit(match.Groups["head"].Value[0]).ToString())
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return heads.Length > 1;
    }

    private static string? ExtractTailCombinationWithConflict(string text)
    {
        string beforeOpening = BeforeOpeningResult(SimplifyOcrText(text));
        var candidates = new List<(int Index, string Key, string Value)>();

        void Add(int index, char first, char second)
        {
            if (first == second)
                return;
            string key = first < second ? $"{first}{second}" : $"{second}{first}";
            candidates.Add((index, key, $"{first}尾+{second}尾"));
        }

        foreach (Match match in Regex.Matches(beforeOpening,
            @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?"))
        {
            Add(match.Index, match.Groups["first"].Value[0], match.Groups["second"].Value[0]);
        }

        foreach (Match match in Regex.Matches(beforeOpening,
            @"[【\[](?<first>[0-9])\s+(?<second>[0-9])\s*[】\]]?\s*尾"))
        {
            Add(match.Index, match.Groups["first"].Value[0], match.Groups["second"].Value[0]);
        }

        foreach (Match match in Regex.Matches(beforeOpening,
            @"(?<!\d)(?<digits>[0-9]{2})\s*尾"))
        {
            string digits = match.Groups["digits"].Value;
            Add(match.Index, digits[0], digits[1]);
        }

        string[] keys = candidates.Select(item => item.Key)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (keys.Length > 1)
            return ConflictMarker;
        if (keys.Length == 0)
            return null;
        return candidates.Where(item => item.Key == keys[0])
            .OrderBy(item => item.Index)
            .First().Value;
    }

'''
if text.count(anchor) != 1:
    raise RuntimeError('conflict helper insertion anchor mismatch')
text = text.replace(anchor, helper + anchor, 1)

replace_once(
'''        if (HasConflictingSingleValues(tail, rule.Type))
            return ConflictMarker;
        return ExtractTyped(tail, rule.Type);''',
'''        if (rule.Type == "头" && HasConflictingExplicitHeadValues(tail))
            return ConflictMarker;
        if (HasConflictingSingleValues(tail, rule.Type))
            return ConflictMarker;
        return ExtractTyped(tail, rule.Type);''',
'head explicit conflict check')

replace_once(
'''        if (type == "尾数组合")
        {
            Match combination = Regex.Match(beforeOpening, @"(?<!\\d)(?<first>[0-9])\\s*尾?\\s*(?:\\+|＋|、|,|，|\\.|。|-|－)\\s*(?<second>[0-9])\\s*尾?");
            if (!combination.Success)
                combination = Regex.Match(beforeOpening, @"[【\\[](?<first>[0-9])\\s*(?<second>[0-9])\\s*[】\\]]?尾");
            if (combination.Success)
                return $"{combination.Groups[\"first\"].Value}尾+{combination.Groups[\"second\"].Value}尾";

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\\d)(?<digits>[0-9]{2})\\s*尾");
            return compact.Success
                ? $"{compact.Groups[\"digits\"].Value[0]}尾+{compact.Groups[\"digits\"].Value[1]}尾"
                : null;
        }''',
'''        if (type == "尾数组合")
            return ExtractTailCombinationWithConflict(beforeOpening);''',
'two-tail conflict extraction')

path.write_text(text, encoding='utf-8')
print('Applied explicit head and two-tail conflict closure')
