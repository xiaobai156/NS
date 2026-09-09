from pathlib import Path
import re


def require_sub(text: str, pattern: str, replacement: str, label: str, count: int = 1) -> str:
    updated, actual = re.subn(pattern, lambda _: replacement, text, count=count, flags=re.S)
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

        // A complete numeric bracket is an explicit field boundary even when
        // OCR leaves unrelated title fragments around it.
        if (HasExactBracketPayload(line, expectedCount))
            return true;

        string[]? numbers = ParseNumbers(RemoveIssue(BeforeOpeningResult(simplified)));
        if (numbers is null || numbers.Length == 0)
            return false;

        bool hasLetters = Regex.IsMatch(line, @"\p{L}");
        if (hasLetters)
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

            // Remove numbers and layout punctuation, then allow only explicit
            // number-field decorations already used by reviewed production cards.
            // Arbitrary labels such as “其他栏目 10 02” cannot become continuation.
            string decoration = Regex.Replace(
                simplified, @"[0-9\s,，.。:：*【】\[\]()（）?？←→]+", string.Empty);
            bool structuralField = Regex.IsMatch(decoration,
                @"^(?:开|開|禁|杀|殺|杀码|殺碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼)$");
            if (!ownIdentity && !structuralField)
                return false;
        }

        if (numbers.Length >= 2 || !hasLetters
            || line.Contains('←') || line.Contains('→'))
            return true;
        return Regex.IsMatch(
            simplified, @"^\s*[0-9 ,，.。【】\[\]]+\s*开(?=\s*(?:[?？]+|[0-9]+|$))");
    }

    private static bool HasExactBracketPayload''',
    "number continuation body")
rule_path.write_text(rule_text, encoding="utf-8")


# F14: persist both source and actual OCR input identity, then lock every
# successful source/input from the final hash check through distribution.
state_path = Path("OcrLineTool.App/RecognitionStateStore.cs")
state_text = state_path.read_text(encoding="utf-8")
state_text = require_sub(
    state_text,
    r'(public sealed record ResultEvidenceRecord\(.*?\s+string SourcePath,\s+)string SourceHash,',
    r'''public sealed record ResultEvidenceRecord(
    string RuleId,
    string RuleType,
    string OutputLabel,
    string RuleSignature,
    string Value,
    string Status,
    string SourcePath,
    string InputPath,
    string SourceHash,''',
    "evidence record input path")
state_text = require_sub(
    state_text,
    r'(\s+evidence\.SourcePath,\s+)evidence\.SourceHash,',
    r'''            evidence.SourcePath,
            evidence.InputPath,
            evidence.SourceHash,''',
    "build record input path")
state_text = require_sub(
    state_text,
    r'string\.IsNullOrWhiteSpace\(record\.SourcePath\) \|\| string\.IsNullOrWhiteSpace\(record\.SourceHash\) \|\|\s+string\.IsNullOrWhiteSpace\(record\.InputHash\)',
    '''string.IsNullOrWhiteSpace(record.SourcePath) || string.IsNullOrWhiteSpace(record.InputPath) ||
                    string.IsNullOrWhiteSpace(record.SourceHash) || string.IsNullOrWhiteSpace(record.InputHash)''',
    "load input path validation")
state_text = require_sub(
    state_text,
    r'if \(!File\.Exists\(record\.SourcePath\) \|\|\s+!LocalOcrIdentity\.Image\(record\.SourcePath\)\.Equals\(record\.SourceHash, StringComparison\.OrdinalIgnoreCase\)\)\s+continue;',
    '''if (!File.Exists(record.SourcePath) || !File.Exists(record.InputPath) ||
                        !LocalOcrIdentity.Image(record.SourcePath).Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase) ||
                        !LocalOcrIdentity.Image(record.InputPath).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
                        continue;''',
    "load source input identity")

anchor = '    internal static void Invalidate(string appDirectory, string selectedDirectory, int issue)\n    {'
if state_text.count(anchor) != 1:
    raise RuntimeError(f"publish lock insertion: expected 1, found {state_text.count(anchor)}")
lock_code = '''    internal static IDisposable LockCurrentEvidenceForPublish(
        IReadOnlyList<OcrRule> rules,
        IReadOnlyDictionary<string, string> values,
        ResultEvidenceLedger evidence)
    {
        var handles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    || string.IsNullOrWhiteSpace(record.InputPath)
                    || string.IsNullOrWhiteSpace(record.SourceHash)
                    || string.IsNullOrWhiteSpace(record.InputHash))
                    throw new OcrException(
                        $"{rule.OutputLabel} 缺少可验证的来源状态，本次结果不会发布。",
                        "OCR_STATE_REQUIRED");

                Lock(record.SourcePath, record.SourceHash);
                Lock(record.InputPath, record.InputHash);
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

        void Lock(string path, string expectedHash)
        {
            string fullPath = Path.GetFullPath(path);
            if (expectedHashes.TryGetValue(fullPath, out string? existingHash))
            {
                if (!existingHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new OcrException("同一识别来源出现不同图片版本，请重新识别。", "OCR_IMAGE_CHANGED");
                return;
            }

            FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string currentHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(stream));
            stream.Position = 0;
            if (!currentHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                stream.Dispose();
                throw new OcrException(
                    "图片或识别视图在结果发布前发生变化，请重新识别。",
                    "OCR_IMAGE_CHANGED");
            }
            handles.Add(fullPath, stream);
            expectedHashes.Add(fullPath, expectedHash);
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

manual = '''            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
                ?? RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedImageDirectory!));
            string[] outputLines = RecognitionStateStore.BuildTrustedOutputLines(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules);'''
manual_new = '''            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
                ?? RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedImageDirectory!));
            RecognitionStateLoad trustedState = RecognitionStateStore.Load(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules);
            using IDisposable publishGuard = RecognitionStateStore.LockCurrentEvidenceForPublish(
                rules, trustedState.Values, trustedState.Evidence);
            string[] outputLines = RecognitionStateStore.BuildTrustedOutputLines(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules);'''
if main_text.count(manual) != 1:
    raise RuntimeError(f"manual distribution guard: expected 1, found {main_text.count(manual)}")
main_text = main_text.replace(manual, manual_new, 1)
main_path.write_text(main_text, encoding="utf-8")

print("Applied final F03/F14 closure patch")
