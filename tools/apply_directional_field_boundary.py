from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old_calls = '''            string? directional = followingTable
                ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: true)
                : precedingTable
                    ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: false)
                    : null;'''
new_calls = '''            string? directional = followingTable
                ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: true, rule)
                : precedingTable
                    ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: false, rule)
                    : null;'''
if text.count(old_calls) != 1:
    raise RuntimeError(f'directional calls: expected 1, found {text.count(old_calls)}')
text = text.replace(old_calls, new_calls, 1)

old_signature = '''    private static string? ExtractDirectionalNumberTable(
        string[] lines, int issueIndex, int issue, int expectedCount, bool forward)'''
new_signature = '''    private static string? ExtractDirectionalNumberTable(
        string[] lines, int issueIndex, int issue, int expectedCount, bool forward, OcrRule rule)'''
if text.count(old_signature) != 1:
    raise RuntimeError(f'directional signature: expected 1, found {text.count(old_signature)}')
text = text.replace(old_signature, new_signature, 1)

old_loop = '''        var payload = new List<string>();
        for (int index = start; index < end; index++)
        {
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                continue;
            string candidate = BeforeOpeningResult(RemoveIssue(lines[index]));
            string[]? numbers = ParseNumbers(candidate);
            if (numbers is null || numbers.Length == 0)
                continue;
            if (numbers.Length == 1 && Regex.IsMatch(lines[index], @"\\p{L}")
                && !lines[index].Contains(':') && !lines[index].Contains('：')
                && !lines[index].Contains('←') && !lines[index].Contains('→'))
                continue;
            payload.Add(candidate);
        }
        return payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);'''
new_loop = '''        var payload = new List<string>();
        for (int index = start; index < end; index++)
        {
            if (!IsNumberContinuation(lines[index], expectedCount, rule))
                continue;
            payload.Add(BeforeOpeningResult(RemoveIssue(lines[index])));
        }
        return payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);'''
if text.count(old_loop) != 1:
    raise RuntimeError(f'directional loop: expected 1, found {text.count(old_loop)}')
text = text.replace(old_loop, new_loop, 1)

path.write_text(text, encoding='utf-8')
print('Applied directional field-boundary patch')
