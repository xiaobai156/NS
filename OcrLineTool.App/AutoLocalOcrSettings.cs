using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcrLineTool;

internal sealed class AutoLocalOcrExecution
{
    public string Date { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Status { get; set; } = "failed";
    public int Attempts { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class AutoLocalOcrSettings
{
    private const string DateFormat = "yyyy-MM-dd";
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);
    internal const int MaxAttempts = 3;
    private const int RetainedDays = 7;

    private static readonly Regex FullDatePrefix = new(
        @"^(?<year>\d{4})[._-](?<month>\d{1,2})[._-](?<day>\d{1,2})(?:\D|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MonthDayPrefix = new(
        @"^(?<month>\d{1,2})[._-](?<day>\d{1,2})(?:\D|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FullDateAnywhere = new(
        @"(?<!\d)(?<year>\d{4})[._-](?<month>\d{1,2})[._-](?<day>\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MonthDayAnywhere = new(
        @"(?<!\d)(?<month>\d{1,2})[._-](?<day>\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool Enabled { get; set; }
    public Dictionary<string, AutoLocalOcrExecution> Executions { get; set; } =
        new(StringComparer.Ordinal);

    internal static string PathFor(string appDirectory) =>
        Path.Combine(appDirectory, "自动本地OCR设置.json");

    internal static AutoLocalOcrSettings Load(string appDirectory, DateOnly? today = null)
    {
        try
        {
            string path = PathFor(appDirectory);
            if (!File.Exists(path))
                return new();

            string json = File.ReadAllText(path);
            AutoLocalOcrSettings? settings = JsonSerializer.Deserialize<AutoLocalOcrSettings>(json);
            if (settings is null)
                return new();

            settings.Executions = new Dictionary<string, AutoLocalOcrExecution>(
                settings.Executions ?? new(), StringComparer.Ordinal);
            settings.RemoveExcludedGroups();
            settings.RemoveOldExecutions(today ?? CredentialSchedule.TodayInBeijing());
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    internal void Save(string appDirectory, DateOnly? today = null)
    {
        RemoveExcludedGroups();
        RemoveOldExecutions(today ?? CredentialSchedule.TodayInBeijing());
        AtomicFile.WriteAllText(
            PathFor(appDirectory),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(true));
    }

    internal static bool IsExcludedGroup(string? groupName) =>
        !string.IsNullOrWhiteSpace(groupName) &&
        RuleCatalog.NormalizeGroupName(groupName)
            .Equals(RuleCatalog.NormalizeGroupName("嫣然心水"), StringComparison.OrdinalIgnoreCase);

    internal static bool IsVisibleAutomaticGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return false;
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        return VisibleAutomaticGroups.Any(group =>
            normalized.Equals(RuleCatalog.NormalizeGroupName(group), StringComparison.OrdinalIgnoreCase));
    }

    internal static DateOnly DateForFolder(string folder, DateOnly fallback)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        Match match = FullDatePrefix.Match(name);
        if (!match.Success)
            match = FullDateAnywhere.Match(name);
        if (match.Success && TryCreateDate(match, fallback.Year, out DateOnly fullDate))
            return fullDate;

        match = MonthDayPrefix.Match(name);
        if (!match.Success)
            match = MonthDayAnywhere.Match(name);
        return match.Success && TryCreateDate(match, fallback.Year, out DateOnly monthDay)
            ? monthDay
            : fallback;
    }

    internal bool HasRunToday(string groupName, DateTime nowInBeijing)
    {
        if (!IsVisibleAutomaticGroup(groupName))
            return true;
        DateOnly today = DateOnly.FromDateTime(nowInBeijing);
        return HasSuccessfulExecution(groupName, today);
    }

    internal bool CanAttemptVisibleGroup(
        string folder, string groupName, DateOnly executionDate, DateTime nowInBeijing)
    {
        if (!Enabled || !IsVisibleAutomaticGroup(groupName))
            return false;

        string key = ExecutionKey(executionDate, groupName);
        if (!Executions.TryGetValue(key, out AutoLocalOcrExecution? execution))
            return true;
        if (string.Equals(execution.Status, "success", StringComparison.OrdinalIgnoreCase) ||
            execution.Attempts >= MaxAttempts)
            return false;

        DateTimeOffset now = BeijingOffset(nowInBeijing);
        return execution.NextRetryAt is null || execution.NextRetryAt <= now;
    }

    internal bool TryBeginVisibleGroup(
        string folder, string groupName, DateOnly executionDate, DateTime nowInBeijing)
    {
        if (!CanAttemptVisibleGroup(folder, groupName, executionDate, nowInBeijing))
            return false;

        string key = ExecutionKey(executionDate, groupName);
        DateTimeOffset now = BeijingOffset(nowInBeijing);
        Executions.TryGetValue(key, out AutoLocalOcrExecution? execution);
        execution ??= new AutoLocalOcrExecution
        {
            Date = FormatDate(executionDate),
            Group = groupName
        };
        execution.Folder = folder;
        execution.Status = "running";
        execution.Attempts++;
        execution.NextRetryAt = now.Add(RetryDelay);
        execution.UpdatedAt = now;
        Executions[key] = execution;
        return true;
    }

    internal void RecordVisibleGroupResult(
        string folder,
        string groupName,
        DateOnly executionDate,
        bool success,
        DateTime nowInBeijing)
    {
        string key = ExecutionKey(executionDate, groupName);
        DateTimeOffset now = BeijingOffset(nowInBeijing);
        Executions.TryGetValue(key, out AutoLocalOcrExecution? execution);
        execution ??= new AutoLocalOcrExecution
        {
            Date = FormatDate(executionDate),
            Group = groupName,
            Attempts = 1
        };
        execution.Folder = folder;
        execution.Status = success ? "success" : "failed";
        execution.NextRetryAt = success || execution.Attempts >= MaxAttempts
            ? null
            : now.Add(RetryDelay);
        execution.UpdatedAt = now;
        Executions[key] = execution;
    }

    // Compatibility for callers that only need the current-date claim.
    internal bool TryClaimVisibleGroup(string groupName, DateTime nowInBeijing)
    {
        DateOnly date = DateOnly.FromDateTime(nowInBeijing);
        return TryBeginVisibleGroup(groupName, groupName, date, nowInBeijing);
    }

    private bool HasSuccessfulExecution(string groupName, DateOnly date) =>
        Executions.TryGetValue(ExecutionKey(date, groupName), out AutoLocalOcrExecution? execution) &&
        string.Equals(execution.Status, "success", StringComparison.OrdinalIgnoreCase);

    private void RemoveExcludedGroups()
    {
        foreach (string key in Executions.Keys
                     .Where(key => Executions[key] is null || IsExcludedGroup(Executions[key].Group))
                     .ToArray())
            Executions.Remove(key);
    }

    private void RemoveOldExecutions(DateOnly today)
    {
        DateOnly firstRetained = today.AddDays(-(RetainedDays - 1));
        foreach (string key in Executions.Keys.Where(key =>
                     Executions[key] is null ||
                     !DateOnly.TryParseExact(Executions[key].Date ?? string.Empty, DateFormat,
                         CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ||
                     date < firstRetained || date > today).ToArray())
            Executions.Remove(key);
    }

    private static string ExecutionKey(DateOnly date, string groupName) =>
        $"{FormatDate(date)}|{RuleCatalog.NormalizeGroupName(groupName)}";

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset BeijingOffset(DateTime date) =>
        new(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(8));

    private static bool TryCreateDate(Match match, int fallbackYear, out DateOnly date)
    {
        date = default;
        int year = match.Groups["year"].Success
            ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture)
            : fallbackYear;
        int month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        int day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static readonly string[] VisibleAutomaticGroups =
    [
        "新澳六合彩资料",
        "新澳高手",
        "新澳高级会员",
        "蜻蜓一套",
        "黄大仙新澳"
    ];
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
