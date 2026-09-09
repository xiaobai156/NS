from pathlib import Path
import re

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old_gate = '''        if (rule.AllowNearbyValue && rule.Type == "生肖" && rule.Id != "骁腾杀肖")
        {
            // Nearby poster values are valid only inside the selected issue's
            // physical neighborhood. ExtractNearbySingleZodiac owns the issue
            // boundaries, including bare 4-6 digit issue rows that the strict
            // period regex intentionally does not classify globally.
            string? nearbyValue = ExtractNearbySingleZodiac(lines, issue, rule);
            if (nearbyValue is not null)
                return nearbyValue;
        }'''
new_gate = '''        if (rule.AllowNearbyValue && rule.Type == "生肖" && rule.Id != "骁腾杀肖")
        {
            // This reviewed poster family owns its nearby-value parser. If that
            // parser cannot prove a value inside the selected issue block, stay
            // missing instead of falling through to the generic strict-table
            // parser, which has a different layout contract.
            return ExtractNearbySingleZodiac(lines, issue, rule);
        }'''
if text.count(old_gate) != 1:
    raise RuntimeError(f'nearby exclusive gate mismatch: {text.count(old_gate)}')
text = text.replace(old_gate, new_gate, 1)

pattern = re.compile(
    r'    private static IEnumerable<string> NearbyIssueWindow\(\n'
    r'        string\[\] lines,\n'
    r'        int issueIndex,\n'
    r'        int issue,\n'
    r'        int before,\n'
    r'        int after\)\n'
    r'    \{.*?\n    \}\n\n'
    r'    private static string\? ExtractVerticalIssueZodiac',
    re.S)
if not pattern.search(text):
    raise RuntimeError('NearbyIssueWindow method not found')
replacement = r'''    private static IEnumerable<string> NearbyIssueWindow(
        string[] lines,
        int issueIndex,
        int issue,
        int before,
        int after)
    {
        bool IsOtherIssue(string line) =>
            FindIssues(new[] { line }).Any(actual => actual != issue);

        // Values before the first printed issue are allowed for the reviewed
        // poster layout. Once another issue already exists, text before the
        // selected issue belongs to an earlier block and is not eligible.
        bool hasEarlierOtherIssue = lines.Take(issueIndex).Any(IsOtherIssue);
        int start = hasEarlierOtherIssue ? issueIndex : Math.Max(0, issueIndex - before);
        int end = Math.Min(lines.Length, issueIndex + after + 1);

        for (int index = start; index < end; index++)
        {
            if (index != issueIndex && IsOtherIssue(lines[index]))
            {
                // A later issue closes the target block. Never skip the marker
                // and continue into that issue's value rows.
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
print('Applied exclusive nearby parser and hard other-issue boundaries')
