using System.Text.Json;

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
