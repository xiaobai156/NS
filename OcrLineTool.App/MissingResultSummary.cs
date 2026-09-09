using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcrLineTool;

public static class MissingResultSummary
{
    public static async Task<(string Path, int Count)> WriteAsync(string appDirectory, string? imageRoot = null)
    {
        string directory = ResultFilePaths.GroupResultsDirectory(appDirectory);
        string outputPath = ResultFilePaths.ForMissingSummary(appDirectory);
        Directory.CreateDirectory(directory);
        var groups = new SortedDictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        int count = 0;

        foreach (string path in Directory.EnumerateFiles(directory, "*.txt")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase))
                continue;
            // Read only the application's group-result naming format, not notes or other reports.
            Match name = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^(.+)_\d+期$");
            if (!name.Success)
                continue;
            string groupName = name.Groups[1].Value;
            string rulePath = RuleCatalog.PathForFolder(appDirectory, groupName);
            OcrRule[] rules = File.Exists(rulePath)
                ? RuleCatalog.Load(rulePath).OrderByDescending(rule => rule.OutputLabel.Length).ToArray()
                : [];
            var diagnosticFolders = ResolveDiagnosticFolders(appDirectory, imageRoot, Path.GetFileNameWithoutExtension(path), groupName,
                rules.Where(rule => string.IsNullOrWhiteSpace(rule.Folder)).Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal));
            string category = "其他";
            foreach (string rawLine in await File.ReadAllLinesAsync(path).ConfigureAwait(false))
            {
                string line = GroupResultFormatter.RemoveLegacySourceSuffix(rawLine).Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("来源：", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("【", StringComparison.Ordinal) && line.EndsWith("】", StringComparison.Ordinal))
                {
                    category = line[1..^1];
                    continue;
                }
                if (!line.StartsWith("缺失", StringComparison.Ordinal) &&
                    line.EndsWith("（已分流）", StringComparison.Ordinal))
                    continue;

                if (!groups.TryGetValue(groupName, out var categories))
                    groups[groupName] = categories = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                if (!categories.TryGetValue(category, out var entries))
                    categories[category] = entries = [];
                string summaryLine = line;
                string labelLine = line.EndsWith("（未分流）", StringComparison.Ordinal)
                    ? line[..^5].TrimEnd() : line;
                OcrRule? rule = rules.FirstOrDefault(candidate =>
                    labelLine.Equals(candidate.OutputLabel, StringComparison.Ordinal) ||
                    labelLine.EndsWith(" " + candidate.OutputLabel, StringComparison.Ordinal));
                string? folderName = rule?.Folder;
                if (string.IsNullOrWhiteSpace(folderName) && rule is not null)
                    diagnosticFolders.TryGetValue(rule.Id, out folderName);
                string groupSuffix = string.IsNullOrWhiteSpace(folderName)
                    ? $" —— {groupName}"
                    : $" —— {groupName} —— {folderName}";
                if (!summaryLine.EndsWith(groupSuffix, StringComparison.Ordinal))
                    summaryLine += groupSuffix;
                entries.Add(summaryLine);
                count++;
            }
        }

        var output = new List<string>();
        foreach (var (group, categories) in groups)
        {
            if (output.Count > 0)
                output.Add(string.Empty);
            output.Add(group);
            bool firstCategory = true;
            foreach (string category in GroupResultFormatter.CategoryOrder.Concat(categories.Keys).Distinct(StringComparer.Ordinal)
                         .Where(categories.ContainsKey))
            {
                if (!firstCategory)
                    output.Add(string.Empty);
                output.Add($"【{category}】");
                output.AddRange(categories[category]);
                firstCategory = false;
            }
        }
        if (count == 0)
            output.Add("没有缺失或未分流的数据。");

        // Replace only after every source was read and the complete new summary was written.
        string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllLinesAsync(temporaryPath, output, new UTF8Encoding(true)).ConfigureAwait(false);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        return (outputPath, count);
    }

    private static Dictionary<string, string> ResolveDiagnosticFolders(
        string appDirectory, string? imageRoot, string resultName, string groupName, IReadOnlySet<string> missingFolderIds)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string diagnostic = Path.Combine(ResultFilePaths.TemporaryFilesDirectory(appDirectory), "诊断", $"OCR诊断_{resultName}.json");
        if (missingFolderIds.Count == 0 || imageRoot is null || !Directory.Exists(imageRoot) || !File.Exists(diagnostic))
            return result;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(diagnostic));
        if (!document.RootElement.TryGetProperty("images", out JsonElement images))
            return result;
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (JsonElement image in images.EnumerateArray())
        {
            if (!image.TryGetProperty("file", out JsonElement file) || !image.TryGetProperty("rules", out JsonElement ids))
                continue;
            foreach (JsonElement id in ids.EnumerateArray())
            {
                string? key = id.GetString();
                if (key is null || !missingFolderIds.Contains(key) || file.GetString() is not string filename)
                    continue;
                if (!names.TryGetValue(key, out var files)) names[key] = files = new(StringComparer.OrdinalIgnoreCase);
                files.Add(Path.GetFileName(filename));
            }
        }
        if (names.Count == 0) return result;
        var wanted = names.Values.SelectMany(files => files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string group in Directory.EnumerateDirectories(imageRoot).Where(path => RuleCatalog.IsGroupFolder(path, groupName)))
        foreach (string file in ImageFolderScanner.Scan(group).Where(path => wanted.Contains(Path.GetFileName(path))))
        {
            string name = Path.GetFileName(file);
            if (!folders.TryGetValue(name, out var children)) folders[name] = children = new(StringComparer.Ordinal);
            string relative = Path.GetRelativePath(group, file);
            children.Add(relative.Contains(Path.DirectorySeparatorChar)
                ? relative.Split(Path.DirectorySeparatorChar)[0] : "群目录根部");
        }
        foreach (var (id, files) in names)
        {
            string[] candidates = files.Where(folders.ContainsKey).SelectMany(file => folders[file]).Distinct(StringComparer.Ordinal).ToArray();
            if (candidates.Length == 1) result[id] = candidates[0];
        }
        return result;
    }
}
