from pathlib import Path
import re


def require_sub(text: str, pattern: str, replacement: str, label: str, count: int = 1) -> str:
    updated, actual = re.subn(pattern, replacement, text, count=count, flags=re.S)
    if actual != count:
        raise RuntimeError(f"{label}: expected {count}, found {actual}")
    return updated


# F03: number continuation must stay inside the target rule's field.
rule_path = Path("OcrLineTool.App/RuleEngine.cs")
rule_text = rule_path.read_text(encoding="utf-8")

rule_text = require_sub(
    rule_text,
    r'ExtractStrictCenteredNumberWindow\(\s*lines,\s*index,\s*issue,\s*reviewedExpectedCount\)',
    'ExtractStrictCenteredNumberWindow(\n                    lines, index, issue, reviewedExpectedCount, rule)',
    "strict centered call")
rule_text = require_sub(
    rule_text,
    r'private static string\? ExtractStrictCenteredNumberWindow\(\s*string\[\] lines, int issueIndex, int issue, int expectedCount\)',
    'private static string? ExtractStrictCenteredNumberWindow(\n        string[] lines, int issueIndex, int issue, int expectedCount, OcrRule rule)',
    "strict centered signature")
rule_text = require_sub(
    rule_text,
    r'ExtractReviewedSplitNumberWindow\(\s*lines,\s*scopeStart,\s*index,\s*issue,\s*expectedCount\)',
    'ExtractReviewedSplitNumberWindow(\n                    lines, scopeStart, index, issue, expectedCount, rule)',
    "reviewed split call")
rule_text = require_sub(
    rule_text,
    r'private static string\? ExtractReviewedSplitNumberWindow\(\s*string\[\] lines, int scopeStart, int issueIndex, int issue, int expectedCount\)',
    'private static string? ExtractReviewedSplitNumberWindow(\n        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount, OcrRule rule)',
    "reviewed split signature")

next_count = rule_text.count('IsNumberContinuation(lines[next], expectedCount)')
if next_count != 1:
    raise RuntimeError(f"next continuation calls: expected 1, found {next_count}")
rule_text = rule_text.replace(
    'IsNumberContinuation(lines[next], expectedCount)',
    'IsNumberContinuation(lines[next], expectedCount, rule)', 1)
index_count = rule_text.count('IsNumberContinuation(lines[index], expectedCount)')
if index_count != 2:
    raise RuntimeError(f"indexed continuation calls: expected 2, found {index_count}")
rule_text = rule_text.replace(
    'IsNumberContinuation(lines[index], expectedCount)',
    'IsNumberContinuation(lines[index], expectedCount, rule)')

rule_text = require_sub(
    rule_text,
    r'    private static bool IsNumberContinuation\(string line, int expectedCount\)\s*\{.*?\n    \}\n\n    private static bool HasExactBracketPayload',
    '''    private static bool IsNumberContinuation(string line, int expectedCount, OcrRule rule)
    {
        string simplified = SimplifyOcrText(line);
        if (ContainsAnyIssue(line)
            || ContainsPeerIdentity(line, rule)
            || Regex.IsMatch(simplified, @"参考|旁栏|排行|统计|说明"))
            return false;

        // Text-bearing continuation rows must prove that they belong to this
        // rule. Pure numeric rows remain valid for reviewed wrapped layouts.
        bool openingTail = Regex.IsMatch(
            simplified, @"^\\s*[0-9 ,，.。]+\\s*开(?=\\s*(?:[?？]+|[0-9]+))");
        bool hasLetters = Regex.IsMatch(line, @"\\p{L}");
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

    private static bool HasExactBracketPayload''',
    "number continuation body")
rule_path.write_text(rule_text, encoding="utf-8")


# F14: keep every successful source immutable from the last hash check through
# trusted-state save and external distribution.
state_path = Path("OcrLineTool.App/RecognitionStateStore.cs")
state_text = state_path.read_text(encoding="utf-8")
anchor = '    internal static void Invalidate(string appDirectory, string selectedDirectory, int issue)\n    {'
if state_text.count(anchor) != 1:
    raise RuntimeError(f"publish lock insertion: expected 1, found {state_text.count(anchor)}")
lock_code = '''    internal static IDisposable LockCurrentEvidenceForPublish(
        IReadOnlyList<OcrRule> rules,
        IReadOnlyDictionary<string, string> values,
        ResultEvidenceLedger evidence)
    {
        var handles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (OcrRule rule in rules)
            {
                if (ResultValues.IsConflict(values, rule.Id) || !values.ContainsKey(rule.Id))
                    continue;
                if (!evidence.Records.TryGetValue(rule.Id, out ResultEvidenceRecord? record)
                    || record.Status != "success"
                    || record.RuleSignature != RuleSignature(rule)
                    || string.IsNullOrWhiteSpace(record.SourcePath)
                    || string.IsNullOrWhiteSpace(record.SourceHash))
                    throw new OcrException(
                        $"{rule.OutputLabel} 缺少可验证的来源状态，本次结果不会发布。",
                        "OCR_STATE_REQUIRED");

                if (handles.ContainsKey(record.SourcePath))
                    continue;

                FileStream stream = new(
                    record.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                string currentHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(stream));
                stream.Position = 0;
                if (!currentHash.Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase))
                {
                    stream.Dispose();
                    throw new OcrException(
                        "图片在结果发布前发生变化，请重新识别。",
                        "OCR_IMAGE_CHANGED");
                }
                handles.Add(record.SourcePath, stream);
            }
            return new EvidencePublishLock(handles.Values.ToArray());
        }
        catch (OcrException)
        {
            foreach (FileStream stream in handles.Values) stream.Dispose();
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            foreach (FileStream stream in handles.Values) stream.Dispose();
            throw new OcrException(
                "无法锁定识别来源到结果发布完成，请重新识别。",
                "OCR_IMAGE_CHANGED");
        }
    }

    private sealed class EvidencePublishLock(FileStream[] streams) : IDisposable
    {
        public void Dispose()
        {
            foreach (FileStream stream in streams) stream.Dispose();
        }
    }

'''
state_text = state_text.replace(anchor, lock_code + anchor, 1)
state_path.write_text(state_text, encoding="utf-8")


main_path = Path("OcrLineTool.App/MainForm.cs")
main_text = main_path.read_text(encoding="utf-8")
normal = '            ActiveToken.ThrowIfCancellationRequested();\n            string[] outputLines = RuleEngine.FormatOutput(rules, values, missingReasons);'
normal_new = '            ActiveToken.ThrowIfCancellationRequested();\n            using IDisposable publishGuard = RecognitionStateStore.LockCurrentEvidenceForPublish(\n                rules, values, evidenceLedger);\n            string[] outputLines = RuleEngine.FormatOutput(rules, values, missingReasons);'
if main_text.count(normal) != 2:
    raise RuntimeError(f"normal publish guards: expected 2, found {main_text.count(normal)}")
main_text = main_text.replace(normal, normal_new)
retry = '            ActiveToken.ThrowIfCancellationRequested();\n            string[] outputLines = RuleEngine.FormatOutput(lastRules, lastValues, lastMissingReasons);'
retry_new = '            ActiveToken.ThrowIfCancellationRequested();\n            using IDisposable publishGuard = RecognitionStateStore.LockCurrentEvidenceForPublish(\n                lastRules, lastValues, lastEvidenceLedger);\n            string[] outputLines = RuleEngine.FormatOutput(lastRules, lastValues, lastMissingReasons);'
if main_text.count(retry) != 1:
    raise RuntimeError(f"retry publish guard: expected 1, found {main_text.count(retry)}")
main_text = main_text.replace(retry, retry_new, 1)
main_path.write_text(main_text, encoding="utf-8")

print("Applied final F03/F14 closure patch")
