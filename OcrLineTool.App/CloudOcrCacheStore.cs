using System.Text.Json;

namespace OcrLineTool;

public sealed record CloudOcrCacheEntry(
    int Issue,
    string ImagePath,
    IReadOnlyList<string> Lines,
    DateTimeOffset CapturedAt,
    string? ImageFingerprint = null,
    string? InputFingerprint = null,
    string? ViewId = null,
    IReadOnlyList<OcrLineEvidence>? Items = null);

public static class CloudOcrCacheStore
{
    private static readonly object Gate = new();
    private sealed record CacheDocument(DateOnly Date, List<CloudOcrCacheEntry> Entries);

    public static Dictionary<string, IReadOnlyList<string>> Load(string appDirectory, string groupName, int issue) =>
        LoadEvidence(appDirectory, groupName, issue)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Lines.ToArray(), StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, OcrEvidence> LoadEvidence(string appDirectory, string groupName, int issue)
    {
        var output = new Dictionary<string, OcrEvidence>(StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            try
            {
                string path = ResultFilePaths.ForCloudOcrCache(appDirectory, groupName);
                CacheDocument? document = ReadDocument(path);
                if (document?.Date != CredentialSchedule.TodayInBeijing()) return output;
                foreach (CloudOcrCacheEntry entry in document.Entries ?? [])
                {
                    if (entry.Issue != issue || string.IsNullOrWhiteSpace(entry.ImagePath) ||
                        entry.ImageFingerprint is null || entry.InputFingerprint != entry.ImageFingerprint ||
                        string.IsNullOrWhiteSpace(entry.ViewId) || entry.Items is not { Count: > 0 })
                        continue;
                    string? current = Fingerprint(entry.ImagePath);
                    if (!string.Equals(current, entry.ImageFingerprint, StringComparison.OrdinalIgnoreCase))
                        continue;
                    output[entry.ImagePath] = new OcrEvidence(
                        entry.ImagePath,
                        entry.ImagePath,
                        entry.ImageFingerprint,
                        entry.InputFingerprint,
                        entry.ViewId,
                        entry.Items.ToArray());
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return output;
    }

    public static void SaveEntry(string appDirectory, string groupName, int issue, OcrEvidence evidence)
    {
        if (evidence.Items.Count == 0 || evidence.InputHash != evidence.SourceHash)
            return;
        lock (Gate)
        {
            try
            {
                string? currentSource = Fingerprint(evidence.SourcePath);
                string? currentInput = Fingerprint(evidence.InputPath);
                if (!string.Equals(currentSource, evidence.SourceHash, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(currentInput, evidence.InputHash, StringComparison.OrdinalIgnoreCase))
                    return;
                SaveEntryCore(appDirectory, groupName, issue,
                    new CloudOcrCacheEntry(
                        issue,
                        evidence.SourcePath,
                        evidence.Lines.ToArray(),
                        DateTimeOffset.UtcNow,
                        evidence.SourceHash,
                        evidence.InputHash,
                        evidence.ViewId,
                        evidence.Items.ToArray()));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // Cache failure must not discard an otherwise successful recognition.
            }
        }
    }

    // Compatibility overload. Production OCR paths use the bound evidence overload.
    public static void SaveEntry(string appDirectory, string groupName, int issue, string imagePath,
        IReadOnlyList<string> lines, string? actualInputPath = null)
    {
        if (!lines.Any(line => !string.IsNullOrWhiteSpace(line))) return;
        try
        {
            string inputPath = actualInputPath ?? imagePath;
            OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(imagePath, inputPath, "legacy-cache");
            OcrEvidence evidence = OcrEvidence.FromLines(inputPath, lines, "legacy-cache").Bind(identity);
            SaveEntry(appDirectory, groupName, issue, evidence);
        }
        catch (OcrException) { }
    }

    private static void SaveEntryCore(string appDirectory, string groupName, int issue, CloudOcrCacheEntry newEntry)
    {
        string path = ResultFilePaths.ForCloudOcrCache(appDirectory, groupName);
        DateOnly today = CredentialSchedule.TodayInBeijing();
        CacheDocument? document;
        try { document = ReadDocument(path); } catch (JsonException) { document = null; }
        List<CloudOcrCacheEntry> entries = document?.Date == today
            ? (document.Entries ?? []).Where(entry => !(entry.Issue == issue &&
                string.Equals(entry.ImagePath, newEntry.ImagePath, StringComparison.OrdinalIgnoreCase))).ToList()
            : [];
        entries.Add(newEntry);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new CacheDocument(today, entries)));
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
