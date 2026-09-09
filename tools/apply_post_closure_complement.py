from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old = '''        if (type == "缺尾")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("公子送尾数", StringComparison.Ordinal);
            string tailText = marker >= 0
                ? withoutIssue[(marker + "公子送尾数".Length)..]
                : withoutIssue;
            int[] tails = Regex.Matches(tailText, @"(?<!\\d)[0-9]+(?!\\d)")
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
new = '''        if (type == "缺尾")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("公子送尾数", StringComparison.Ordinal);
            string tailText = marker >= 0
                ? withoutIssue[(marker + "公子送尾数".Length)..]
                : withoutIssue;
            int[] rawTails = Regex.Matches(tailText, @"(?<!\\d)[0-9]+(?!\\d)")
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
if text.count(old) != 1:
    raise RuntimeError(f'missing-tail block mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

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
if text.count(old) != 1:
    raise RuntimeError(f'five-element block mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

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
if text.count(old) != 1:
    raise RuntimeError(f'missing-head block mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

old = '''            if (combination.Success)
                return $"{combination.Groups["first"].Value}尾+{combination.Groups["second"].Value}尾";

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\\d)(?<digits>[0-9]{2})\\s*尾");
            return compact.Success
                ? $"{compact.Groups["digits"].Value[0]}尾+{compact.Groups["digits"].Value[1]}尾"
                : null;'''
new = '''            if (combination.Success)
            {
                string first = combination.Groups["first"].Value;
                string second = combination.Groups["second"].Value;
                return first != second ? $"{first}尾+{second}尾" : null;
            }

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\\d)(?<digits>[0-9]{2})\\s*尾");
            return compact.Success && compact.Groups["digits"].Value[0] != compact.Groups["digits"].Value[1]
                ? $"{compact.Groups["digits"].Value[0]}尾+{compact.Groups["digits"].Value[1]}尾"
                : null;'''
if text.count(old) != 1:
    raise RuntimeError(f'tail-pair block mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

# Refine the new head-conflict helper so numeric instruction phrases such as
# “杀1头” or “禁止1头” are not themselves treated as an answer.
old = '''        if (delimiter >= 0)
        {
            values.AddRange(Regex.Matches(text[(delimiter + 1)..],
                    @"(?<!\\d)(?<head>[0-4零一二三四])\\s*头")
                .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        }'''
new = '''        if (delimiter >= 0)
        {
            string field = text[(delimiter + 1)..];
            values.AddRange(Regex.Matches(field,
                    @"(?<!\\d)(?<head>[0-4零一二三四])\\s*头")
                .Where(match => !Regex.IsMatch(
                    field[..match.Index], @"(?:杀|殺|禁|禁止)\\s*$"))
                .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        }'''
if text.count(old) != 1:
    raise RuntimeError(f'head candidate refinement mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
print('Applied complement-field raw cardinality validation')
