using System.Text;

namespace OcrLineTool;

public static class OperationLog
{
    public static void AppendException(string appDirectory, string action, string? group, int issue, Exception exception)
    {
        // Only runtime metadata: Message, Data, Source and file paths can contain secrets.
        var details = new StringBuilder(action + " 未预期异常");
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            details.Append($" | {current.GetType().FullName} HResult={current.HResult:X8}");
            foreach (var frame in new System.Diagnostics.StackTrace(current, false).GetFrames())
            {
                var method = frame.GetMethod();
                if (method is null) continue;
                details.Append($" > {method.DeclaringType?.FullName}.{method.Name} MVID={method.Module.ModuleVersionId} IL={frame.GetILOffset()}");
            }
        }
        Append(appDirectory, details.ToString(), group, issue);
    }

    private static readonly object SyncRoot = new();
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void Append(string appDirectory, string action, string? group, int issue) =>
        Append(appDirectory, NowInBeijing(), action, group, issue);

    public static void Append(string appDirectory, DateTime now, string action, string? group, int issue)
    {
        try
        {
            lock (SyncRoot)
            {
                ClearIfExpiredCore(appDirectory, DateOnly.FromDateTime(now));
                string path = ResultFilePaths.ForOperationLog(appDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string line = $"{now:yyyy-MM-dd HH:mm:ss}\t{Sanitize(action)}\t群={Sanitize(group)}\t期={issue}{Environment.NewLine}";
                File.AppendAllText(path, line, Utf8);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            // Logging must never interrupt recognition or another user operation.
        }
    }

    public static void ClearIfExpired(string appDirectory) =>
        ClearIfExpired(appDirectory, NowInBeijing());

    public static void ClearIfExpired(string appDirectory, DateTime now)
    {
        try
        {
            lock (SyncRoot)
                ClearIfExpiredCore(appDirectory, DateOnly.FromDateTime(now));
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            // A log cleanup failure must not affect the running application.
        }
    }

    private static void ClearIfExpiredCore(string appDirectory, DateOnly today)
    {
        string path = ResultFilePaths.ForOperationLog(appDirectory);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return;

        using var reader = new StreamReader(path, Utf8, detectEncodingFromByteOrderMarks: true);
        string? firstLine = reader.ReadLine();
        if (firstLine?.StartsWith(today.ToString("yyyy-MM-dd"), StringComparison.Ordinal) == true)
            return;
        reader.Close();
        File.WriteAllText(path, string.Empty, Utf8);
    }

    private static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private static DateTime NowInBeijing()
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
