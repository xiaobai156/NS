from pathlib import Path

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')


def repl(old: str, new: str, label: str, count: int = 1):
    global text
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(f'{label}: expected {count}, found {actual}')
    text = text.replace(old, new, count)

repl(
'''                    foreach (OcrRule rule in candidate.Rules)
                    {
                        string? value = RuleEngine.ExtractFinalValue(evidence, issue, rule);
                        if (value is not null)
                            evidenceLedger.Observe(values, rule, value, evidence);
                    }''',
'''                    AddExtractedEvidenceValues(
                        evidence, candidate.Rules, issue, values, evidenceLedger);''',
'local-primary local evidence conflict propagation')

repl(
'''            lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var cloudDeduplicators = new Dictionary<OcrProvider, CloudImageDeduplicator>();''',
'''            lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var localPrimaryCloudEvidence = new Dictionary<string, OcrEvidence>(StringComparer.OrdinalIgnoreCase);
            var cloudDeduplicators = new Dictionary<OcrProvider, CloudImageDeduplicator>();''',
'local-primary structured same-run evidence cache')

repl(
'''                if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                    lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cached) &&
                    CanReuseRetryCloudLines(selectedImageDirectory!, evidenceRules, cached, issue))
                {
                    OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                        candidate.SourcePath, candidate.SourcePath, "local-primary/cloud-cache");
                    cloudEvidence = OcrEvidence.FromLines(candidate.SourcePath, cached, "cache").Bind(identity);
                    cloudProvider = "缓存";
                }''',
'''                if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                    localPrimaryCloudEvidence.TryGetValue(candidate.SourcePath, out OcrEvidence? cachedEvidence) &&
                    IsEvidenceCurrent(cachedEvidence))
                {
                    cloudEvidence = cachedEvidence;
                    cloudProvider = "本次运行证据复用";
                }''',
'local-primary no text-only evidence reconstruction')

repl(
'''                if (cloudEvidence is not null)
                {
                    foreach (OcrRule rule in evidenceRules)
                    {
                        string? value = RuleEngine.ExtractFinalValue(cloudEvidence, issue, rule);
                        if (value is not null)
                        {
                            evidenceLedger.Observe(values, rule, value, cloudEvidence);
                            recognizedRuleIds.Add(rule.Id);
                        }
                    }
                }''',
'''                if (cloudEvidence is not null)
                {
                    AddExtractedEvidenceValues(
                        cloudEvidence, evidenceRules, issue, values, evidenceLedger);
                    recognizedRuleIds.UnionWith(evidenceRules.Select(rule => rule.Id));
                }''',
'local-primary cloud evidence conflict propagation')

repl(
'''                if (cloudLines.Count > 0)
                {
                    lastCloudOcrResults[candidate.SourcePath] = cloudLines;
                    CloudOcrCacheStore.SaveEntry(AppContext.BaseDirectory, groupName, issue, candidate.SourcePath, cloudLines, candidate.OcrPath);
                }''',
'''                if (cloudEvidence is not null && cloudLines.Count > 0)
                {
                    lastCloudOcrResults[candidate.SourcePath] = cloudLines;
                    if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase))
                        localPrimaryCloudEvidence[candidate.SourcePath] = cloudEvidence;
                    CloudOcrCacheStore.SaveEntry(
                        AppContext.BaseDirectory, groupName, issue, cloudEvidence);
                }''',
'local-primary saves original bound evidence')

repl(
'''    internal static string CloudEvidenceKey(OcrEvidenceIdentity identity) =>
        string.Join("|", identity.SourcePath, identity.SourceHash, identity.InputPath, identity.InputHash, identity.ViewId);''',
'''    internal static bool IsEvidenceCurrent(OcrEvidence evidence)
    {
        try
        {
            OcrEvidenceIdentity current = OcrEvidenceIdentity.Capture(
                evidence.SourcePath, evidence.InputPath, evidence.ViewId);
            return current.SourceHash.Equals(evidence.SourceHash, StringComparison.OrdinalIgnoreCase)
                && current.InputHash.Equals(evidence.InputHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (OcrException)
        {
            return false;
        }
    }

    internal static string CloudEvidenceKey(OcrEvidenceIdentity identity) =>
        string.Join("|", identity.SourcePath, identity.SourceHash, identity.InputPath, identity.InputHash, identity.ViewId);''',
'current evidence identity helper')

path.write_text(text, encoding='utf-8')
print('Applied local-primary structured evidence gap patch')
