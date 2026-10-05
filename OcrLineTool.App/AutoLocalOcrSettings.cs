using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

internal sealed class AutoLocalOcrSettings
{
    private const string DateFormat = "yyyy-MM-dd";
    public bool Enabled { get; set; }
    public Dictionary<string, string> LastRunDates { get; set; } = new(StringComparer.Ordinal);

    private static readonly string[] VisibleAutomaticGroups =
    [
        "新澳六合彩资料",
        "新澳高手",
        "新澳高级会员",
        "蜻蜓一套",
        "黄大仙新澳"
    ];

    internal static string PathFor(string appDirectory) =>
        Path.Combine(appDirectory, "自动本地OCR设置.json");

    internal static AutoLocalOcrSettings Load(string appDirectory)
    {
        try
        {
            string path = PathFor(appDirectory);
            if (!File.Exists(path))
                return new();

            AutoLocalOcrSettings? settings = JsonSerializer.Deserialize<AutoLocalOcrSettings>(
                File.ReadAllText(path));
            if (settings is null)
                return new();

            settings.LastRunDates = new Dictionary<string, string>(
                settings.LastRunDates ?? new(), StringComparer.Ordinal);
            settings.RemoveExcludedGroups();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    internal void Save(string appDirectory)
    {
        RemoveExcludedGroups();
        AtomicFile.WriteAllText(
            PathFor(appDirectory),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(true));
    }

    private void RemoveExcludedGroups()
    {
        foreach (string key in LastRunDates.Keys.Where(IsExcludedGroup).ToArray())
            LastRunDates.Remove(key);
    }

    internal static bool IsExcludedGroup(string groupName) =>
        RuleCatalog.NormalizeGroupName(groupName)
            .Equals(RuleCatalog.NormalizeGroupName("嫣然心水"), StringComparison.OrdinalIgnoreCase);

    internal static bool IsVisibleAutomaticGroup(string groupName)
    {
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        return VisibleAutomaticGroups.Any(group =>
            normalized.Equals(RuleCatalog.NormalizeGroupName(group), StringComparison.OrdinalIgnoreCase));
    }

    internal bool HasRunToday(string groupName, DateTime nowInBeijing)
    {
        if (!IsVisibleAutomaticGroup(groupName))
            return true;

        string today = DateOnly.FromDateTime(nowInBeijing).ToString(DateFormat, CultureInfo.InvariantCulture);
        return LastRunDates.TryGetValue(groupName, out string? lastDate) && lastDate == today;
    }

    internal bool TryClaimVisibleGroup(string groupName, DateTime nowInBeijing)
    {
        if (!Enabled || !IsVisibleAutomaticGroup(groupName) || HasRunToday(groupName, nowInBeijing))
            return false;

        string today = DateOnly.FromDateTime(nowInBeijing).ToString(DateFormat, CultureInfo.InvariantCulture);
        LastRunDates[groupName] = today;
        return true;
    }
}

internal sealed class AutomaticOcrReadiness
{
    internal static readonly TimeSpan StabilityWindow = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, Observation> observations =
        new(StringComparer.OrdinalIgnoreCase);

    internal bool IsStable(string folder, string signature, DateTime nowInBeijing)
    {
        if (!observations.TryGetValue(folder, out Observation? observation) ||
            !string.Equals(observation.Signature, signature, StringComparison.Ordinal))
        {
            observations[folder] = new Observation(signature, nowInBeijing);
            return false;
        }

        return nowInBeijing - observation.StableSince >= StabilityWindow;
    }

    internal void KeepOnly(ISet<string> visibleFolders)
    {
        foreach (string folder in observations.Keys.Where(folder => !visibleFolders.Contains(folder)).ToArray())
            observations.Remove(folder);
    }

    internal void Clear() => observations.Clear();

    private sealed record Observation(string Signature, DateTime StableSince);
}
