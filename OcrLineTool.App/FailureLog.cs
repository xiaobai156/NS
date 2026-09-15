using System.Text;
using System.Text.RegularExpressions;

namespace OcrLineTool;

/// <summary>
/// 当天失败日志：每个群一个 TXT（本地日志\失败日志\{yyyyMMdd}\{群名}.txt），文件内
/// 按期号分段；每次识别/复抓只刷新本次期号那一段，同一天其他期号的段落保留。
/// 日志只用于定位失败原因，不写凭据、云响应正文或 OCR 原文之外的敏感信息。
/// </summary>
internal static class FailureLog
{
    internal static string RootDirectory(string appDirectory) =>
        Path.Combine(appDirectory, "本地日志", "失败日志");

    internal static string DirectoryFor(string appDirectory, DateOnly date) =>
        Path.Combine(RootDirectory(appDirectory), date.ToString("yyyyMMdd"));

    internal static string PathFor(string appDirectory, DateOnly date, string groupName) =>
        Path.Combine(DirectoryFor(appDirectory, date), SanitizeName(groupName) + ".txt");

    /// <summary>写入/刷新某群某期的失败段；其他群、其他期的内容保持不变。</summary>
    internal static void WriteGroup(
        string appDirectory,
        DateOnly date,
        string groupName,
        int issue,
        DateTime now,
        IReadOnlyList<(string Label, string? ImageName, string Reason)> entries)
    {
        try
        {
            string directory = DirectoryFor(appDirectory, date);
            Directory.CreateDirectory(directory);
            string path = PathFor(appDirectory, date, groupName);

            var sections = new List<(int Issue, string Text)>();
            if (File.Exists(path))
                sections.AddRange(ParseSections(File.ReadAllText(path, Encoding.UTF8)));
            sections.RemoveAll(section => section.Issue == issue);

            var body = new StringBuilder();
            body.AppendLine($"========== {issue}期  失败 {entries.Count} 条（最后更新 {now:HH:mm}） ==========");
            if (entries.Count == 0)
            {
                body.AppendLine("（本次没有失败项）");
            }
            else
            {
                foreach (IGrouping<string, (string Label, string? ImageName, string Reason)> category in entries
                    .GroupBy(entry => CategoryFor(entry.Reason))
                    .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    body.AppendLine($"{category.Key}（{category.Count()}）");
                    foreach ((string label, string? imageName, string reason) in category
                        .OrderBy(entry => entry.Label, StringComparer.Ordinal))
                    {
                        body.AppendLine(imageName is null
                            ? $"  {label}   原因={reason}"
                            : $"  {label}   图片={imageName}   原因={reason}");
                    }
                }
            }
            sections.Add((issue, body.ToString().TrimEnd()));
            sections.Sort((left, right) => right.Issue.CompareTo(left.Issue));

            string content = string.Join(
                Environment.NewLine + Environment.NewLine,
                sections.Select(section => section.Text)) + Environment.NewLine;
            AtomicFile.WriteAllText(path, content, new UTF8Encoding(true));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            // 失败日志只用于诊断，写不了不影响识别、复抓与分流结果。
        }
    }

    /// <summary>只保留当天：启动时与跨过北京时间零点时清理旧日期目录。</summary>
    internal static void CleanupBefore(string appDirectory, DateOnly today)
    {
        try
        {
            string root = RootDirectory(appDirectory);
            if (!Directory.Exists(root))
                return;
            string todayName = today.ToString("yyyyMMdd");
            foreach (string folder in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(Path.GetFileName(folder), todayName, StringComparison.Ordinal))
                    continue;
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 单个旧目录删不掉时保留，下次启动继续尝试。
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响识别流程。
        }
    }

    private static IEnumerable<(int Issue, string Text)> ParseSections(string content)
    {
        int currentIssue = -1;
        var buffer = new List<string>();
        foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
        {
            Match heading = Regex.Match(line, @"^={5,}\s*(?<issue>\d+)期\s");
            if (heading.Success)
            {
                if (currentIssue >= 0)
                    yield return (currentIssue, string.Join(Environment.NewLine, buffer).TrimEnd());
                currentIssue = int.Parse(heading.Groups["issue"].Value);
                buffer.Clear();
                buffer.Add(line);
                continue;
            }
            if (currentIssue >= 0)
                buffer.Add(line);
        }
        if (currentIssue >= 0)
            yield return (currentIssue, string.Join(Environment.NewLine, buffer).TrimEnd());
    }

    internal static string CategoryFor(string reason)
    {
        if (reason.Contains("未找到对应图片", StringComparison.Ordinal))
            return "未找到对应图片";
        if (reason.Contains("文字识别失败", StringComparison.Ordinal))
            return "图片文字识别失败";
        if (reason.Contains("冲突", StringComparison.Ordinal))
            return "同一期结果冲突";
        if (reason.Contains("未识别到第", StringComparison.Ordinal))
            return "未识别到目标数据";
        if (reason.Contains("未通过", StringComparison.Ordinal))
            return "未通过校验";
        return "其他失败";
    }

    private static string SanitizeName(string groupName)
    {
        string trimmed = groupName.Trim();
        var builder = new StringBuilder(trimmed.Length);
        foreach (char character in trimmed)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
        }
        return builder.Length == 0 ? "未命名群" : builder.ToString();
    }
}
