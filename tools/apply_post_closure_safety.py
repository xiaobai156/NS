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
match = pattern.search(text)
if not match:
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
        // Preserve the reviewed poster layout where a standalone value can sit a
        // few lines before the first printed issue. Once any earlier issue exists,
        // however, text before the selected issue cannot belong to this block.
        bool hasEarlierIssue = lines.Take(issueIndex).Any(ContainsAnyIssue);
        int start = hasEarlierIssue ? issueIndex : Math.Max(0, issueIndex - before);
        int end = Math.Min(lines.Length, issueIndex + after + 1);

        for (int index = start; index < end; index++)
        {
            if (index != issueIndex
                && ContainsAnyIssue(lines[index])
                && !ContainsIssue(lines[index], issue))
            {
                // A later issue closes the selected issue block. Do not merely
                // skip its marker and continue into the following issue's value.
                if (index > issueIndex)
                    yield break;
                continue;
            }
            yield return lines[index];
        }
    }

    private static string? ExtractVerticalIssueZodiac'''
text = pattern.sub(lambda _: replacement, text, count=1)
path.write_text(text, encoding='utf-8')
print('Applied bounded nearby-zodiac issue windows')
