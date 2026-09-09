from pathlib import Path

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')

def repl(old: str, new: str, expected: int = 1) -> None:
    global text
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f'expected {expected} anchors, found {count}: {old[:180]!r}')
    text = text.replace(old, new, expected)

# ---------------------------------------------------------------------------
# Persistent in-memory state is typed and paired with a provenance ledger.
# ---------------------------------------------------------------------------
repl(
'''    private Dictionary<string, string> lastValues = new ResultValues(StringComparer.Ordinal);
    private Dictionary<string, string> lastMissingReasons = new(StringComparer.Ordinal);
    private HashSet<string> lastTextRecognizedRuleIds = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyList<string>> lastCloudOcrResults = new(StringComparer.OrdinalIgnoreCase);
    private int lastIssue;''',
'''    private ResultValues lastValues = new(StringComparer.Ordinal);
    private Dictionary<string, string> lastMissingReasons = new(StringComparer.Ordinal);
    private HashSet<string> lastTextRecognizedRuleIds = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyList<string>> lastCloudOcrResults = new(StringComparer.OrdinalIgnoreCase);
    private ResultEvidenceLedger lastEvidenceLedger = new();
    private int lastIssue;''')

# ---------------------------------------------------------------------------
# Local-primary mode: carry Paddle evidence into final extraction and ledger.
# ---------------------------------------------------------------------------
repl(
'''            IReadOnlyList<RecognitionCandidate> candidates = selectedCandidates
                .Select(candidate => candidate with
                {
                    LocalLines = mediumResults.TryGetValue(candidate.OcrPath, out IReadOnlyList<string>? lines)
                        ? lines
                        : []
                })
                .ToArray();
            var values = new ResultValues(StringComparer.Ordinal);
            var recognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);

            void ApplyLocalValues(IEnumerable<RecognitionCandidate> source)
            {
                foreach (RecognitionCandidate candidate in source)
                {
                    IReadOnlyList<string> lines = candidate.LocalLines ?? [];
                    if (lines.Any(line => !string.IsNullOrWhiteSpace(line)))
                        recognizedRuleIds.UnionWith(candidate.Rules.Select(rule => rule.Id));
                    foreach (OcrRule rule in candidate.Rules)
                    {
                        string? value = RuleEngine.ExtractFinalValue(lines, issue, rule);
                        if (value is not null)
                            ResultValues.AddTo(values, rule.Id, value);
                    }
                }
            }
''',
'''            IReadOnlyList<RecognitionCandidate> candidates = selectedCandidates
                .Select(candidate => candidate with
                {
                    LocalLines = mediumResults.TryGetValue(candidate.OcrPath, out IReadOnlyList<string>? lines)
                        ? lines
                        : [],
                    LocalEvidence = BindPaddleEvidence(localClient, candidate, "local-primary/medium")
                })
                .ToArray();
            var values = new ResultValues(StringComparer.Ordinal);
            var evidenceLedger = new ResultEvidenceLedger();
            var recognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);

            void ApplyLocalValues(IEnumerable<RecognitionCandidate> source)
            {
                foreach (RecognitionCandidate candidate in source)
                {
                    OcrEvidence? evidence = candidate.LocalEvidence;
                    if (evidence is null)
                        continue;
                    if (evidence.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)))
                        recognizedRuleIds.UnionWith(candidate.Rules.Select(rule => rule.Id));
                    foreach (OcrRule rule in candidate.Rules)
                    {
                        string? value = RuleEngine.ExtractFinalValue(evidence, issue, rule);
                        if (value is not null)
                            evidenceLedger.Observe(values, rule, value, evidence);
                    }
                }
            }
''')

repl(
'''            IReadOnlyList<IReadOnlyList<string>> localSamples = candidates
                .Where(candidate => candidate.IsPrimary)
                .Select(candidate => candidate.LocalLines ?? [])
                .Take(10)
                .ToArray();''',
'''            IReadOnlyList<IReadOnlyList<string>> localSamples = candidates
                .Where(candidate => candidate.IsPrimary)
                .Select(candidate => candidate.LocalEvidence?.Lines ?? [])
                .Take(10)
                .ToArray();''')

# Local-primary cloud fallback returns a bound evidence envelope.
start = text.index('            async Task<IReadOnlyList<string>> RequestCloudFallbackAsync(RecognitionCandidate candidate)')
end = text.index('\n            var diagnostics = candidates.Select', start)
old = text[start:end]
new = '''            async Task<OcrEvidence> RequestCloudFallbackAsync(RecognitionCandidate candidate)
            {
                OcrException? lastError = null;
                foreach (OcrCredential credential in CredentialSchedule.RotationFrom(selectedCredential))
                {
                    if (string.IsNullOrWhiteSpace(credential.Id) || string.IsNullOrWhiteSpace(credential.Secret))
                        continue;

                    IOcrClient client;
                    try
                    {
                        client = OcrClientFactory.CreateDeferred(credential);
                    }
                    catch (OcrException exception)
                    {
                        lastError = exception;
                        continue;
                    }

                    if (!cloudDeduplicators.TryGetValue(credential.Provider, out CloudImageDeduplicator? deduplicator))
                        cloudDeduplicators[credential.Provider] = deduplicator = new CloudImageDeduplicator();
                    OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                        candidate.SourcePath, candidate.OcrPath,
                        $"local-primary/cloud/{credential.Provider}/{candidate.SelectionMode}");
                    try
                    {
                        OcrEvidence evidence = await deduplicator.RecognizeEvidenceAsync(
                            selectedImageDirectory!,
                            candidate.OcrPath,
                            candidate.Rules,
                            async () =>
                            {
                                if (!providerSpacing.TryGetValue(credential.Provider, out Stopwatch? spacing))
                                    providerSpacing[credential.Provider] = spacing = new Stopwatch();
                                if (providerStarted.Contains(credential.Provider))
                                    await WaitForPacingAsync(spacing, CloudOcrPolicy.MinimumInterval(credential.Provider));
                                spacing.Restart();
                                providerStarted.Add(credential.Provider);
                                cloudRequests++;
                                statusLabel.Text = $"本地主识别：云 OCR 兜底 {credential.DisplayName} · {completed + 1}/{cloudCandidates.Length} · 当前：{ShortPath(candidate.SourcePath)}";
                                return await client.RecognizeEvidenceAsync(candidate.OcrPath, ActiveToken);
                            });
                        return evidence.Bind(identity);
                    }
                    catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(credential.Provider, exception))
                    {
                        lastError = exception;
                        statusLabel.Text = $"本地主识别：{credential.DisplayName} 没有额度，切换下一个账号……";
                    }
                    catch (OcrException exception)
                    {
                        lastError = exception;
                        break;
                    }
                }

                throw lastError ?? new OcrException("没有可用的云 OCR 账号。");
            }
'''
text = text[:start] + new + text[end:]

# Local-primary loop consumes evidence; cached plain text is isolated line-by-line.
repl(
'''                IReadOnlyList<string> cloudLines = [];
                string? cloudProvider = null;
                string? cloudError = null;
                if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                    lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cached) &&
                    CanReuseRetryCloudLines(selectedImageDirectory!, evidenceRules, cached, issue))
                {
                    cloudLines = cached;
                    cloudProvider = "缓存";
                }
                else
                {
                    try
                    {
                        cloudLines = await RequestCloudFallbackAsync(candidate);
                        cloudProvider = "云 OCR";
                    }
                    catch (OcrException exception)
                    {
                        cloudError = exception.Message;
                    }
                }

                foreach (OcrRule rule in evidenceRules)
                {
                    string? value = RuleEngine.ExtractFinalValue(cloudLines, issue, rule);
                    if (value is not null)
                    {
                        ResultValues.AddTo(values, rule.Id, value);
                        recognizedRuleIds.Add(rule.Id);
                    }
                }

                if (cloudLines.Count > 0)
                {
                    lastCloudOcrResults[candidate.SourcePath] = cloudLines;
                    CloudOcrCacheStore.SaveEntry(AppContext.BaseDirectory, groupName, issue, candidate.SourcePath, cloudLines, candidate.OcrPath);
                }''',
'''                OcrEvidence? cloudEvidence = null;
                string? cloudProvider = null;
                string? cloudError = null;
                if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                    lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cached) &&
                    CanReuseRetryCloudLines(selectedImageDirectory!, evidenceRules, cached, issue))
                {
                    OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                        candidate.SourcePath, candidate.SourcePath, "local-primary/cloud-cache");
                    cloudEvidence = OcrEvidence.FromLines(candidate.SourcePath, cached, "cache").Bind(identity);
                    cloudProvider = "缓存";
                }
                else
                {
                    try
                    {
                        cloudEvidence = await RequestCloudFallbackAsync(candidate);
                        cloudProvider = "云 OCR";
                    }
                    catch (OcrException exception)
                    {
                        cloudError = exception.Message;
                    }
                }

                IReadOnlyList<string> cloudLines = cloudEvidence?.Lines ?? [];
                if (cloudEvidence is not null)
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
                }

                if (cloudLines.Count > 0)
                {
                    lastCloudOcrResults[candidate.SourcePath] = cloudLines;
                    CloudOcrCacheStore.SaveEntry(AppContext.BaseDirectory, groupName, issue, candidate.SourcePath, cloudLines, candidate.OcrPath);
                }''')

# Add provenance to local-primary diagnostics.
repl(
'''                    provider = cloudProvider,
                    lines = cloudLines,
                    error = cloudError''',
'''                    provider = cloudProvider,
                    lines = cloudLines,
                    source_hash = cloudEvidence?.SourceHash,
                    input_hash = cloudEvidence?.InputHash,
                    view = cloudEvidence?.ViewId,
                    regions = cloudEvidence?.Items.Select(item => item.ViewId + "/" + item.RegionId).Distinct(),
                    minimum_confidence = cloudEvidence?.MinimumConfidence,
                    error = cloudError''')

# Save trusted structured state before any external distribution.
repl(
'''            await AtomicFile.WriteAllLinesAsync(groupOutputPath, outputLines, new UTF8Encoding(true));
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, issue, outputLines);''',
'''            await AtomicFile.WriteAllLinesAsync(groupOutputPath, outputLines, new UTF8Encoding(true));
            await RecognitionStateStore.SaveAsync(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules, values, evidenceLedger, ActiveToken);
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, issue, outputLines);''', expected=2)

# The first occurrence above is local-primary and second normal cloud; set ledgers at both completion sites.
repl(
'''            lastTextRecognizedRuleIds = new HashSet<string>(recognizedRuleIds, StringComparer.Ordinal);
            lastIssue = issue;''',
'''            lastTextRecognizedRuleIds = new HashSet<string>(recognizedRuleIds, StringComparer.Ordinal);
            lastEvidenceLedger = evidenceLedger;
            lastIssue = issue;''')

# ---------------------------------------------------------------------------
# Normal cloud mode: real clients return positioned evidence and all business
# extraction uses the evidence overload.
# ---------------------------------------------------------------------------
repl(
'''            var values = new ResultValues(StringComparer.Ordinal);
            var missingReasons = new Dictionary<string, string>(StringComparer.Ordinal);
            var textRecognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);''',
'''            var values = new ResultValues(StringComparer.Ordinal);
            var evidenceLedger = new ResultEvidenceLedger();
            var missingReasons = new Dictionary<string, string>(StringComparer.Ordinal);
            var textRecognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);''', expected=1)

# Primary request helper.
repl(
'''            async Task<IReadOnlyList<string>> RecognizePrimaryAsync(
                RecognitionCandidate candidate,
                int displayIndex,
                int displayTotal,
                string stage)
            {
                int automaticRetry = 0;
                while (true)''',
'''            async Task<OcrEvidence> RecognizePrimaryAsync(
                RecognitionCandidate candidate,
                int displayIndex,
                int displayTotal,
                string stage)
            {
                OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.OcrPath, $"cloud-primary/{candidate.SelectionMode}");
                int automaticRetry = 0;
                while (true)''')
repl(
'''                        return await cloudClient.RecognizeAsync(candidate.OcrPath, ActiveToken);''',
'''                        OcrEvidence evidence = await cloudClient.RecognizeEvidenceAsync(candidate.OcrPath, ActiveToken);
                        return evidence.Bind(identity);''')

# Deduplicated primary helper + checked evidence.
repl(
'''            var checkedCloudLines = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            async Task<IReadOnlyList<string>> RecognizePrimaryDeduplicatedAsync(
                RecognitionCandidate candidate, int displayIndex, int displayTotal, string stage)
                => await primaryImageDeduplicator.RecognizeAsync(
                    selectedImageDirectory!, candidate.OcrPath, candidate.Rules,
                    () => RecognizePrimaryAsync(candidate, displayIndex, displayTotal, stage));''',
'''            var checkedCloudEvidence = new Dictionary<string, OcrEvidence>(StringComparer.OrdinalIgnoreCase);
            async Task<OcrEvidence> RecognizePrimaryDeduplicatedAsync(
                RecognitionCandidate candidate, int displayIndex, int displayTotal, string stage)
                => await primaryImageDeduplicator.RecognizeEvidenceAsync(
                    selectedImageDirectory!, candidate.OcrPath, candidate.Rules,
                    () => RecognizePrimaryAsync(candidate, displayIndex, displayTotal, stage));''')

# Fallback helper returns bound evidence.
repl(
'''            async Task<IReadOnlyList<string>> RecognizeFallbackAsync(
                RecognitionCandidate candidate,
                int cloudIndex,
                Action<string> setError)
            {
                int fallbackRetry = 0;
                while (true)''',
'''            async Task<OcrEvidence?> RecognizeFallbackAsync(
                RecognitionCandidate candidate,
                int cloudIndex,
                Action<string> setError)
            {
                OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.SourcePath, "cloud-fallback/original");
                int fallbackRetry = 0;
                while (true)''')
repl(
'''                        return await fallbackClient.RecognizeAsync(candidate.SourcePath, ActiveToken);''',
'''                        OcrEvidence evidence = await fallbackClient.RecognizeEvidenceAsync(candidate.SourcePath, ActiveToken);
                        return evidence.Bind(identity);''')
repl(
'''                        setError(exception.Message);
                        return [];''',
'''                        setError(exception.Message);
                        return null;''')

# Issue check uses evidence but detector still receives safe lines.
repl(
'''                    checkedCloudLines[candidate.SourcePath] = await RecognizePrimaryDeduplicatedAsync(
                        candidate, index + 1, issueCheckCount, "云 OCR 期数检查");''',
'''                    checkedCloudEvidence[candidate.SourcePath] = await RecognizePrimaryDeduplicatedAsync(
                        candidate, index + 1, issueCheckCount, "云 OCR 期数检查");''')
repl(
'''                    issueCheckCandidates.Select(candidate => checkedCloudLines[candidate.SourcePath]),''',
'''                    issueCheckCandidates.Select(candidate => checkedCloudEvidence[candidate.SourcePath].Lines),''')

# Main candidate cloud block.
repl(
'''                IReadOnlyList<string> cloudLines;
                if (!checkedCloudLines.TryGetValue(candidate.SourcePath, out cloudLines!))
                {
                    cloudLines = await RecognizePrimaryDeduplicatedAsync(
                        candidate, cloudIndex, plannedCloudImages, "云 OCR");
                }

                SetProgress(cloudIndex, plannedCloudImages);
                var matchedValues = new List<string>();
                var missingRules = new List<OcrRule>();
                foreach (OcrRule rule in activeRules)
                {
                    string? value = RuleEngine.ExtractFinalValue(cloudLines, issue, rule);
                    if (value is not null)
                    {
                        ResultValues.AddTo(values, rule.Id, value);
                        matchedValues.Add($"{value} {rule.OutputLabel}");
                    }
                    else if (!values.ContainsKey(rule.Id))
                    {
                        missingRules.Add(rule);
                    }
                }

                IReadOnlyList<string>? fallbackLines = null;
                string? fallbackError = null;
                if (missingRules.Count > 0)
                {
                    fallbackLines = await fallbackImageDeduplicator.RecognizeAsync(
                        selectedImageDirectory!, candidate.SourcePath, candidate.Rules,
                        () => RecognizeFallbackAsync(candidate, cloudIndex, error => fallbackError = error));

                    foreach (OcrRule rule in activeRules.Where(_ => fallbackLines is not null))
                    {
                        string? value = RuleEngine.ExtractFinalValue(fallbackLines!, issue, rule);
                        if (value is null)
                            continue;
                        ResultValues.AddTo(values, rule.Id, value);
                        matchedValues.Add($"{value} {rule.OutputLabel}");
                    }
                }

                bool recognizedText = cloudLines.Any(line => !string.IsNullOrWhiteSpace(line))
                    || fallbackLines?.Any(line => !string.IsNullOrWhiteSpace(line)) == true;
                    lastCloudOcrResults[candidate.SourcePath] = cloudLines;''',
'''                OcrEvidence cloudEvidence;
                if (!checkedCloudEvidence.TryGetValue(candidate.SourcePath, out cloudEvidence!))
                {
                    cloudEvidence = await RecognizePrimaryDeduplicatedAsync(
                        candidate, cloudIndex, plannedCloudImages, "云 OCR");
                }
                IReadOnlyList<string> cloudLines = cloudEvidence.Lines;

                SetProgress(cloudIndex, plannedCloudImages);
                var matchedValues = new List<string>();
                var missingRules = new List<OcrRule>();
                foreach (OcrRule rule in activeRules)
                {
                    string? value = RuleEngine.ExtractFinalValue(cloudEvidence, issue, rule);
                    if (value is not null)
                    {
                        evidenceLedger.Observe(values, rule, value, cloudEvidence);
                        matchedValues.Add($"{value} {rule.OutputLabel}");
                    }
                    else if (!values.ContainsKey(rule.Id))
                    {
                        missingRules.Add(rule);
                    }
                }

                OcrEvidence? fallbackEvidence = null;
                string? fallbackError = null;
                if (missingRules.Count > 0)
                {
                    fallbackEvidence = await fallbackImageDeduplicator.RecognizeEvidenceAsync(
                        selectedImageDirectory!, candidate.SourcePath, candidate.Rules,
                        async () => await RecognizeFallbackAsync(candidate, cloudIndex, error => fallbackError = error)
                            ?? OcrEvidence.FromLines(candidate.SourcePath, [], "fallback-empty"));

                    if (fallbackEvidence.Items.Count > 0)
                    {
                        foreach (OcrRule rule in activeRules)
                        {
                            string? value = RuleEngine.ExtractFinalValue(fallbackEvidence, issue, rule);
                            if (value is null)
                                continue;
                            evidenceLedger.Observe(values, rule, value, fallbackEvidence);
                            matchedValues.Add($"{value} {rule.OutputLabel}");
                        }
                    }
                }
                IReadOnlyList<string>? fallbackLines = fallbackEvidence?.Lines;

                bool recognizedText = cloudEvidence.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text))
                    || fallbackEvidence?.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)) == true;
                lastCloudOcrResults[candidate.SourcePath] = cloudLines;''')

# Diagnostics provenance for primary/fallback.
repl(
'''                    primary_provider = credential.DisplayName,
                    primary_lines = cloudLines,
                    fallback_provider = fallbackLines is null ? null : fallbackCredential.DisplayName,
                    fallback_lines = fallbackLines,
                    fallback_error = fallbackError''',
'''                    primary_provider = credential.DisplayName,
                    primary_lines = cloudLines,
                    primary_source_hash = cloudEvidence.SourceHash,
                    primary_input_hash = cloudEvidence.InputHash,
                    primary_view = cloudEvidence.ViewId,
                    primary_regions = cloudEvidence.Items.Select(item => item.ViewId + "/" + item.RegionId).Distinct(),
                    primary_minimum_confidence = cloudEvidence.MinimumConfidence,
                    fallback_provider = fallbackEvidence is null ? null : fallbackCredential.DisplayName,
                    fallback_lines = fallbackLines,
                    fallback_source_hash = fallbackEvidence?.SourceHash,
                    fallback_input_hash = fallbackEvidence?.InputHash,
                    fallback_view = fallbackEvidence?.ViewId,
                    fallback_regions = fallbackEvidence?.Items.Select(item => item.ViewId + "/" + item.RegionId).Distinct(),
                    fallback_minimum_confidence = fallbackEvidence?.MinimumConfidence,
                    fallback_error = fallbackError''')

# Completion of normal cloud mode stores its ledger.
repl(
'''            lastTextRecognizedRuleIds = new HashSet<string>(textRecognizedRuleIds, StringComparer.Ordinal);
            lastIssue = issue;''',
'''            lastTextRecognizedRuleIds = new HashSet<string>(textRecognizedRuleIds, StringComparer.Ordinal);
            lastEvidenceLedger = evidenceLedger;
            lastIssue = issue;''')

# ---------------------------------------------------------------------------
# Retry mode: cache is unpositioned evidence, new requests retain structure,
# and the existing provenance ledger is updated/stickily invalidated on conflict.
# ---------------------------------------------------------------------------
repl(
'''                IReadOnlyList<string> primaryLines = [];
                bool reusedCache = lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cachedLines)
                    && CanReuseRetryCloudLines(selectedImageDirectory!, candidateMissing, cachedLines, lastIssue);
                if (reusedCache)
                {
                    // 初次识别已经请求过这张图，补抓时复用 OCR 文本，不重复消耗云 OCR。
                    primaryLines = cachedLines!;
                    AddExtractedValues(primaryLines, candidateMissing, lastIssue, lastValues);
                }
                else
                {''',
'''                OcrEvidence? primaryEvidence = null;
                bool reusedCache = lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cachedLines)
                    && CanReuseRetryCloudLines(selectedImageDirectory!, candidateMissing, cachedLines, lastIssue);
                if (reusedCache)
                {
                    // Legacy cloud cache has no geometry; each opaque line is isolated.
                    OcrEvidenceIdentity cacheIdentity = OcrEvidenceIdentity.Capture(
                        candidate.SourcePath, candidate.SourcePath, "retry/cloud-cache");
                    primaryEvidence = OcrEvidence.FromLines(candidate.SourcePath, cachedLines!, "cache").Bind(cacheIdentity);
                    AddExtractedValues(primaryEvidence, candidateMissing, lastIssue, lastValues, lastEvidenceLedger);
                }
                else
                {''')

repl(
'''                        primaryLines = await primaryImageDeduplicator.RecognizeAsync(
                            selectedImageDirectory!, RetryPrimaryImage(selectedImageDirectory!, candidate.SourcePath, candidate.OcrPath, candidateMissing), candidateMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryAsync(
                                    cloudClient, credential,
                                    RetryPrimaryImage(
                                        selectedImageDirectory!, candidate.SourcePath, candidate.OcrPath, candidateMissing),
                                    completed + 1, selection.Candidates.Count, candidateMissing, lastIssue, lastValues);
                            });
                        AddExtractedValues(primaryLines, candidateMissing, lastIssue, lastValues);''',
'''                        string primaryInputPath = RetryPrimaryImage(
                            selectedImageDirectory!, candidate.SourcePath, candidate.OcrPath, candidateMissing);
                        primaryEvidence = await primaryImageDeduplicator.RecognizeEvidenceAsync(
                            selectedImageDirectory!, primaryInputPath, candidateMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryEvidenceAsync(
                                    cloudClient, credential, candidate.SourcePath, primaryInputPath,
                                    completed + 1, selection.Candidates.Count);
                            });
                        AddExtractedValues(primaryEvidence, candidateMissing, lastIssue, lastValues, lastEvidenceLedger);''')

repl(
'''                IReadOnlyList<string> fallbackLines = [];
                if (fallbackMissing.Length > 0)''',
'''                OcrEvidence? fallbackEvidence = null;
                if (fallbackMissing.Length > 0)''')
repl(
'''                        fallbackLines = await fallbackImageDeduplicator.RecognizeAsync(
                            selectedImageDirectory!, candidate.SourcePath, fallbackMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryAsync(
                                    fallbackClient, fallbackCredential, candidate.SourcePath, completed + 1, selection.Candidates.Count,
                                    fallbackMissing, lastIssue, lastValues);
                            });
                        AddExtractedValues(fallbackLines, candidate.Rules, lastIssue, lastValues);''',
'''                        fallbackEvidence = await fallbackImageDeduplicator.RecognizeEvidenceAsync(
                            selectedImageDirectory!, candidate.SourcePath, fallbackMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryEvidenceAsync(
                                    fallbackClient, fallbackCredential, candidate.SourcePath, candidate.SourcePath,
                                    completed + 1, selection.Candidates.Count);
                            });
                        AddExtractedValues(fallbackEvidence, candidate.Rules, lastIssue, lastValues, lastEvidenceLedger);''')

repl(
'''                    lastCloudOcrResults[candidate.SourcePath] = primaryLines;
                    CloudOcrCacheStore.SaveEntry(
                        AppContext.BaseDirectory, groupName, lastIssue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath],''',
'''                    IReadOnlyList<string> primaryLines = primaryEvidence?.Lines ?? [];
                    lastCloudOcrResults[candidate.SourcePath] = primaryLines;
                    CloudOcrCacheStore.SaveEntry(
                        AppContext.BaseDirectory, groupName, lastIssue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath],''')

repl(
'''                bool recognizedText = primaryLines.Any(line => !string.IsNullOrWhiteSpace(line))
                    || fallbackLines.Any(line => !string.IsNullOrWhiteSpace(line));''',
'''                bool recognizedText = primaryEvidence?.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)) == true
                    || fallbackEvidence?.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)) == true;''')

# Retry state save before distribution (unique lastIssue form).
repl(
'''            await AtomicFile.WriteAllLinesAsync(groupOutputPath, outputLines, new UTF8Encoding(true));
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, lastIssue, outputLines);''',
'''            await AtomicFile.WriteAllLinesAsync(groupOutputPath, outputLines, new UTF8Encoding(true));
            await RecognitionStateStore.SaveAsync(
                AppContext.BaseDirectory, selectedImageDirectory!, lastIssue, lastRules, lastValues, lastEvidenceLedger, ActiveToken);
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, lastIssue, outputLines);''')

# Replace retry request helper; extraction is now caller-side with evidence.
start = text.index('    private async Task<IReadOnlyList<string>> RecognizeRetryAsync(')
end = text.index('\n    private async Task WaitForPacingAsync', start)
text = text[:start] + '''    private async Task<OcrEvidence> RecognizeRetryEvidenceAsync(
        IOcrClient client,
        OcrCredential credential,
        string sourcePath,
        string inputPath,
        int current,
        int total)
    {
        OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
            sourcePath, inputPath, $"retry/{credential.Provider}");
        int retry = 0;
        while (true)
        {
            try
            {
                OcrEvidence evidence = await client.RecognizeEvidenceAsync(inputPath, ActiveToken);
                return evidence.Bind(identity);
            }
            catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(credential.Provider, exception))
            {
                if (retry < CloudOcrPolicy.MaxAutomaticRetries)
                {
                    TimeSpan delay = CloudOcrPolicy.RetryDelay(++retry);
                    statusLabel.Text = $"复抓触发限流：{delay.TotalSeconds:0} 秒后重试 {current}/{total}";
                    await Task.Delay(delay, ActiveToken);
                    continue;
                }

                await WaitForCloudResumeAsync(current, total, sourcePath);
                retry = 0;
            }
        }
    }
''' + text[end:]

# Preserve old string helper for unit-level conflict tests; add evidence helper.
repl(
'''    private static void AddExtractedValues(
        IReadOnlyList<string> lines,
        IEnumerable<OcrRule> rules,
        int issue,
        IDictionary<string, string> values)
    {
        foreach (OcrRule rule in rules)
        {
            string? value = RuleEngine.ExtractFinalValue(lines, issue, rule);
            if (value is not null)
                ResultValues.AddTo(values, rule.Id, value);
        }
    }
''',
'''    private static void AddExtractedValues(
        IReadOnlyList<string> lines,
        IEnumerable<OcrRule> rules,
        int issue,
        IDictionary<string, string> values)
    {
        foreach (OcrRule rule in rules)
        {
            string? value = RuleEngine.ExtractFinalValue(lines, issue, rule);
            if (value is not null)
                ResultValues.AddTo(values, rule.Id, value);
        }
    }

    private static void AddExtractedValues(
        OcrEvidence evidence,
        IEnumerable<OcrRule> rules,
        int issue,
        ResultValues values,
        ResultEvidenceLedger ledger)
    {
        foreach (OcrRule rule in rules)
        {
            string? value = RuleEngine.ExtractFinalValue(evidence, issue, rule);
            if (value is not null)
                ledger.Observe(values, rule, value, evidence);
        }
    }
''')

# ---------------------------------------------------------------------------
# F16: TXT is display only. Trusted retry values come solely from the structured
# state and are revalidated against current rule/type and current source hash.
# ---------------------------------------------------------------------------
start = text.index('    private void RestoreRetryState(IReadOnlyList<string> lines)\n    {')
end = text.index('\n    private static string MissingReasonFrom', start)
text = text[:start] + '''    private void RestoreRetryState(IReadOnlyList<string> lines)
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
            ?? throw new OcrException("请先选择要读取的子文件夹。"));
        lastRules = rules;
        lastMissingReasons = new Dictionary<string, string>(StringComparer.Ordinal);
        lastTextRecognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        lastIssue = Decimal.ToInt32(issueInput.Value);

        RecognitionStateLoad restored = selectedImageDirectory is null
            ? new(new ResultValues(StringComparer.Ordinal), new ResultEvidenceLedger())
            : RecognitionStateStore.Load(
                AppContext.BaseDirectory, selectedImageDirectory, lastIssue, rules);
        lastValues = restored.Values;
        lastEvidenceLedger = restored.Evidence;
        lastTextRecognizedRuleIds.UnionWith(lastValues.Keys);

        foreach (string line in lines)
        {
            OcrRule? rule = rules
                .OrderByDescending(candidate => candidate.OutputLabel.Length)
                .FirstOrDefault(candidate =>
                    line.EndsWith(candidate.OutputLabel + "（已分流）", StringComparison.Ordinal) ||
                    line.EndsWith(candidate.OutputLabel, StringComparison.Ordinal));
            if (rule is null)
                continue;

            string distributedSuffix = rule.OutputLabel + "（已分流）";
            string suffix = line.EndsWith(distributedSuffix, StringComparison.Ordinal)
                ? distributedSuffix
                : rule.OutputLabel;
            string value = line[..^suffix.Length].Trim();
            if (value.StartsWith("缺失", StringComparison.Ordinal))
            {
                if (!lastValues.ContainsKey(rule.Id))
                    lastMissingReasons[rule.Id] = MissingReasonFrom(value);
                continue;
            }

            if (lastValues.ContainsKey(rule.Id))
                continue; // Structured state, not the TXT text, is the trusted source.
            lastMissingReasons[rule.Id] = RuleEngine.IsFormattedOutputValueValid(rule, value)
                ? "保存TXT仅用于展示，未找到有效来源状态"
                : "保存结果未通过当前规则校验";
        }

        retryMissingButton.Enabled = CanRetryMissing();
    }
''' + text[end:]

repl(
'''        lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        lastIssue = 0;''',
'''        lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        lastEvidenceLedger = new ResultEvidenceLedger();
        lastIssue = 0;''')

# ---------------------------------------------------------------------------
# Candidate selection carries Paddle evidence when available.
# ---------------------------------------------------------------------------
repl(
'''                localCandidates.Add(new RecognitionCandidate(
                    plan.Path,
                    ocrPath,
                    plan.Rules,
                    plan.IsPrimary,
                    (plan.IsPrimary ? "本地OCR首选" : "本地OCR备选") + (ocrPath == plan.Path ? "" : "（杰少密集表横向压缩整图）"),
                    null));''',
'''                localCandidates.Add(new RecognitionCandidate(
                    plan.Path,
                    ocrPath,
                    plan.Rules,
                    plan.IsPrimary,
                    (plan.IsPrimary ? "本地OCR首选" : "本地OCR备选") + (ocrPath == plan.Path ? "" : "（杰少密集表横向压缩整图）"),
                    null,
                    localResults.TryGetValue(plan.Path, out IReadOnlyList<string>? planLines) ? planLines : [],
                    BindPaddleEvidence(localClient, plan.Path, plan.Path, "candidate-small")));''')
repl(
'''            if (matched.Count > 0)
                localCandidates.Add(new RecognitionCandidate(path, path, matched, true, "本地OCR", null));''',
'''            if (matched.Count > 0)
                localCandidates.Add(new RecognitionCandidate(
                    path, path, matched, true, "本地OCR", null, lines,
                    BindPaddleEvidence(localClient, path, path, "candidate-small")));''')

# Candidate record gains structured local evidence.
repl(
'''    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null);''',
'''    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null,
        OcrEvidence? LocalEvidence = null);''')

# Helper binds Paddle result to source/input identity captured from current files.
repl(
'''    private static bool IsCompactJieshaoTable(string groupDirectory, IReadOnlyList<OcrRule> rules) =>''',
'''    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        RecognitionCandidate candidate,
        string stage) =>
        BindPaddleEvidence(client, candidate.SourcePath, candidate.OcrPath, stage + "/" + candidate.SelectionMode);

    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        string sourcePath,
        string inputPath,
        string stage)
    {
        if (!client.LastEvidence.TryGetValue(inputPath, out OcrEvidence? evidence))
            return null;
        OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(sourcePath, inputPath, stage);
        return evidence.Bind(identity);
    }

    private static bool IsCompactJieshaoTable(string groupDirectory, IReadOnlyList<OcrRule> rules) =>''')

path.write_text(text, encoding='utf-8')
print('Applied F18 business evidence and F16 structured-state integration')
