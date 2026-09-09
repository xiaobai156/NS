from pathlib import Path
import re


def replace_exact(text: str, old: str, new: str, label: str, count: int = 1) -> str:
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(f"{label}: expected {count}, found {actual}")
    return text.replace(old, new, count)


# F03: number continuation must stay inside the target rule's field.
rule_path = Path("OcrLineTool.App/RuleEngine.cs")
rule_text = rule_path.read_text(encoding="utf-8")

rule_text = replace_exact(
    rule_text,
    '''                string? split = ExtractStrictCenteredNumberWindow(\n                    lines, index, issue, reviewedExpectedCount);''',
    '''                string? split = ExtractStrictCenteredNumberWindow(\n                    lines, index, issue, reviewedExpectedCount, rule);''',
    "strict centered call")

rule_text = replace_exact(
    rule_text,
    '''    private static string? ExtractStrictCenteredNumberWindow(\n        string[] lines, int issueIndex, int issue, int expectedCount)''',
    '''    private static string? ExtractStrictCenteredNumberWindow(\n        string[] lines, int issueIndex, int issue, int expectedCount, OcrRule rule)''',
    "strict centered signature")

rule_text = replace_exact(
    rule_text,
    '''                string? split = ExtractReviewedSplitNumberWindow(\n                    lines, scopeStart, index, issue, expectedCount);''',
    '''                string? split = ExtractReviewedSplitNumberWindow(\n                    lines, scopeStart, index, issue, expectedCount, rule);''',
    "reviewed split call")

rule_text = replace_exact(
    rule_text,
    '''    private static string? ExtractReviewedSplitNumberWindow(\n        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount)''',
    '''    private static string? ExtractReviewedSplitNumberWindow(\n        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount, OcrRule rule)''',
    "reviewed split signature")

rule_text = replace_exact(
    rule_text,
    'IsNumberContinuation(lines[next], expectedCount)',
    'IsNumberContinuation(lines[next], expectedCount, rule)',
    "next continuation calls",
    count=1)
rule_text = replace_exact(
    rule_text,
    'IsNumberContinuation(lines[index], expectedCount)',
    'IsNumberContinuation(lines[index], expectedCount, rule)',
    "indexed continuation calls",
    count=2)

pattern = re.compile(
    r'    private static bool IsNumberContinuation\(string line, int expectedCount\)\n'
    r'    \{.*?\n    \}\n\n    private static ',
    re.S)
replacement = r'''    private static bool IsNumberContinuation(string line, int expectedCount, OcrRule rule)
    {
        string simplified = SimplifyOcrText(line);
        if (ContainsAnyIssue(line)
            || ContainsPeerIdentity(line, rule)
            || Regex.IsMatch(simplified, @"参考|旁栏|排行|统计|说明"))
            return false;

        // A continuation that contains ordinary text must prove that the text
        // belongs to this rule. Pure number rows remain valid for wrapped cards.
        bool openingTail = Regex.IsMatch(
            simplified, @"^\s*[0-9 ,，.。]+\s*开(?=\s*(?:[?？]+|[0-9]+))");
        bool hasLetters = Regex.IsMatch(line, @"\p{L}");
        if (hasLetters && !openingTail)
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
            if (!ownIdentity)
                return false;
        }

        if (HasExactBracketPayload(line, expectedCount))
            return true;
        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(simplified)));
        if (numbers is null || numbers.Length == 0)
            return false;
        if (numbers.Length >= 2 || !hasLetters
            || line.Contains('←') || line.Contains('→'))
            return true;
        return openingTail;
    }

    private static '''
rule_text, replaced = pattern.subn(replacement, rule_text, count=1)
if replaced != 1:
    raise RuntimeError(f"number continuation body: expected 1, found {replaced}")
rule_path.write_text(rule_text, encoding="utf-8")


# F14: keep every successful source immutable from the last hash check through
# trusted-state save and external distribution.
state_path = Path("OcrLineTool.App/RecognitionStateStore.cs")
state_text = state_path.read_text(encoding="utf-8")
insert_before = '''    internal static void Invalidate(string appDirectory, string selectedDirectory, int issue)\n    {'''
lock_code = '''    internal static IDisposable LockCurrentEvidenceForPublish(\n        IReadOnlyList<OcrRule> rules,\n        IReadOnlyDictionary<string, string> values,\n        ResultEvidenceLedger evidence)\n    {\n        var handles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);\n        try\n        {\n            foreach (OcrRule rule in rules)\n            {\n                if (ResultValues.IsConflict(values, rule.Id) || !values.ContainsKey(rule.Id))\n                    continue;\n                if (!evidence.Records.TryGetValue(rule.Id, out ResultEvidenceRecord? record)\n                    || record.Status != "success"\n                    || record.RuleSignature != RuleSignature(rule)\n                    || string.IsNullOrWhiteSpace(record.SourcePath)\n                    || string.IsNullOrWhiteSpace(record.SourceHash))\n                    throw new OcrException(\n                        $"{rule.OutputLabel} 缺少可验证的来源状态，本次结果不会发布。",\n                        "OCR_STATE_REQUIRED");\n\n                if (handles.ContainsKey(record.SourcePath))\n                    continue;\n\n                FileStream stream = new(\n                    record.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);\n                string currentHash = Convert.ToHexString(\n                    System.Security.Cryptography.SHA256.HashData(stream));\n                stream.Position = 0;\n                if (!currentHash.Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase))\n                {\n                    stream.Dispose();\n                    throw new OcrException(\n                        "图片在结果发布前发生变化，请重新识别。",\n                        "OCR_IMAGE_CHANGED");\n                }\n                handles.Add(record.SourcePath, stream);\n            }\n            return new EvidencePublishLock(handles.Values.ToArray());\n        }\n        catch (OcrException)\n        {\n            foreach (FileStream stream in handles.Values) stream.Dispose();\n            throw;\n        }\n        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)\n        {\n            foreach (FileStream stream in handles.Values) stream.Dispose();\n            throw new OcrException(\n                "无法锁定识别来源到结果发布完成，请重新识别。",\n                "OCR_IMAGE_CHANGED");\n        }\n    }\n\n    private sealed class EvidencePublishLock(FileStream[] streams) : IDisposable\n    {\n        public void Dispose()\n        {\n            foreach (FileStream stream in streams) stream.Dispose();\n        }\n    }\n\n'''
state_text = replace_exact(
    state_text,
    insert_before,
    lock_code + insert_before,
    "publish lock insertion")
state_path.write_text(state_text, encoding="utf-8")


main_path = Path("OcrLineTool.App/MainForm.cs")
main_text = main_path.read_text(encoding="utf-8")
normal = '''            ActiveToken.ThrowIfCancellationRequested();\n            string[] outputLines = RuleEngine.FormatOutput(rules, values, missingReasons);'''
normal_new = '''            ActiveToken.ThrowIfCancellationRequested();\n            using IDisposable publishGuard = RecognitionStateStore.LockCurrentEvidenceForPublish(\n                rules, values, evidenceLedger);\n            string[] outputLines = RuleEngine.FormatOutput(rules, values, missingReasons);'''
main_text = replace_exact(main_text, normal, normal_new, "normal publish guards", count=2)

retry = '''            ActiveToken.ThrowIfCancellationRequested();\n            string[] outputLines = RuleEngine.FormatOutput(lastRules, lastValues, lastMissingReasons);'''
retry_new = '''            ActiveToken.ThrowIfCancellationRequested();\n            using IDisposable publishGuard = RecognitionStateStore.LockCurrentEvidenceForPublish(\n                lastRules, lastValues, lastEvidenceLedger);\n            string[] outputLines = RuleEngine.FormatOutput(lastRules, lastValues, lastMissingReasons);'''
main_text = replace_exact(main_text, retry, retry_new, "retry publish guard")
main_path.write_text(main_text, encoding="utf-8")

print("Applied final F03/F14 closure patch")
