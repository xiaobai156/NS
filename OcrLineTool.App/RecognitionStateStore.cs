using System.Text;
using System.Text.Json;

namespace OcrLineTool;

public sealed record ResultEvidenceRecord(
    string RuleId,
    string RuleType,
    string OutputLabel,
    string Value,
    string Status,
    string SourcePath,
    string SourceHash,
    string InputHash,
    string ViewId,
    string[] RegionIds,
    double? MinimumConfidence);

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
        if (ResultValues.IsConflict(values, rule.Id)
            || !values.TryGetValue(rule.Id, out string? accepted))
        {
            records.Remove(rule.Id);
            return;
        }

        records[rule.Id] = new(
            rule.Id,
            rule.Type,
            rule.OutputLabel,
            accepted,
            "success",
            evidence.SourcePath,
            evidence.SourceHash,
            evidence.InputHash,
            evidence.ViewId,
            evidence.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(item => item.ViewId + "/" + item.RegionId)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            evidence.MinimumConfidence);
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
    private sealed record StateDocument(int Version, string Group, int Issue, List<ResultEvidenceRecord> Results);

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
                if (!string.Equals(record.Status, "success", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(record.RuleId) ||
                    !byId.TryGetValue(record.RuleId, out OcrRule? rule) ||
                    record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                    !RuleEngine.IsFormattedOutputValueValid(rule, record.Value ?? string.Empty) ||
                    string.IsNullOrWhiteSpace(record.SourcePath) || string.IsNullOrWhiteSpace(record.SourceHash) ||
                    string.IsNullOrWhiteSpace(record.InputHash) || string.IsNullOrWhiteSpace(record.ViewId) ||
                    record.RegionIds is not { Length: > 0 })
                    continue;
                try
                {
                    if (!File.Exists(record.SourcePath) ||
                        !LocalOcrIdentity.Image(record.SourcePath).Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    continue;
                }

                ResultValues.AddTo(values, rule.Id, record.Value);
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

    internal static async Task SaveAsync(
        string appDirectory,
        string selectedDirectory,
        int issue,
        IReadOnlyList<OcrRule> rules,
        IReadOnlyDictionary<string, string> values,
        ResultEvidenceLedger evidence,
        CancellationToken cancellationToken = default)
    {
        string path = ResultFilePaths.ForRecognitionState(appDirectory, selectedDirectory, issue);
        string group = RuleCatalog.GroupNameForFolder(appDirectory, selectedDirectory);
        var byId = rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        var results = new List<ResultEvidenceRecord>();
        foreach ((string ruleId, string value) in values)
        {
            if (!byId.TryGetValue(ruleId, out OcrRule? rule) ||
                !evidence.Records.TryGetValue(ruleId, out ResultEvidenceRecord? record) ||
                record.Status != "success" || record.Value != value ||
                record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                !RuleEngine.IsFormattedOutputValueValid(rule, value))
                continue;
            results.Add(record);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Missing state is safe; stale state is not. Remove the previous snapshot
            // before publishing the new complete snapshot so a failed write cannot
            // resurrect a value that the current run has invalidated or conflicted.
            if (File.Exists(path)) File.Delete(path);
            await AtomicFile.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(new StateDocument(Version, group, issue, results)),
                new UTF8Encoding(false),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("无法保存带来源证明的识别状态；本次结果不会作为可恢复成功值。", "OCR_STATE_WRITE_ERROR");
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
