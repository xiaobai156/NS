using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcrLineTool;

public static class RuleCatalog
{
    private static readonly Regex DateOrIssuePrefix = new(
        @"^(?:(?:第)?\d{1,6}期|\d{2,4}年\d{1,2}月\d{1,2}(?:日|号)|\d{1,4}[./_\-]\d{1,2}(?:[./_\-]\d{1,4})?|\d{1,2}月\d{1,2}(?:日|号))[\s._\-/、:：]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateOrIssueSuffix = new(
        @"[\s._\-/、:：]+(?:(?:第)?\d{1,6}期|\d{2,4}年\d{1,2}月\d{1,2}(?:日|号)|\d{1,4}[./_\-]\d{1,2}(?:[./_\-]\d{1,4})?|\d{1,2}月\d{1,2}(?:日|号))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string PathForFolder(string appDirectory, string selectedFolder)
    {
        string configurationDirectory = ResultFilePaths.ConfigurationDirectory(appDirectory);
        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedFolder));
        if (Directory.Exists(configurationDirectory))
        {
            string? matchingRuleFile = EnumerateRuleFiles(configurationDirectory)
                .Where(item => IsGroupFolder(selectedFolder, item.GroupName))
                .OrderByDescending(item => NormalizeGroupName(item.GroupName).Length)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Path)
                .FirstOrDefault();
            if (matchingRuleFile is not null)
                return matchingRuleFile;
        }

        return Path.Combine(configurationDirectory, folderName + ".json");
    }

    /// <summary>
    /// Returns true when the selected directory is exactly the expected group,
    /// with only a recognizable date/issue wrapper allowed around it.
    /// </summary>
    public static bool IsGroupFolder(string selectedFolder, string expectedGroup)
    {
        if (string.IsNullOrWhiteSpace(expectedGroup))
            return false;

        string actualBase = BaseGroupName(selectedFolder);
        if (actualBase.Length == 0 ||
            !char.IsLetterOrDigit(actualBase[0]) ||
            !char.IsLetterOrDigit(actualBase[^1]))
            return false;

        string actual = NormalizeGroupName(actualBase);
        string expected = NormalizeGroupName(expectedGroup);
        return actual.Length > 0 && expected.Length > 0 &&
               actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the exact configured group represented by a dated/issued folder.
    /// The returned value is one of <paramref name="expectedGroups"/>.
    /// </summary>
    public static string? MatchGroupFolder(string selectedFolder, IEnumerable<string> expectedGroups)
    {
        ArgumentNullException.ThrowIfNull(expectedGroups);
        return expectedGroups
            .Where(group => !string.IsNullOrWhiteSpace(group) && IsGroupFolder(selectedFolder, group))
            .OrderByDescending(group => NormalizeGroupName(group).Length)
            .ThenBy(group => group, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Removes only a leading/trailing date or issue marker from a folder name.
    /// Arbitrary suffixes such as “备份” and “临时” are intentionally retained.
    /// </summary>
    public static string BaseGroupName(string selectedFolder)
    {
        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedFolder));
        if (string.IsNullOrWhiteSpace(folderName))
            return string.Empty;

        string value = folderName.Trim();
        for (int pass = 0; pass < 3; pass++)
        {
            string stripped = DateOrIssuePrefix.Replace(value, string.Empty, 1);
            if (stripped == value)
                break;
            value = stripped.Trim();
        }

        for (int pass = 0; pass < 3; pass++)
        {
            string stripped = DateOrIssueSuffix.Replace(value, string.Empty, 1);
            if (stripped == value)
                break;
            value = stripped.Trim();
        }

        return value.Trim();
    }

    /// <summary>
    /// Resolves the canonical group name from OCR rule JSON in the app config.
    /// Falls back to the stripped folder name when no catalog is available.
    /// </summary>
    public static string GroupNameForFolder(string appDirectory, string selectedFolder)
    {
        string configurationDirectory = ResultFilePaths.ConfigurationDirectory(appDirectory);
        if (Directory.Exists(configurationDirectory))
        {
            RuleFileCandidate? matching = EnumerateRuleFiles(configurationDirectory)
                .Where(item => IsGroupFolder(selectedFolder, item.GroupName))
                .OrderByDescending(item => NormalizeGroupName(item.GroupName).Length)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (matching is not null)
                return matching.GroupName;
        }

        return BaseGroupName(selectedFolder);
    }

    /// <summary>
    /// Normalizes only structural separators and whitespace for an exact group comparison.
    /// </summary>
    public static string NormalizeGroupName(string value) =>
        string.Concat(value.Where(character =>
            !char.IsWhiteSpace(character) &&
            character is not '-' and not '_' and not '－' and not '＿'));

    private static IEnumerable<RuleFileCandidate> EnumerateRuleFiles(string configurationDirectory)
    {
        foreach (string path in Directory.EnumerateFiles(configurationDirectory, "*.json"))
        {
            string fileName = Path.GetFileName(path);
            if (fileName.EndsWith(".templates.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("分发规则.json", StringComparison.OrdinalIgnoreCase))
                continue;

            string stem = Path.GetFileNameWithoutExtension(path);
            string group = ReadGroupName(path) ?? stem;
            if (!string.IsNullOrWhiteSpace(group))
                yield return new RuleFileCandidate(path, group);
        }
    }

    private static string? ReadGroupName(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("group", out JsonElement group) &&
                group.ValueKind == JsonValueKind.String)
            {
                string? value = group.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            // A malformed file is still resolved by its filename so Load can report
            // the useful format error after the directory has been selected.
        }

        return null;
    }

    private sealed record RuleFileCandidate(string Path, string GroupName);

    public static IReadOnlyList<OcrRule> Load(string path)
    {
        string fileName = Path.GetFileName(path);
        if (!File.Exists(path))
            throw new OcrException($"未找到 {fileName}，请将对应规则文件放在“配置文件”子目录。");

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("rules", out JsonElement rules))
                throw new OcrException($"{fileName} 缺少 rules 配置。");

            var output = new List<OcrRule>();
            bool strictIssueBlock = document.RootElement.TryGetProperty("strictIssueBlock", out JsonElement strictElement)
                && strictElement.ValueKind == JsonValueKind.True;
            foreach (JsonElement item in rules.EnumerateArray())
            {
                string keyword = item.GetProperty("keyword").GetString() ?? "";
                string type = item.GetProperty("type").GetString() ?? "";
                string? label = item.TryGetProperty("label", out JsonElement labelElement) ? labelElement.GetString() : null;
                string? section = item.TryGetProperty("section", out JsonElement sectionElement) ? sectionElement.GetString() : null;
                string? requiredKeyword = item.TryGetProperty("required_keyword", out JsonElement requiredElement) ? requiredElement.GetString() : null;
                string? folder = item.TryGetProperty("folder", out JsonElement folderElement) ? folderElement.GetString() : null;
                bool ignoreIssue = item.TryGetProperty("ignoreIssue", out JsonElement ignoreIssueElement)
                    && ignoreIssueElement.ValueKind == JsonValueKind.True;
                bool allowNearbyValue = item.TryGetProperty("allowNearbyValue", out JsonElement nearbyElement)
                    && nearbyElement.ValueKind == JsonValueKind.True;
                bool allowValueWithoutKeyword = item.TryGetProperty("allowValueWithoutKeyword", out JsonElement withoutKeywordElement)
                    && withoutKeywordElement.ValueKind == JsonValueKind.True;
                bool singleValuePerIssue = item.TryGetProperty("singleValuePerIssue", out JsonElement singleValueElement)
                    && singleValueElement.ValueKind == JsonValueKind.True;
                bool itemStrictIssueBlock = item.TryGetProperty("strictIssueBlock", out JsonElement itemStrictElement)
                    ? itemStrictElement.ValueKind == JsonValueKind.True
                    : strictIssueBlock;
                if (keyword.Length > 0 && type.Length > 0)
                    output.Add(new OcrRule(
                        keyword,
                        type,
                        label,
                        section,
                        requiredKeyword,
                        folder,
                        ignoreIssue,
                        allowNearbyValue,
                        allowValueWithoutKeyword,
                        itemStrictIssueBlock,
                        singleValuePerIssue));
            }
            if (output.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != output.Count)
                throw new OcrException($"{fileName} 中存在重复的输出名称。");
            return output;
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            throw new OcrException($"{fileName} 格式无效。");
        }
    }
}
