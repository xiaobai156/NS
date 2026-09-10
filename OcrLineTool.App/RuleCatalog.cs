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
    /// Returns true when the file sits inside the expected group directory,
    /// allowing a dated/issued wrapper on any ancestor directory name.
    /// </summary>
    public static bool PathBelongsToGroup(string filePath, string expectedGroup)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(expectedGroup))
            return false;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (IsGroupFolder(directory, expectedGroup))
                return true;
            directory = Path.GetDirectoryName(directory);
        }
        return false;
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
            bool strictIssueBlock = ReadOptionalBoolean(
                document.RootElement, "strictIssueBlock", false, fileName);
            foreach (JsonElement item in rules.EnumerateArray())
            {
                string keyword = item.GetProperty("keyword").GetString() ?? "";
                string type = item.GetProperty("type").GetString() ?? "";
                string? label = item.TryGetProperty("label", out JsonElement labelElement) ? labelElement.GetString() : null;
                string? section = item.TryGetProperty("section", out JsonElement sectionElement) ? sectionElement.GetString() : null;
                string? requiredKeyword = item.TryGetProperty("required_keyword", out JsonElement requiredElement) ? requiredElement.GetString() : null;
                IReadOnlyList<string>? requiredKeywordsAny = null;
                if (item.TryGetProperty("required_keywords_any", out JsonElement requiredAnyElement))
                {
                    if (requiredAnyElement.ValueKind != JsonValueKind.Array)
                        throw new OcrException($"{fileName} 的 required_keywords_any 必须是数组。");
                    requiredKeywordsAny = requiredAnyElement.EnumerateArray()
                        .Select(value => value.GetString() ?? string.Empty)
                        .Where(value => value.Length > 0)
                        .ToArray();
                    if (requiredKeywordsAny.Count == 0)
                        requiredKeywordsAny = null;
                }
                string? folder = item.TryGetProperty("folder", out JsonElement folderElement) ? folderElement.GetString() : null;
                bool ignoreIssue = ReadOptionalBoolean(item, "ignoreIssue", false, fileName);
                bool allowNearbyValue = ReadOptionalBoolean(item, "allowNearbyValue", false, fileName);
                bool allowValueWithoutKeyword = ReadOptionalBoolean(item, "allowValueWithoutKeyword", false, fileName);
                bool singleValuePerIssue = ReadOptionalBoolean(item, "singleValuePerIssue", false, fileName);
                bool itemStrictIssueBlock = ReadOptionalBoolean(item, "strictIssueBlock", strictIssueBlock, fileName);
                bool allowFolderIdentity = ReadOptionalBoolean(item, "allow_folder_identity", false, fileName);
                bool skipConflictingRows = ReadOptionalBoolean(item, "skip_conflicting_rows", false, fileName);
                bool allowIssueLessSummary = ReadOptionalBoolean(item, "allow_issue_less_summary", false, fileName);
                bool stopAtPlus = ReadOptionalBoolean(item, "stop_at_plus", false, fileName);
                bool tenZodiacCombo = ReadOptionalBoolean(item, "ten_zodiac_combo", false, fileName);
                bool primaryOnly = ReadOptionalBoolean(item, "primary_only", false, fileName);
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
                        singleValuePerIssue,
                        null,
                        requiredKeywordsAny,
                        allowFolderIdentity,
                        skipConflictingRows,
                        allowIssueLessSummary,
                        stopAtPlus,
                        tenZodiacCombo,
                        primaryOnly));
            }
            if (output.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != output.Count)
                throw new OcrException($"{fileName} 中存在重复的输出名称。");

            OcrRule[] enriched = output.Select(rule =>
            {
                if (string.IsNullOrWhiteSpace(rule.Folder))
                    return rule;
                HashSet<string> own = new[]
                {
                    rule.Keyword, rule.Label ?? string.Empty, rule.Section ?? string.Empty,
                    rule.RequiredKeyword ?? string.Empty
                }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.Ordinal);
                string[] peers = output
                    .Where(other => other.Id != rule.Id
                        && string.Equals(other.Folder, rule.Folder, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(other => new[]
                    {
                        other.Keyword, other.Label ?? string.Empty, other.Section ?? string.Empty,
                        other.RequiredKeyword ?? string.Empty
                    })
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !own.Contains(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return rule with { PeerKeywords = peers };
            }).ToArray();
            return enriched;
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

    private static bool ReadOptionalBoolean(
        JsonElement element, string propertyName, bool fallback, string fileName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new OcrException($"{fileName} 的 {propertyName} 必须是 true 或 false。")
        };
    }
}
