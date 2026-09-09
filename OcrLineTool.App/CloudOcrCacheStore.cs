using System.Text.Json;

namespace OcrLineTool;

public sealed record CloudOcrCacheEntry(
    int Issue,
    string ImagePath,
    IReadOnlyList<string> Lines,
    DateTimeOffset CapturedAt);

public static class CloudOcrCacheStore
{
    private sealed record CacheDocument(DateOnly Date, List<CloudOcrCacheEntry> Entries);

    public static Dictionary<string, IReadOnlyList<string>> Load(string appDirectory, string groupName, int issue)
    {
        DateOnly today = BeijingToday();
        string path = ResultFilePaths.ForCloudOcrCache(appDirectory, groupName);
        try
        {
            if (!File.Exists(path))
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            CacheDocument? document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(path));
            if (document is null || document.Date != today)
            {
                SaveDocument(path, new CacheDocument(today, []));
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }

            return document.Entries
                .Where(entry => entry.Issue == issue && !string.IsNullOrWhiteSpace(entry.ImagePath))
                .GroupBy(entry => entry.ImagePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Last().Lines.ToArray(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void SaveEntry(string appDirectory, string groupName, int issue, string imagePath, IReadOnlyList<string> lines)
    {
        string path = ResultFilePaths.ForCloudOcrCache(appDirectory, groupName);
        DateOnly today = BeijingToday();
        CacheDocument document;
        try
        {
            document = File.Exists(path)
                ? JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(path)) ?? new CacheDocument(today, [])
                : new CacheDocument(today, []);
        }
        catch (JsonException)
        {
            document = new CacheDocument(today, []);
        }

        List<CloudOcrCacheEntry> entries = document.Date == today
            ? document.Entries.Where(entry => !(entry.Issue == issue && entry.ImagePath.Equals(imagePath, StringComparison.OrdinalIgnoreCase))).ToList()
            : [];
        entries.Add(new CloudOcrCacheEntry(issue, imagePath, lines.ToArray(), DateTimeOffset.UtcNow));
        SaveDocument(path, new CacheDocument(today, entries));
    }

    public static void ClearExpired(string appDirectory)
    {
        string directory = ResultFilePaths.ConfigurationDirectory(appDirectory);
        if (!Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory, "云OCR缓存_*.json"))
        {
            try
            {
                CacheDocument? document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(path));
                if (document is not null && document.Date != BeijingToday())
                    SaveDocument(path, new CacheDocument(BeijingToday(), []));
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
    }

    private static void SaveDocument(string path, CacheDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static DateOnly BeijingToday()
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }
}
