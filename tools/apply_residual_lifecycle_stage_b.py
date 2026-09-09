from pathlib import Path


def patch(path: str, old: str, new: str, label: str, count: int = 1):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(f"{label}: expected {count}, found {actual}")
    p.write_text(text.replace(old, new, count), encoding="utf-8")


# RuleEngine: canonical business values are not the same representation as display text.
patch(
    "OcrLineTool.App/RuleEngine.cs",
    '''    public static bool IsFormattedOutputValueValid(OcrRule rule, string value)\n    {''',
    '''    public static bool IsCanonicalValueValid(OcrRule rule, string value)\n    {\n        value = SimplifyOcrText(value.Trim());\n        if (rule.Type == "五行")\n        {\n            return value.Length == 1 && "金木水火土".Contains(value[0])\n                || value.Length == 4 && value.All("金木水火土".Contains)\n                    && value.Distinct().Count() == 4;\n        }\n        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal))\n            return IsFormattedOutputValueValid(rule, Regex.Replace(value, @"\\s+", ","));\n        if (rule.Type is "尾数组合" or "头数组合")\n            return IsFormattedOutputValueValid(rule, value.Replace('+', ' '));\n        return IsFormattedOutputValueValid(rule, value);\n    }\n\n    public static bool IsFormattedOutputValueValid(OcrRule rule, string value)\n    {''',
    "canonical value validator",
)

# Baidu: actual request must ask the endpoint that returns location/probability.
patch(
    "OcrLineTool.App/OcrClients.cs",
    '''            new("language_type", "CHN_ENG"),\n            new("detect_direction", "true")''',
    '''            new("language_type", "CHN_ENG"),\n            new("detect_direction", "true"),\n            new("probability", "true")''',
    "baidu probability request",
)
patch(
    "OcrLineTool.App/OcrClients.cs",
    '''string url = "https://aip.baidubce.com/rest/2.0/ocr/v1/accurate_basic?access_token=" + Uri.EscapeDataString(token);''',
    '''string url = "https://aip.baidubce.com/rest/2.0/ocr/v1/accurate?access_token=" + Uri.EscapeDataString(token);''',
    "baidu positional endpoint",
)

# MainForm: precheck cache is keyed by immutable source/input versions.
patch(
    "OcrLineTool.App/MainForm.cs",
    '''            var checkedCloudEvidence = new Dictionary<string, OcrEvidence>(StringComparer.OrdinalIgnoreCase);''',
    '''            var checkedCloudEvidence = new Dictionary<string, OcrEvidence>(StringComparer.Ordinal);\n            var issueCheckEvidence = new List<OcrEvidence>();''',
    "precheck evidence containers",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                    checkedCloudEvidence[candidate.SourcePath] = await RecognizePrimaryDeduplicatedAsync(\n                        candidate, index + 1, issueCheckCount, "云 OCR 期数检查");\n                    SetProgress(index + 1, issueCheckCount);''',
    '''                    OcrEvidence precheckEvidence = await RecognizePrimaryDeduplicatedAsync(\n                        candidate, index + 1, issueCheckCount, "云 OCR 期数检查");\n                    checkedCloudEvidence[CloudEvidenceKey(precheckEvidence)] = precheckEvidence;\n                    issueCheckEvidence.Add(precheckEvidence);\n                    SetProgress(index + 1, issueCheckCount);''',
    "precheck version key",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                    issueCheckCandidates.Select(candidate => checkedCloudEvidence[candidate.SourcePath].Lines),''',
    '''                    issueCheckEvidence.Select(evidence => evidence.Lines),''',
    "precheck mismatch evidence list",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                OcrEvidence cloudEvidence;\n                if (!checkedCloudEvidence.TryGetValue(candidate.SourcePath, out cloudEvidence!))\n                {\n                    cloudEvidence = await RecognizePrimaryDeduplicatedAsync(\n                        candidate, cloudIndex, plannedCloudImages, "云 OCR");\n                }''',
    '''                OcrEvidence cloudEvidence;\n                OcrEvidenceIdentity currentPrimaryIdentity = OcrEvidenceIdentity.Capture(\n                    candidate.SourcePath, candidate.OcrPath, $"cloud-primary/{candidate.SelectionMode}");\n                if (!checkedCloudEvidence.TryGetValue(CloudEvidenceKey(currentPrimaryIdentity), out cloudEvidence!))\n                {\n                    cloudEvidence = await RecognizePrimaryDeduplicatedAsync(\n                        candidate, cloudIndex, plannedCloudImages, "云 OCR");\n                }''',
    "precheck consume current version",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                lastCloudOcrResults[candidate.SourcePath] = cloudLines;\n                CloudOcrCacheStore.SaveEntry(\n                    AppContext.BaseDirectory, groupName, issue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath], candidate.OcrPath);''',
    '''                lastCloudOcrResults[candidate.SourcePath] = cloudLines;\n                CloudOcrCacheStore.SaveEntry(\n                    AppContext.BaseDirectory, groupName, issue, cloudEvidence);''',
    "main cache saves bound evidence",
)

# Retry: never clear a known conflict and never resolve it from old cached text.
patch(
    "OcrLineTool.App/MainForm.cs",
    '''            // Revalidate disk/image identity; do not retain stale in-memory paths after image replacement.\n            lastCloudOcrResults = CloudOcrCacheStore.Load(AppContext.BaseDirectory, groupName, lastIssue);\n            if (lastValues is ResultValues guarded) guarded.Conflicts.Clear();''',
    '''            // Revalidate disk/image identity; structured cache entries retain the\n            // exact source hash and OCR evidence. Known conflicts stay sticky.\n            Dictionary<string, OcrEvidence> retryCloudEvidence =\n                CloudOcrCacheStore.LoadEvidence(AppContext.BaseDirectory, groupName, lastIssue);\n            lastCloudOcrResults = retryCloudEvidence.ToDictionary(\n                pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Lines.ToArray(),\n                StringComparer.OrdinalIgnoreCase);''',
    "retry keeps conflicts and structured cache",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                // Selection already contains only the rules missing at retry start.\n                OcrRule[] candidateMissing = candidate.Rules.ToArray();\n                if (candidateMissing.Length == 0)\n                    continue;\n\n                OcrEvidence? primaryEvidence = null;\n                bool reusedCache = lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cachedLines)\n                    && CanReuseRetryCloudLines(selectedImageDirectory!, candidateMissing, cachedLines, lastIssue);\n                if (reusedCache)\n                {\n                    // Legacy cloud cache has no geometry; each opaque line is isolated.\n                    OcrEvidenceIdentity cacheIdentity = OcrEvidenceIdentity.Capture(\n                        candidate.SourcePath, candidate.SourcePath, "retry/cloud-cache");\n                    primaryEvidence = OcrEvidence.FromLines(candidate.SourcePath, cachedLines!, "cache").Bind(cacheIdentity);\n                    AddExtractedEvidenceValues(primaryEvidence, candidateMissing, lastIssue, lastValues, lastEvidenceLedger);\n                }''',
    '''                // Selection contains the rules missing at retry start, but an already\n                // obtained whole-image response must also be compared against sibling rules\n                // that share the same configured source folder.\n                OcrRule[] candidateMissing = candidate.Rules.ToArray();\n                if (candidateMissing.Length == 0)\n                    continue;\n                OcrRule[] evidenceRules = RetryEvidenceRules(candidate, lastRules);\n\n                OcrEvidence? primaryEvidence = null;\n                bool reusedCache = retryCloudEvidence.TryGetValue(candidate.SourcePath, out OcrEvidence? cachedEvidence)\n                    && CanReuseRetryCloudEvidence(lastValues, candidateMissing, cachedEvidence, lastIssue);\n                if (reusedCache)\n                {\n                    primaryEvidence = cachedEvidence;\n                    AddExtractedEvidenceValues(primaryEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);\n                }''',
    "retry structured cache and sibling comparison",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                        AddExtractedEvidenceValues(primaryEvidence, candidateMissing, lastIssue, lastValues, lastEvidenceLedger);''',
    '''                        AddExtractedEvidenceValues(primaryEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);''',
    "retry primary compares siblings",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                        AddExtractedEvidenceValues(fallbackEvidence, candidate.Rules, lastIssue, lastValues, lastEvidenceLedger);''',
    '''                        AddExtractedEvidenceValues(fallbackEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);''',
    "retry fallback compares siblings",
)
patch(
    "OcrLineTool.App/MainForm.cs",
    '''                if (!reusedCache)\n                {\n                    IReadOnlyList<string> primaryLines = primaryEvidence?.Lines ?? [];\n                    lastCloudOcrResults[candidate.SourcePath] = primaryLines;\n                    CloudOcrCacheStore.SaveEntry(\n                        AppContext.BaseDirectory, groupName, lastIssue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath],\n                        RetryPrimaryImage(selectedImageDirectory!, candidate.SourcePath, candidate.OcrPath, candidate.Rules));\n                }''',
    '''                if (!reusedCache && primaryEvidence is not null)\n                {\n                    lastCloudOcrResults[candidate.SourcePath] = primaryEvidence.Lines;\n                    CloudOcrCacheStore.SaveEntry(\n                        AppContext.BaseDirectory, groupName, lastIssue, primaryEvidence);\n                }''',
    "retry cache saves response identity",
)

# Add small helpers next to the existing extraction helper.
patch(
    "OcrLineTool.App/MainForm.cs",
    '''    private bool CanRetryMissing() =>''',
    '''    internal static string CloudEvidenceKey(OcrEvidenceIdentity identity) =>\n        string.Join("|", identity.SourcePath, identity.SourceHash, identity.InputPath, identity.InputHash, identity.ViewId);\n\n    private static string CloudEvidenceKey(OcrEvidence evidence) =>\n        string.Join("|", evidence.SourcePath, evidence.SourceHash, evidence.InputPath, evidence.InputHash, evidence.ViewId);\n\n    internal static bool CanReuseRetryCloudEvidence(\n        IReadOnlyDictionary<string, string> values,\n        IReadOnlyList<OcrRule> rules,\n        OcrEvidence evidence,\n        int issue)\n    {\n        if (rules.Any(rule => ResultValues.IsConflict(values, rule.Id)))\n            return false;\n        try\n        {\n            if (!File.Exists(evidence.SourcePath) ||\n                !LocalOcrIdentity.Image(evidence.SourcePath).Equals(evidence.SourceHash, StringComparison.OrdinalIgnoreCase))\n                return false;\n        }\n        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)\n        {\n            return false;\n        }\n        return rules.Any(rule => RuleEngine.ExtractFinalValue(evidence, issue, rule) is not null);\n    }\n\n    private static OcrRule[] RetryEvidenceRules(RecognitionCandidate candidate, IReadOnlyList<OcrRule> allRules)\n    {\n        HashSet<string> selectedIds = candidate.Rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);\n        HashSet<string> folders = candidate.Rules\n            .Select(rule => rule.Folder)\n            .Where(folder => !string.IsNullOrWhiteSpace(folder))\n            .Select(folder => folder!)\n            .ToHashSet(StringComparer.OrdinalIgnoreCase);\n        return allRules.Where(rule => selectedIds.Contains(rule.Id)\n            || !string.IsNullOrWhiteSpace(rule.Folder) && folders.Contains(rule.Folder!)).ToArray();\n    }\n\n    private bool CanRetryMissing() =>''',
    "lifecycle helper methods",
)

print("Applied residual lifecycle stage B")
