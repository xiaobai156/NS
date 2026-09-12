using System.Text.Json;

namespace OcrLineTool;

/// <summary>
/// Removes previous-day ownership records from every configured distribution
/// target so the .ocr-state folders stay small without touching today's rows.
/// </summary>
public static class DistributionStateCleanup
{
    public static void ClearStaleBefore(string configurationDirectory, DateOnly? today = null)
    {
        DateOnly reference = today ?? CredentialSchedule.TodayInBeijing();
        foreach (string targetDirectory in EnumerateTargetDirectories(configurationDirectory))
        {
            ClearEntries(
                Path.Combine(targetDirectory, ".ocr-state"),
                entry => CredentialSchedule.BeijingDate(GetLastWriteUtc(entry)) < reference);
        }
    }

    internal static IEnumerable<string> EnumerateTargetDirectories(string configurationDirectory)
    {
        if (!Directory.Exists(configurationDirectory))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string configPath in Directory.EnumerateFiles(configurationDirectory, "*分发规则.json"))
        {
            string? target = null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
                if (document.RootElement.TryGetProperty("targetDirectory", out JsonElement element))
                    target = element.GetString();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                continue;
            }

            target = string.IsNullOrWhiteSpace(target) ? ResultDistributor.TargetDirectory : target;
            if (seen.Add(target))
                yield return target;
        }
    }

    private static void ClearEntries(string directory, Func<string, bool> shouldDelete)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    ClearEntries(entry, shouldDelete);
                    if (!Directory.EnumerateFileSystemEntries(entry).Any())
                        Directory.Delete(entry);
                    continue;
                }
                if (shouldDelete(entry))
                    File.Delete(entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static DateTime GetLastWriteUtc(string entry) =>
        Directory.Exists(entry) ? Directory.GetLastWriteTimeUtc(entry) : File.GetLastWriteTimeUtc(entry);
}
