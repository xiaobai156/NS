from pathlib import Path
import re

path = Path("OcrLineTool.App/RuleEngine.cs")
text = path.read_text(encoding="utf-8")

anchor = '''    private static string? ExtractStrictCenteredNumberWindow(
        string[] lines, int issueIndex, int issue, int expectedCount, OcrRule rule)
    {'''
helper = '''    private static bool IsReviewedCompactTrailingCompletion(
        string[] lines,
        int index,
        int issueIndex,
        int issue,
        int expectedCount,
        OcrRule rule,
        IEnumerable<string> currentParts)
    {
        if (!SplitIssueNumberRuleIds.Contains(rule.Id)
            || index <= issueIndex + 1
            || !IsOpeningOnlySeparator(lines[index - 1])
            || !ContainsIssueBoundary(lines[index], issue))
            return false;

        string trimmed = SimplifyOcrText(lines[index]).Trim();
        if (!Regex.IsMatch(trimmed, @"^\\d{4,6}$"))
            return false;

        string scoped = ScopeNumberPayload(lines[index], rule, expectedCount);
        return ExtractNumbers(string.Join(' ', currentParts.Append(scoped)), expectedCount) is not null;
    }

'''
if text.count(anchor) != 1:
    raise RuntimeError(f"compat helper anchor mismatch: {text.count(anchor)}")
text = text.replace(anchor, helper + anchor, 1)


def patch_method(source: str, start_marker: str, end_marker: str, old: str, new: str, label: str) -> str:
    start = source.index(start_marker)
    end = source.index(end_marker, start)
    block = source[start:end]
    if block.count(old) != 1:
        raise RuntimeError(f"{label}: expected 1 occurrence, found {block.count(old)}")
    block = block.replace(old, new, 1)
    return source[:start] + block + source[end:]

old_strict = '''        bool sawRightPayload = false;
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

            string right = ScopeNumberPayload(lines[index], rule, expectedCount);'''
new_strict = '''        bool sawRightPayload = false;
        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            bool reviewedTrailingCompletion = candidates.Any(parts =>
                IsReviewedCompactTrailingCompletion(
                    lines, index, issueIndex, issue, expectedCount, rule, parts));
            if (ContainsIssueBoundary(lines[index], issue) && !reviewedTrailingCompletion)
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (IsNumberRowStart(lines[index]) && sawRightPayload)
                break;
            if (!reviewedTrailingCompletion
                && !IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }

            string right = ScopeNumberPayload(lines[index], rule, expectedCount);'''
text = patch_method(
    text,
    "    private static string? ExtractStrictCenteredNumberWindow(",
    "    private static string? ExtractReviewedSplitNumberWindow(",
    old_strict,
    new_strict,
    "strict centered trailing compatibility")

old_reviewed = '''        bool sawRightPayload = false;
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
            parts.Add(ScopeNumberPayload(lines[index], rule, expectedCount));'''
new_reviewed = '''        bool sawRightPayload = false;
        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            bool reviewedTrailingCompletion = IsReviewedCompactTrailingCompletion(
                lines, index, issueIndex, issue, expectedCount, rule, parts);
            if (ContainsIssueBoundary(lines[index], issue) && !reviewedTrailingCompletion)
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (IsNumberRowStart(lines[index]) && sawRightPayload)
                break;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (!reviewedTrailingCompletion
                && !IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }
            parts.Add(ScopeNumberPayload(lines[index], rule, expectedCount));'''
text = patch_method(
    text,
    "    private static string? ExtractReviewedSplitNumberWindow(",
    "    private static string? ExtractDirectionalNumberTable(",
    old_reviewed,
    new_reviewed,
    "reviewed split trailing compatibility")

path.write_text(text, encoding="utf-8")
print("Applied reviewed compact trailing compatibility without reopening generic bare-issue parsing")
