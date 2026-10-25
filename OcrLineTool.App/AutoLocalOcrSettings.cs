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
    // Kept for backwards-compatible settings files; automatic OCR no longer
    // schedules another attempt after a failed run.
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class AutoLocalOcrSettings
{
    private const string DateFormat = "yyyy-MM-dd";
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
    // 按群勾选：自动识别只跑这里列出的群。旧版的 Enabled 开关保留字段但不再参与判断，
    // 新逻辑完全由 SelectedGroups 决定；嫣然心水仍排除在外。
    public List<string> SelectedGroups { get; set; } = new();
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

    // 自动识别按群勾选：只要该群被勾选（且不是排除群）就参与，不再限制固定五个群。
    internal bool IsSelectedGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName) || IsExcludedGroup(groupName))
            return false;
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        return SelectedGroups.Any(group => !string.IsNullOrWhiteSpace(group)
            && RuleCatalog.NormalizeGroupName(group).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    internal void SetSelectedGroup(string groupName, bool selected)
    {
        if (string.IsNullOrWhiteSpace(groupName) || IsExcludedGroup(groupName))
            return;
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        SelectedGroups.RemoveAll(group => string.IsNullOrWhiteSpace(group)
            || RuleCatalog.NormalizeGroupName(group).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (selected)
            SelectedGroups.Add(normalized);
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
        if (!IsSelectedGroup(groupName))
            return true;
        DateOnly today = DateOnly.FromDateTime(nowInBeijing);
        return HasExecution(groupName, today);
    }

    internal bool CanAttemptVisibleGroup(
        string folder, string groupName, DateOnly executionDate, DateTime nowInBeijing)
    {
        if (!IsSelectedGroup(groupName))
            return false;

        string key = ExecutionKey(executionDate, groupName);
        // One automatic attempt per date/group. Failed runs remain visible in
        // the log and can be handled by the user's manual retry action.
        return !Executions.ContainsKey(key);
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
        execution.NextRetryAt = null;
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
        execution.NextRetryAt = null;
        execution.UpdatedAt = now;
        Executions[key] = execution;
    }

    // Compatibility for callers that only need the current-date claim.
    internal bool TryClaimVisibleGroup(string groupName, DateTime nowInBeijing)
    {
        DateOnly date = DateOnly.FromDateTime(nowInBeijing);
        return TryBeginVisibleGroup(groupName, groupName, date, nowInBeijing);
    }

    private bool HasExecution(string groupName, DateOnly date) =>
        Executions.ContainsKey(ExecutionKey(date, groupName));

    private void RemoveExcludedGroups()
    {
        SelectedGroups ??= new List<string>();
        SelectedGroups.RemoveAll(group => string.IsNullOrWhiteSpace(group) || IsExcludedGroup(group));
        SelectedGroups = SelectedGroups
            .Select(RuleCatalog.NormalizeGroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
