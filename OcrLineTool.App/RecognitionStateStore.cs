using System.Text;
using System.Text.Json;

namespace OcrLineTool;

public sealed record ResultEvidenceRecord(
    string RuleId,
    string RuleType,
    string OutputLabel,
    string RuleSignature,
    string Value,
    string Status,
    string SourcePath,
    string InputPath,
    string SourceHash,
    string InputHash,
    string ViewId,
    string[] RegionIds,
    double? MinimumConfidence,
    int ExtractorRevision = 0);

internal sealed class ResultEvidenceLedger
{
    private readonly Dictionary<string, ResultEvidenceRecord> records = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, ResultEvidenceRecord> Records => records;

    internal void Observe(
        ResultValues values,
        OcrRule rule,
        string value,
        OcrEvidence evidence)
    {
        ResultValues.AddTo(values, rule.Id, value);
        bool conflict = ResultValues.IsConflict(values, rule.Id);
        string? accepted = null;
        if (!conflict && !values.TryGetValue(rule.Id, out accepted))
        {
            records.Remove(rule.Id);
            return;
        }

        records[rule.Id] = BuildRecord(
            rule,
            conflict ? string.Empty : accepted!,
            conflict ? "conflict" : "success",
            evidence);
    }

    private static ResultEvidenceRecord BuildRecord(
        OcrRule rule,
        string value,
        string status,
        OcrEvidence evidence) => new(
            rule.Id,
            rule.Type,
            rule.OutputLabel,
            RecognitionStateStore.RuleSignature(rule),
            value,
            status,            evidence.SourcePath,
            evidence.InputPath,
            evidence.SourceHash,
            evidence.InputHash,
            evidence.ViewId,
            evidence.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(item => item.ViewId + "/" + item.RegionId)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            evidence.MinimumConfidence,
            RecognitionStateStore.ExtractorRevision);

    internal void ObserveConflict(ResultValues values, OcrRule rule, OcrEvidence evidence)
    {
        ResultValues.MarkConflict(values, rule.Id);
        records[rule.Id] = BuildRecord(rule, string.Empty, "conflict", evidence);
    }

    internal void Seed(ResultEvidenceRecord record) => records[record.RuleId] = record;
    internal void Remove(string ruleId) => records.Remove(ruleId);
    internal void Clear() => records.Clear();
}

internal sealed record RecognitionStateLoad(
    ResultValues Values,
    ResultEvidenceLedger Evidence);

internal static class RecognitionStateStore
{
    private const int Version = 1;
    // Extraction contract revision. Bump this whenever RuleEngine output
    // semantics change so an older Success produced by a buggy extractor is
    // never restored as a trusted value. Conflicts stay reusable.
    internal const int ExtractorRevision = 3;
    private sealed record StateDocument(int Version, string Group, int Issue, List<ResultEvidenceRecord> Results);

    internal static string RuleSignature(OcrRule rule)
    {
        string json = JsonSerializer.Serialize(rule);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    internal static string[] BuildTrustedOutputLines(
        string appDirectory,
        string selectedDirectory,
        int issue,
        IReadOnlyList<OcrRule> rules)
    {
        RecognitionStateLoad restored = Load(appDirectory, selectedDirectory, issue, rules);
        bool hasConflict = rules.Any(rule => ResultValues.IsConflict(restored.Values, rule.Id));
        if (restored.Values.Count == 0 && !hasConflict)
            throw new OcrException("当前群结果没有可验证的来源状态，请重新识别后再手动分流。", "OCR_STATE_REQUIRED");
        var missingReasons = rules
            .Where(rule => !restored.Values.ContainsKey(rule.Id) && !ResultValues.IsConflict(restored.Values, rule.Id))
            .ToDictionary(rule => rule.Id, _ => "未找到可信来源状态", StringComparer.Ordinal);
        return RuleEngine.FormatOutput(rules, restored.Values, missingReasons);
    }

    internal static RecognitionStateLoad Load(
        string appDirectory,
        string selectedDirectory,
        int issue,
        IReadOnlyList<OcrRule> rules)
    {
        var values = new ResultValues(StringComparer.Ordinal);
        var evidence = new ResultEvidenceLedger();
        string path = ResultFilePaths.ForRecognitionState(appDirectory, selectedDirectory, issue);
        try
        {
            if (!File.Exists(path))
                return new(values, evidence);
            StateDocument? document = JsonSerializer.Deserialize<StateDocument>(File.ReadAllText(path));
            string expectedGroup = RuleCatalog.GroupNameForFolder(appDirectory, selectedDirectory);
            if (document is null || document.Version != Version || document.Issue != issue ||
                !string.Equals(document.Group, expectedGroup, StringComparison.Ordinal))
                return new(values, evidence);

            var byId = rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
            foreach (ResultEvidenceRecord record in document.Results ?? [])
            {
                if (string.IsNullOrWhiteSpace(record.RuleId) ||
                    !byId.TryGetValue(record.RuleId, out OcrRule? rule) ||
                    record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                    record.RuleSignature != RuleSignature(rule))
                    continue;

                if (string.Equals(record.Status, "conflict", StringComparison.Ordinal))
                {
                    // A conflict is a safety state, not a reusable success value. Keep it
                    // even if the original image has since been replaced so a previously
                    // distributed owned value can still be revoked on the next manual run.
                    values.Conflicts.Add(rule.Id);
                    evidence.Seed(record);
                    continue;
                }

                if (record.ExtractorRevision != ExtractorRevision ||
                    !string.Equals(record.Status, "success", StringComparison.Ordinal) ||
                    !RuleEngine.IsCanonicalValueValid(rule, record.Value ?? string.Empty) ||
                    string.IsNullOrWhiteSpace(record.SourcePath) || string.IsNullOrWhiteSpace(record.InputPath) ||
                    string.IsNullOrWhiteSpace(record.SourceHash) || string.IsNullOrWhiteSpace(record.InputHash) || string.IsNullOrWhiteSpace(record.ViewId) ||
                    record.RegionIds is not { Length: > 0 })
                    continue;
                try
                {
                    if (!File.Exists(record.SourcePath) || !File.Exists(record.InputPath) ||
                        !LocalOcrIdentity.Image(record.SourcePath).Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase) ||
                        !LocalOcrIdentity.Image(record.InputPath).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    continue;
                }

                ResultValues.AddTo(values, rule.Id, record.Value!);
                if (!ResultValues.IsConflict(values, rule.Id))
                    evidence.Seed(record);
            }
            return new(values, evidence);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(values, evidence);
        }
    }

    internal sealed record RecognitionStateSaveOutcome(
        IReadOnlyDictionary<string, string> FailedSuccesses);

    internal static async Task<RecognitionStateSaveOutcome> SaveAsync(
        string appDirectory,
        string selectedDirectory,
        int issue,
        IReadOnlyList<OcrRule> rules,
        IReadOnlyDictionary<string, string> values,
        ResultEvidenceLedger evidence,
        CancellationToken cancellationToken = default,
        Action? beforeSecondPersist = null)
    {
        string path = ResultFilePaths.ForRecognitionState(appDirectory, selectedDirectory, issue);
        string group = RuleCatalog.GroupNameForFolder(appDirectory, selectedDirectory);
        var results = new List<ResultEvidenceRecord>();
        var failedSuccesses = new Dictionary<string, string>(StringComparer.Ordinal);

        async Task PersistStateAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Never delete the previous checkpoint before the replacement:
            // AtomicFile already keeps the last completed file, and a failed
            // or cancelled second write must leave the first conflict state.
            await AtomicFile.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(new StateDocument(Version, group, issue, results)),
                new UTF8Encoding(false),
                cancellationToken);
        }

        // Conflicts revoke prior successes before any optional evidence copy.
        // This keeps the ledger durable when a single success snapshot is bad.
        foreach (OcrRule rule in rules)
        {
            bool conflict = ResultValues.IsConflict(values, rule.Id);
            if (!evidence.Records.TryGetValue(rule.Id, out ResultEvidenceRecord? record) ||
                record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                record.RuleSignature != RuleSignature(rule))
                continue;

            if (conflict)
            {
                results.Add(record with { Value = string.Empty, Status = "conflict" });
                continue;
            }

            if (!values.TryGetValue(rule.Id, out string? value) ||
                record.Status != "success" || record.Value != value ||
                !RuleEngine.IsCanonicalValueValid(rule, value))
                continue;
        }

        try
        {
            await PersistStateAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("无法保存带来源证明的识别状态；本次结果不会作为可恢复成功值。", "OCR_STATE_WRITE_ERROR");
        }

        foreach (OcrRule rule in rules)
        {
            if (ResultValues.IsConflict(values, rule.Id)
                || !evidence.Records.TryGetValue(rule.Id, out ResultEvidenceRecord? record)
                || record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel
                || record.RuleSignature != RuleSignature(rule)
                || record.ExtractorRevision != ExtractorRevision
                || !values.TryGetValue(rule.Id, out string? value)
                || record.Status != "success" || record.Value != value
                || !RuleEngine.IsCanonicalValueValid(rule, value))
                continue;

            // A successful OCR view may live in a temporary crop directory that
            // MainForm deletes when the run ends. Persist the exact input bytes
            // before writing trusted state, while retaining the original source
            // path/hash as the primary provenance check.
            try
            {
                record = PersistInputSnapshot(appDirectory, group, issue, record);
                evidence.Seed(record);
                results.Add(record);
            }
            catch (OcrException exception)
            {
                // A success that cannot be persisted is not a trusted success.
                // Report it so the caller removes it from values, regenerates
                // the output, and never distributes it.
                failedSuccesses[rule.Id] = exception.Message;
                evidence.Remove(rule.Id);
                if (values is ResultValues mutableValues)
                    mutableValues.Remove(rule.Id);
                results.RemoveAll(item => item.RuleId == rule.Id);
            }
        }

        beforeSecondPersist?.Invoke();
        try
        {
            await PersistStateAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("无法保存带来源证明的识别状态；本次结果不会作为可恢复成功值。", "OCR_STATE_WRITE_ERROR");
        }
        return new RecognitionStateSaveOutcome(failedSuccesses);
    }

    private static ResultEvidenceRecord PersistInputSnapshot(
        string appDirectory,
        string group,
        int issue,
        ResultEvidenceRecord record)
    {
        try
        {
            string source = Path.GetFullPath(record.SourcePath);
            string input = Path.GetFullPath(record.InputPath);
            if (!File.Exists(source)
                || !LocalOcrIdentity.Image(source).Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase))
                throw new OcrException(
                    "识别来源在状态保存前发生变化，请重新识别。",
                    "OCR_IMAGE_CHANGED");

            if (source.Equals(input, StringComparison.OrdinalIgnoreCase))
                return record with { SourcePath = source, InputPath = input };
            if (!File.Exists(input)
                || !LocalOcrIdentity.Image(input).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
                throw new OcrException(
                    "识别裁剪在状态保存前发生变化，请重新识别。",
                    "OCR_IMAGE_CHANGED");

            string directory = ResultFilePaths.RecognitionEvidenceDirectory(
                appDirectory, group, issue);
            Directory.CreateDirectory(directory);
            string extension = Path.GetExtension(input);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                extension = ".img";
            string destination = Path.Combine(directory,
                record.InputHash.ToLowerInvariant() + extension.ToLowerInvariant());

            if (!File.Exists(destination)
                || !LocalOcrIdentity.Image(destination).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
            {
                string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(input, staging, overwrite: true);
                    if (!LocalOcrIdentity.Image(staging).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
                        throw new OcrException(
                            "识别裁剪在持久化时发生变化，请重新识别。",
                            "OCR_IMAGE_CHANGED");
                    File.Move(staging, destination, overwrite: true);
                }
                finally
                {
                    try { if (File.Exists(staging)) File.Delete(staging); } catch { }
                }
            }
            return record with { SourcePath = source, InputPath = destination };
        }
        catch (OcrException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new OcrException(
                "无法持久化识别裁剪，本次结果不会作为可恢复成功值。",
                "OCR_STATE_WRITE_ERROR");
        }
    }

    /// <summary>
    /// Automatic publishing is per-rule fail-closed: an invalid success is
    /// downgraded to missing, but a different rule's already-known conflict is
    /// preserved so it can be saved and revoke an owned stale output.
    /// </summary>
    internal static IDisposable LockValidEvidenceForPublish(
        IReadOnlyList<OcrRule> rules,
        ResultValues values,
        ResultEvidenceLedger evidence,
        IDictionary<string, string> missingReasons)
    {
        var handles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (OcrRule rule in rules)
        {
            if (ResultValues.IsConflict(values, rule.Id) || !values.ContainsKey(rule.Id))
                continue;

            if (!evidence.Records.TryGetValue(rule.Id, out ResultEvidenceRecord? record)
                || record.Status != "success"
                || record.ExtractorRevision != ExtractorRevision
                || record.RuleSignature != RuleSignature(rule)
                || string.IsNullOrWhiteSpace(record.SourcePath)
                || string.IsNullOrWhiteSpace(record.InputPath)
                || string.IsNullOrWhiteSpace(record.SourceHash)
                || string.IsNullOrWhiteSpace(record.InputHash)
                || !TryLock(record.SourcePath, record.SourceHash)
                || !TryLock(record.InputPath, record.InputHash))
            {
                values.Remove(rule.Id);
                evidence.Remove(rule.Id);
                missingReasons[rule.Id] = "图片或识别视图在发布前发生变化";
            }
        }
        return new EvidencePublishLock(handles.Values.ToArray());

        bool TryLock(string path, string expectedHash)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (expectedHashes.TryGetValue(fullPath, out string? existingHash))
                    return existingHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);

                FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                string currentHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(stream));
                stream.Position = 0;
                if (!currentHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    stream.Dispose();
                    return false;
                }
                handles.Add(fullPath, stream);
                expectedHashes.Add(fullPath, expectedHash);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }

    internal static IDisposable LockCurrentEvidenceForPublish(
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
                    || record.ExtractorRevision != ExtractorRevision
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

    internal static void Invalidate(string appDirectory, string selectedDirectory, int issue)
    {
        string path = ResultFilePaths.ForRecognitionState(appDirectory, selectedDirectory, issue);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("无法清除旧识别状态，为避免复用旧成功值已停止本次识别。", "OCR_STATE_WRITE_ERROR");
        }
    }
}
