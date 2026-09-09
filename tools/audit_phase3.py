from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json
import re

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase3-applied.md').exists():
    write('OcrLineTool.App/ResultValues.cs', '''using System.Text.RegularExpressions;

namespace OcrLineTool;

/// <summary>Successful values and sticky conflicts for one recognition run.</summary>
internal sealed class ResultValues : Dictionary<string, string>
{
    internal HashSet<string> Conflicts { get; } = new(StringComparer.Ordinal);
    internal ResultValues(IEqualityComparer<string> comparer) : base(comparer) { }
    internal ResultValues(IDictionary<string, string> source, IEqualityComparer<string> comparer) : base(source, comparer)
    {
        if (source is ResultValues previous) Conflicts.UnionWith(previous.Conflicts);
    }

    internal static void AddTo(IDictionary<string, string> values, string id, string value)
    {
        if (values is not ResultValues guarded) { values.TryAdd(id, value); return; }
        if (guarded.Conflicts.Contains(id)) return;
        if (guarded.TryGetValue(id, out string? existing) && Canonical(existing) != Canonical(value))
        {
            guarded.Remove(id);
            guarded.Conflicts.Add(id);
            return;
        }
        guarded.TryAdd(id, value);
    }

    internal static bool IsConflict(IReadOnlyDictionary<string, string> values, string id) =>
        values is ResultValues guarded && guarded.Conflicts.Contains(id);

    private static string Canonical(string value)
    {
        value = value.Trim();
        if (value.Length > 0 && (value.All("马蛇龙兔虎牛鼠猪狗鸡猴羊".Contains) || value.All("金木水火土".Contains)))
            return string.Concat(value.Order());
        if (Regex.IsMatch(value, @"^\\d{2}(?:[ ,]+\\d{2})+$"))
            return string.Join(",", Regex.Split(value, "[ ,]+").Order(StringComparer.Ordinal));
        if (Regex.IsMatch(value, @"^\\d尾(?:[+ ]+\\d尾)+$"))
            return string.Join("+", Regex.Split(value, "[+ ]+").Order(StringComparer.Ordinal));
        return value;
    }
}
''')
    write('OcrLineTool.App/CloudOcrCacheStore.cs', '''using System.Text.Json;

namespace OcrLineTool;

public sealed record CloudOcrCacheEntry(int Issue, string ImagePath, IReadOnlyList<string> Lines,
    DateTimeOffset CapturedAt, string? ImageFingerprint = null, string? InputFingerprint = null);

public static class CloudOcrCacheStore
{
    private static readonly object Gate = new();
    private sealed record CacheDocument(DateOnly Date, List<CloudOcrCacheEntry> Entries);

    public static Dictionary<string, IReadOnlyList<string>> Load(string appDirectory, string groupName, int issue)
    {
        var output = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            try
            {
                string path = ResultFilePaths.ForCloudOcrCache(appDirectory, groupName);
                CacheDocument? document = ReadDocument(path);
                if (document?.Date != CredentialSchedule.TodayInBeijing()) return output;
                foreach (CloudOcrCacheEntry entry in document.Entries ?? [])
                {
                    // Legacy/path-only entries and transformed inputs are not interchangeable with whole images.
                    if (entry.Issue != issue || string.IsNullOrWhiteSpace(entry.ImagePath) ||
                        entry.ImageFingerprint is null || entry.InputFingerprint != entry.ImageFingerprint ||
                        entry.Lines is null || !entry.Lines.Any(line => !string.IsNullOrWhiteSpace(line))) continue;
                    if (Fingerprint(entry.ImagePath) == entry.ImageFingerprint)
                        output[entry.ImagePath] = entry.Lines.ToArray();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return output;
    }

    public static void SaveEntry(string appDirectory, string groupName, int issue, string imagePath,
        IReadOnlyList<string> lines, string? actualInputPath = null)
    {
        if (!lines.Any(line => !string.IsNullOrWhiteSpace(line))) return;
        lock (Gate)
        {
            try
            {
                string? imageHash = Fingerprint(imagePath);
                string? inputHash = Fingerprint(actualInputPath ?? imagePath);
                if (imageHash is null || inputHash is null) return;
                string path = ResultFilePaths.ForCloudOcrCache(appDirectory, groupName);
                DateOnly today = CredentialSchedule.TodayInBeijing();
                CacheDocument? document;
                try { document = ReadDocument(path); } catch (JsonException) { document = null; }
                List<CloudOcrCacheEntry> entries = document?.Date == today
                    ? (document.Entries ?? []).Where(entry => !(entry.Issue == issue &&
                        string.Equals(entry.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))).ToList()
                    : [];
                entries.Add(new(issue, imagePath, lines.ToArray(), DateTimeOffset.UtcNow, imageHash, inputHash));
                AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new CacheDocument(today, entries)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Cache failure must not discard an otherwise successful recognition.
            }
        }
    }

    public static void ClearExpired(string appDirectory)
    {
        lock (Gate)
        {
            string directory = ResultFilePaths.ConfigurationDirectory(appDirectory);
            if (!Directory.Exists(directory)) return;
            try
            {
                foreach (string path in Directory.EnumerateFiles(directory, "云OCR缓存_*.json"))
                {
                    try
                    {
                        CacheDocument? document = ReadDocument(path);
                        if (document is not null && document.Date != CredentialSchedule.TodayInBeijing())
                            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new CacheDocument(CredentialSchedule.TodayInBeijing(), [])));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static CacheDocument? ReadDocument(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(path)) : null;

    private static string? Fingerprint(string path)
    {
        try { return LocalOcrIdentity.Image(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
''')
    path = 'OcrLineTool.App/CredentialSchedule.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''    private static readonly Lazy<OcrSecrets> ProductionSecrets = new(
        () => OcrSecretsLoader.Load(),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);''', '''    // Do not permanently cache a failed configuration load. Reload at request boundaries.
    private static OcrSecrets ProductionSecrets => OcrSecretsLoader.Load();''')
    text = replace(text, 'TestSecrets.Value ?? ProductionSecrets.Value', 'TestSecrets.Value ?? ProductionSecrets')
    marker = '    public static OcrCredential Today() => ForDate(TodayInBeijing());'
    text = replace(text, marker, marker + '''

    public static OcrCredential DescribeSlot(int slot)
    {
        if ((uint)slot >= Credentials.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        CredentialSlot selected = Credentials[slot];
        return new(selected.Provider, selected.Slot, string.Empty, string.Empty);
    }

    public static OcrCredential DescribeDate(DateOnly date)
    {
        int slot = (date.DayNumber - Anchor.DayNumber) % Credentials.Length;
        return DescribeSlot(slot < 0 ? slot + Credentials.Length : slot);
    }

    public static OcrCredential Resolve(OcrCredential descriptor)
    {
        int slot = Array.FindIndex(Credentials, item => item.Provider == descriptor.Provider && item.Slot == descriptor.Slot);
        return ForSlot(slot);
    }

    public static OcrCredential DescribeFallback(OcrCredential credential)
    {
        int slot = Array.FindIndex(Credentials, item => item.Provider != credential.Provider && item.Slot == credential.Slot);
        if (slot < 0) slot = Array.FindIndex(Credentials, item => item.Provider != credential.Provider);
        return DescribeSlot(slot);
    }
''')
    text = replace(text, '''        return Enumerable.Range(0, Credentials.Length)
            .Select(offset => ForSlot((start + offset) % Credentials.Length))
            .ToArray();''', '''        var available = new List<OcrCredential>();
        OcrSecrets secrets;
        try { secrets = TestSecrets.Value ?? ProductionSecrets; }
        catch (OcrException) { return available; }
        for (int offset = 0; offset < Credentials.Length; offset++)
        {
            CredentialSlot selected = Credentials[(start + offset) % Credentials.Length];
            try
            {
                OcrSecret secret = secrets.Get(selected.Provider, selected.Slot);
                available.Add(new(selected.Provider, selected.Slot, secret.Id, secret.Secret));
            }
            catch (OcrException) { /* An unconfigured slot is not an unavailable application. */ }
        }
        return available;''')
    write(path, text)
    path = 'OcrLineTool.App/OcrSecretsLoader.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''                values[(OcrProvider.Tencent, slot)] = RequireTencent(document, slot);
                values[(OcrProvider.Baidu, slot)] = RequireBaidu(document, slot);''', '''                if (document.Tencent?.ContainsKey(slot) == true)
                    values[(OcrProvider.Tencent, slot)] = RequireTencent(document, slot);
                if (document.Baidu?.ContainsKey(slot) == true)
                    values[(OcrProvider.Baidu, slot)] = RequireBaidu(document, slot);''')
    write(path, text)
    path = 'OcrLineTool.App/OcrClients.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, 'public static class OcrClientFactory\n{', '''public static class OcrClientFactory
{
    public static IOcrClient CreateDeferred(OcrCredential descriptor) => new DeferredOcrClient(descriptor);

    private sealed class DeferredOcrClient(OcrCredential descriptor) : IOcrClient
    {
        private IOcrClient? client;
        public Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            client ??= Create(string.IsNullOrWhiteSpace(descriptor.Id) ? CredentialSchedule.Resolve(descriptor) : descriptor);
            return client.RecognizeAsync(imagePath, cancellationToken);
        }
    }
''')
    old = '        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)'
    count = text.count(old)
    if count == 0:
        raise RuntimeError('Expected OCR HTTP cancellation handlers')
    text = text.replace(old, '''        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
''' + old)
    write(path, text)

    path = 'OcrLineTool.App/RuleEngine.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '        rules.Select(rule => values.TryGetValue(rule.Id, out string? value)', '''        rules.Select(rule => ResultValues.IsConflict(values, rule.Id)
            ? $"缺失（同一期结果冲突，待核对） {rule.OutputLabel}"
            : values.TryGetValue(rule.Id, out string? value)''')
    # Keep difficult but identified cards for medium/cloud; shared folders still require identity.
    text = replace(text, '            return HasValueForAnyIssue(lines, rule);', '''            return HasValueForAnyIssue(lines, rule)
                || (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                    && ContainsKeyword(text, Normalize(rule.RequiredKeyword)))
                || expectedFolder.Equals(rule.RequiredKeyword ?? rule.Keyword, StringComparison.OrdinalIgnoreCase);''')
    write(path, text)

    path = 'OcrLineTool.App/MainForm.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '    private int lastIssue;', '''    private int lastIssue;
    private bool isBusy;
    private bool closeWhenIdle;
    private CancellationTokenSource? activeCancellation;
    private CancellationToken ActiveToken => activeCancellation?.Token ?? CancellationToken.None;''')
    text = replace(text, '    private Dictionary<string, string> lastValues = new(StringComparer.Ordinal);',
        '    private Dictionary<string, string> lastValues = new ResultValues(StringComparer.Ordinal);')
    text = text.replace('lastValues = new Dictionary<string, string>(', 'lastValues = new ResultValues(')
    text = text.replace('var values = new Dictionary<string, string>(', 'var values = new ResultValues(')
    text = text.replace('values.TryAdd(rule.Id, value);', 'ResultValues.AddTo(values, rule.Id, value);')
    text = replace(text, '                lastValues[rule.Id] = value;', '                ResultValues.AddTo(lastValues, rule.Id, value);')
    text = replace(text, '''                OcrRule[] activeRules = candidate.Rules
                    .Where(rule => !values.ContainsKey(rule.Id))
                    .ToArray();''', '''                // Selected competing candidates must be compared, not skipped after first success.
                OcrRule[] activeRules = candidate.Rules.ToArray();''')
    text = replace(text, '''                OcrRule[] candidateMissing = candidate.Rules
                    .Where(rule => !lastValues.ContainsKey(rule.Id))
                    .ToArray();''', '''                // Selection already contains only the rules missing at retry start.
                OcrRule[] candidateMissing = candidate.Rules.ToArray();''')
    # A cache hit is useful only if it resolves every pending rule, for every group.
    start = text.index('    private static bool CanReuseRetryCloudLines(')
    end = text.index('    private void LogMissingDetails(', start)
    text = text[:start] + '''    private static bool CanReuseRetryCloudLines(
        string groupDirectory, IReadOnlyList<OcrRule> rules, IReadOnlyList<string> lines, int issue) =>
        lines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
        rules.All(rule => RuleEngine.ExtractFinalValue(lines, issue, rule) is not null);

''' + text[end:]
    text = replace(text, '''            foreach (var cached in CloudOcrCacheStore.Load(AppContext.BaseDirectory, groupName, lastIssue))
                lastCloudOcrResults.TryAdd(cached.Key, cached.Value);''', '''            // Revalidate disk/image identity; do not retain stale in-memory paths after image replacement.
            lastCloudOcrResults = CloudOcrCacheStore.Load(AppContext.BaseDirectory, groupName, lastIssue);
            if (lastValues is ResultValues guarded) guarded.Conflicts.Clear();''')
    text = replace(text, '                if (candidateMissing.Length > 0 && !reusedCache)',
        '                if (candidateMissing.Length > 0)')
    # Store the actual OCR input. Cropped/compact cache entries cannot impersonate whole images.
    text = text.replace('groupName, issue, candidate.SourcePath, cloudLines);',
        'groupName, issue, candidate.SourcePath, cloudLines, candidate.OcrPath);')
    text = text.replace('groupName, issue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath]);',
        'groupName, issue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath], candidate.OcrPath);')
    text = text.replace('groupName, lastIssue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath]);',
        'groupName, lastIssue, candidate.SourcePath, lastCloudOcrResults[candidate.SourcePath],\n                        RetryPrimaryImage(selectedImageDirectory!, candidate.SourcePath, candidate.OcrPath, candidate.Rules));')
    text = replace(text, '''                if (lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cached) &&''', '''                if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                    lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cached) &&''')
    # Deduplicate the bytes actually sent, not a different original image.
    text = text.replace('selectedImageDirectory!, candidate.SourcePath, candidate.Rules,\n                    () => RecognizePrimaryAsync',
        'selectedImageDirectory!, candidate.OcrPath, candidate.Rules,\n                    () => RecognizePrimaryAsync')
    text = replace(text, '''                            candidate.SourcePath,
                            candidate.Rules,
                            async () =>''', '''                            candidate.OcrPath,
                            candidate.Rules,
                            async () =>''')
    text = replace(text, '''                            selectedImageDirectory!, candidate.SourcePath, candidateMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryAsync(
                                    cloudClient''', '''                            selectedImageDirectory!, RetryPrimaryImage(selectedImageDirectory!, candidate.SourcePath, candidate.OcrPath, candidateMissing), candidateMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryAsync(
                                    cloudClient''')
    text = replace(text, '        if (issueDate != date)', '        if (issueDate != date && !isBusy)')
    text = replace(text, '        issueInput.ValueChanged += (_, _) =>\n        {',
        '        issueInput.ValueChanged += (_, _) =>\n        {\n            if (isBusy) return;')
    text = replace(text, '    private void LoadExistingGroupResult()\n    {',
        '    private void LoadExistingGroupResult()\n    {\n        if (isBusy) return;')
    text = replace(text, '''        try
        {
            var localClient = new PaddleLocalOcrClient();''', '''        try
        {
            RefreshImagesForRetry();
            if (imagePaths.Length == 0) throw new OcrException("所选目录没有可识别图片。");
            var localClient = new PaddleLocalOcrClient();''')
    text = replace(text, '''        try
        {
            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath''', '''        try
        {
            RefreshImagesForRetry();
            if (imagePaths.Length == 0) throw new OcrException("所选目录没有可识别图片。");
            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath''')
    # Displaying/selecting a slot does not read all cloud secrets.
    text = text.replace('CredentialSchedule.ForDate(date).DisplayName', 'CredentialSchedule.DescribeDate(date).DisplayName')
    text = text.replace('CredentialSchedule.ForSlot(credentialSelector.SelectedIndex - 1)', 'CredentialSchedule.DescribeSlot(credentialSelector.SelectedIndex - 1)')
    text = text.replace('CredentialSchedule.Today()', 'CredentialSchedule.DescribeDate(CredentialSchedule.TodayInBeijing())')
    text = text.replace('CredentialSchedule.FallbackFor(credential)', 'CredentialSchedule.DescribeFallback(credential)')
    text = text.replace('OcrClientFactory.Create(credential)', 'OcrClientFactory.CreateDeferred(credential)')
    text = text.replace('OcrClientFactory.Create(fallbackCredential)', 'OcrClientFactory.CreateDeferred(fallbackCredential)')
    text = text.replace('await localClient.EnsureCudaAvailableAsync();', 'await localClient.EnsureCudaAvailableAsync(ActiveToken);')
    text = replace(text, '                            model: PaddleOcrModels.LocalPrimary);',
        '                            model: PaddleOcrModels.LocalPrimary, cancellationToken: ActiveToken);')
    text = replace(text, '                PaddleLocalOcrClient.DetectionMaxSideFor(selectedImageDirectory!));',
        '                PaddleLocalOcrClient.DetectionMaxSideFor(selectedImageDirectory!), cancellationToken: ActiveToken);')
    text = re.sub(r'await (client|cloudClient|fallbackClient)\.RecognizeAsync\((candidate\.(?:OcrPath|SourcePath)|imagePath)\)',
        r'await \1.RecognizeAsync(\2, ActiveToken)', text)
    text = re.sub(r'await Task\.Delay\((\w+)\);', r'await Task.Delay(\1, ActiveToken);', text)
    text = text.replace('private static async Task WaitForPacingAsync(', 'private async Task WaitForPacingAsync(')
    text = replace(text, '        return cloudResume.Task;', '        return cloudResume.Task.WaitAsync(ActiveToken);')
    text = replace(text, '    private void SetBusy(bool busy)\n    {', '''    private void SetBusy(bool busy)
    {
        isBusy = busy;
        if (busy)
        {
            activeCancellation?.Dispose();
            activeCancellation = new CancellationTokenSource();
        }''')
    text = replace(text, '            cloudResume = null;\n        }\n    }', '''            cloudResume = null;
            activeCancellation?.Dispose();
            activeCancellation = null;
            if (closeWhenIdle && !IsDisposed && IsHandleCreated) BeginInvoke(new Action(Close));
        }
    }''')
    # Cancellation of a running task is not an unexpected error, and never writes partial output.
    for name, next_name in [('RunLocalPrimaryRecognitionAsync', 'IsUnderDirectory'),
                            ('RecognizeImagesAsync', 'ManualDistributeAsync'),
                            ('RetryMissingAsync', 'RefreshImagesForRetry')]:
        start = re.search(r'    private (?:async )?(?:Task|void) ' + name + r'\(', text).start()
        end = re.search(r'    private (?:static )?(?:async )?(?:Task|void|bool) ' + next_name + r'\(', text[start:]).start() + start
        block = text[start:end]
        needle = '        catch (OcrException exception)\n        {'
        if needle not in block: raise RuntimeError('Missing outer error handler: ' + name)
        block = block.replace(needle, '''        catch (OperationCanceledException)
        {
            statusLabel.Text = "识别已取消；未完成结果不会自动分流。";
        }
''' + needle, 1)
        block = block.replace('            string[] outputLines = RuleEngine.FormatOutput(',
            '            ActiveToken.ThrowIfCancellationRequested();\n            string[] outputLines = RuleEngine.FormatOutput(')
        text = text[:start] + block + text[end:]
    text = replace(text, '    protected override void Dispose(bool disposing)', '''    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (isBusy && keyData == Keys.Escape)
        {
            activeCancellation?.Cancel();
            statusLabel.Text = "正在取消本次任务……";
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (isBusy)
        {
            e.Cancel = true;
            closeWhenIdle = true;
            activeCancellation?.Cancel();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)''')
    text = replace(text, '            dateTimer.Dispose();', '            activeCancellation?.Cancel();\n            activeCancellation?.Dispose();\n            dateTimer.Dispose();')
    text = text.replace('await File.WriteAllLinesAsync(groupOutputPath,', 'await AtomicFile.WriteAllLinesAsync(groupOutputPath,')
    text = text.replace('await File.WriteAllTextAsync(\n                diagnosticPath,', 'await AtomicFile.WriteAllTextAsync(\n                diagnosticPath,')
    # Save medium observations, not only the fallback cloud responses.
    text = replace(text, '            var mediumResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);',
        '            var mediumResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);')
    needle = '            var diagnostics = new List<object>();'
    position = text.index(needle)
    text = text[:position] + text[position:].replace(needle, '''            var diagnostics = candidates.Select(candidate => (object)new
            {
                file = Path.GetFileName(candidate.SourcePath),
                mode = "本地主识别",
                model = "medium",
                selection = candidate.SelectionMode,
                template_distance = candidate.TemplateDistance,
                rules = candidate.Rules.Select(rule => rule.Id),
                lines = candidate.LocalLines,
                error = localClient.LastImageErrors.GetValueOrDefault(candidate.OcrPath)
            }).ToList();''', 1)
    # Keep explicit per-image failures observable during candidate scanning.
    needle = '        await Task.Yield();\n        var localCandidates = new List<RecognitionCandidate>();'
    text = replace(text, needle, '''        var candidateResults = new Dictionary<string, IReadOnlyList<string>>(localResults, StringComparer.OrdinalIgnoreCase);
        foreach ((string failedPath, string error) in localClient.LastImageErrors)
        {
            candidateResults[failedPath] = [];
            statusLabel.Text = $"本地 OCR 单图失败：{ShortPath(failedPath)}；{error}";
        }
        localResults = candidateResults;
        await Task.Yield();
        var localCandidates = new List<RecognitionCandidate>();''')
    write(path, text)

    # Existing cache tests used fictional paths. Real temporary bytes are now required evidence.
    path = 'OcrLineTool.Tests/CloudOcrCacheStoreTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '        try\n        {', '''        Directory.CreateDirectory(root);
        string imageA = Path.Combine(root, "a.jpg");
        string imageB = Path.Combine(root, "b.jpg");
        File.WriteAllBytes(imageA, [1, 2, 3]);
        File.WriteAllBytes(imageB, [4, 5, 6]);
        try
        {''')
    text = text.replace('"C:\\\\图片\\\\a.jpg"', 'imageA').replace('"C:\\\\图片\\\\b.jpg"', 'imageB')
    write(path, text)
    write('docs/audit-phase3-applied.md', '# Audit repair phase 3\n\n'
        'Conflicting observed values are removed from successful output and remain sticky during a run. '
        'Selected competing candidates are compared, not silently skipped. Invalid cache hits do not suppress fallback. '
        'Disk caches validate actual source content and do not treat transformed inputs as raw images. '
        'Date refresh cannot mutate an active session; recognition rescans images at start. '
        'Cloud credentials are resolved only when requested, allow unconfigured slots, and failed loads can recover. '
        'Esc cancels the current task; window close waits for owned work to unwind. '
        'Main result and diagnostic writes use same-directory atomic replacement.\n')

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
