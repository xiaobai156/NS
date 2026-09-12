using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OcrLineTool;

public sealed record DistributionLineIssue(string Line, string Reason);

public sealed record DistributionResult(
    IReadOnlySet<string> DistributedLines,
    IReadOnlyList<string> Errors)
{
    public IReadOnlyList<DistributionLineIssue> UndistributedLines { get; init; } = [];
}

public static class ResultDistributor
{
    public const string TargetDirectory = @"C:\Users\Administrator\Desktop\每天工具\爬虫合集\大围杀号生肖数据统一归纳";

    public static async Task<DistributionResult> DistributeAllAsync(
        string selectedDirectory,
        int issue,
        IEnumerable<string> outputLines,
        string? targetDirectory = null,
        string? configDirectory = null)
    {
        configDirectory ??= ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        string[] configPaths = Directory.GetFiles(configDirectory, "*分发规则.json");
        if (configPaths.Length == 0)
            throw new OcrException($"未找到数据分发规则：{configDirectory}");

        string[] lines = outputLines.ToArray();
        var distributed = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        var loadedConfigs = new List<LoadedConfig>();
        foreach (string configPath in configPaths.Order(StringComparer.Ordinal))
        {
            try
            {
                LoadedConfig? loaded = await LoadConfigAsync(selectedDirectory, configPath);
                if (loaded is null)
                    continue;
                loadedConfigs.Add(loaded);
                distributed.UnionWith(await ApplyConfigAsync(issue, lines, targetDirectory, loaded));
            }
            catch (OcrException exception)
            {
                errors.Add(exception.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                errors.Add($"分流配置或目标不可用：{Path.GetFileName(configPath)}。");
            }
        }
        return new DistributionResult(distributed, errors)
        {
            UndistributedLines = DescribeUndistributedLines(lines, distributed, loadedConfigs)
        };
    }

    public static string[] MarkDistributedLines(
        IEnumerable<string> outputLines,
        IReadOnlySet<string> distributedLines) =>
        outputLines
            .Select(line => distributedLines.Contains(line) ? line + "（已分流）" : line)
            .ToArray();

    public static async Task<IReadOnlySet<string>> DistributeAsync(
        string selectedDirectory,
        int issue,
        IEnumerable<string> outputLines,
        string? targetDirectory = null,
        string? configPath = null)
    {
        configPath ??= Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory),
            "杀数字分发规则.json");
        LoadedConfig? loaded = await LoadConfigAsync(selectedDirectory, configPath);
        return loaded is null
            ? EmptyResult
            : await ApplyConfigAsync(issue, outputLines.ToArray(), targetDirectory, loaded);
    }

    private static async Task<LoadedConfig?> LoadConfigAsync(string selectedDirectory, string configPath)
    {
        if (!File.Exists(configPath))
            throw new OcrException($"未找到数据分发规则：{configPath}");

        await using FileStream configStream = File.OpenRead(configPath);
        DistributionConfig? config = await JsonSerializer.DeserializeAsync<DistributionConfig>(configStream);
        if (config?.Sources is null || string.IsNullOrWhiteSpace(config.TargetFile))
            throw new OcrException($"数据分发规则格式无效：{configPath}");

        string? sourceGroup = RuleCatalog.MatchGroupFolder(
            selectedDirectory,
            config.Sources.Select(item => item.SourceGroup));
        SourceRule? rule = sourceGroup is null
            ? null
            : config.Sources.FirstOrDefault(item =>
                item.SourceGroup.Equals(sourceGroup, StringComparison.Ordinal));
        if (rule is null)
            return null;

        var labels = new HashSet<string>(rule.Labels, StringComparer.Ordinal);
        IReadOnlyDictionary<string, OcrRule> rulesByLabel = RuleCatalog.Load(
                RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedDirectory))
            .Where(item => labels.Contains(item.OutputLabel))
            .ToDictionary(item => item.OutputLabel, StringComparer.Ordinal);
        return new LoadedConfig(config, rule, labels, rule.NumberCounts, rulesByLabel);
    }

    private static async Task<IReadOnlySet<string>> ApplyConfigAsync(
        int issue,
        string[] outputLines,
        string? targetDirectory,
        LoadedConfig loaded)
    {
        string[] normalizedLines = outputLines
            .Select(line => line.EndsWith("（已分流）", StringComparison.Ordinal) ? line[..^5] : line)
            .ToArray();
        string[] configuredLines = normalizedLines
            .Where(line => IsConfiguredLine(line, loaded.Labels, loaded.NumberCounts, loaded.RulesByLabel))
            .ToArray();
        HashSet<string> revokedLabels = normalizedLines
            .Where(line => line.StartsWith("缺失（同一期结果冲突", StringComparison.Ordinal))
            .Select(line => loaded.Labels.OrderByDescending(label => label.Length)
                .FirstOrDefault(label => line.EndsWith(" " + label, StringComparison.Ordinal)))
            .Where(label => label is not null)
            .Select(label => label!)
            .ToHashSet(StringComparer.Ordinal);
        if (configuredLines.Length == 0 && revokedLabels.Count == 0)
            return EmptyResult;

        targetDirectory ??= loaded.Config.TargetDirectory ?? TargetDirectory;
        if (issue <= 0 || !loaded.Config.TargetFile.Contains("{issue}", StringComparison.Ordinal))
            throw new OcrException("分发目标必须包含动态 {issue} 期号。");
        string targetFile = loaded.Config.TargetFile.Replace("{issue}", issue.ToString(), StringComparison.Ordinal);
        if (Path.GetFileName(targetFile) != targetFile || Path.IsPathRooted(targetFile))
            throw new OcrException("分发 targetFile 必须是目标目录内的文件名。");
        string targetPath = Path.Combine(targetDirectory, targetFile);
        if (!File.Exists(targetPath))
            throw new OcrException($"未找到分发目标文件：{targetPath}");

        string? marker = loaded.Config.Placement?.Mode == "beforeLine"
            ? loaded.Config.Placement.Marker.Replace("{issue}", issue.ToString(), StringComparison.Ordinal)
            : null;
        return await OwnedResultWriter.ApplyAsync(targetPath, loaded.Source.SourceGroup, configuredLines,
            revokedLabels, marker, loaded.Config.Placement?.BlankLineBeforeMarker == true);
    }

    private static IReadOnlyList<DistributionLineIssue> DescribeUndistributedLines(
        string[] outputLines,
        IReadOnlySet<string> distributedLines,
        IReadOnlyList<LoadedConfig> configs)
    {
        var issues = new List<DistributionLineIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in outputLines)
        {
            string line = rawLine.EndsWith("（已分流）", StringComparison.Ordinal) ? rawLine[..^5] : rawLine;
            if (string.IsNullOrWhiteSpace(line)
                || distributedLines.Contains(line)
                || !seen.Add(line)
                || line.StartsWith("缺失", StringComparison.Ordinal)
                || line.StartsWith("【", StringComparison.Ordinal) && line.EndsWith("】", StringComparison.Ordinal))
                continue;

            int separator = line.LastIndexOf(' ');
            if (separator <= 0)
            {
                issues.Add(new DistributionLineIssue(line, "无法解析为“值 名称”格式"));
                continue;
            }

            string label = line[(separator + 1)..];
            bool configured = false;
            bool accepted = false;
            foreach (LoadedConfig config in configs)
            {
                if (!config.Labels.Contains(label))
                    continue;
                configured = true;
                if (IsConfiguredLine(line, config.Labels, config.NumberCounts, config.RulesByLabel))
                    accepted = true;
            }

            issues.Add(accepted
                ? new DistributionLineIssue(line, "未写入分发目标（目标忙、冲突或不可写）")
                : configured
                    ? new DistributionLineIssue(line, "值未通过校验（格式或数量不符）")
                    : new DistributionLineIssue(line, "未找到对应的分发规则"));
        }
        return issues;
    }

    private static readonly IReadOnlySet<string> EmptyResult =
        new HashSet<string>(StringComparer.Ordinal);

    private static void InsertBeforeMarker(
        string targetPath,
        string[] existingLines,
        string[] linesToInsert,
        Placement placement)
    {
        int markerIndex = Array.FindIndex(existingLines, line =>
            line.Equals(placement.Marker, StringComparison.Ordinal));
        if (markerIndex < 0)
            markerIndex = Array.FindIndex(existingLines, IsRankingHeader);
        if (markerIndex < 0)
        {
            // Some production category files are already flat data tables and
            // do not contain the legacy ranking marker. Append safely instead
            // of reporting a distribution failure.
            string existingText = File.ReadAllText(targetPath);
            string leadingNewLine = existingText.Length > 0 && !existingText.EndsWith('\n')
                ? Environment.NewLine
                : string.Empty;
            File.AppendAllText(
                targetPath,
                leadingNewLine + string.Join(Environment.NewLine, linesToInsert) + Environment.NewLine,
                new UTF8Encoding(false));
            return;
        }

        var updated = existingLines.ToList();
        while (markerIndex > 0 && string.IsNullOrWhiteSpace(updated[markerIndex - 1]))
        {
            updated.RemoveAt(markerIndex - 1);
            markerIndex--;
        }

        updated.InsertRange(markerIndex, linesToInsert);
        if (placement.BlankLineBeforeMarker)
            updated.Insert(markerIndex + linesToInsert.Length, string.Empty);

        byte[] bytes = File.ReadAllBytes(targetPath);
        bool hasUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        AtomicFile.WriteAllLines(targetPath, updated, new UTF8Encoding(hasUtf8Bom));
    }

    private static bool IsRankingHeader(string line)
    {
        string normalized = string.Concat(line.Where(character => !char.IsWhiteSpace(character)));
        return normalized.Contains("内容", StringComparison.Ordinal)
            && normalized.Contains("次数", StringComparison.Ordinal)
            && normalized.Contains("排名", StringComparison.Ordinal);
    }

    private static bool IsConfiguredLine(
        string line,
        HashSet<string> labels,
        IReadOnlyDictionary<string, int>? numberCounts,
        IReadOnlyDictionary<string, OcrRule> rulesByLabel)
    {
        int separator = line.LastIndexOf(' ');
        if (separator < 0 || line.StartsWith("缺失", StringComparison.Ordinal))
            return false;

        string label = line[(separator + 1)..];
        if (!labels.Contains(label))
            return false;
        string value = line[..separator].Trim();

        if (numberCounts?.TryGetValue(label, out int expectedCount) == true)
            return IsSafeNumberList(value, expectedCount);
        if (rulesByLabel.TryGetValue(label, out OcrRule? ocrRule))
            return RuleEngine.IsFormattedOutputValueValid(ocrRule, value);

        // Custom labels have no OCR rule to prove a semantic type. Preserve the
        // generic distributor contract only for an unambiguous numeric list;
        // arbitrary text/生肖/尾数 cannot bypass business validation this way.
        return IsSafeNumberList(value, expectedCount: null);
    }

    private static bool IsSafeNumberList(string value, int? expectedCount)
    {
        string[] numbers = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length == 0 || expectedCount is int count && numbers.Length != count)
            return false;
        return numbers.Distinct(StringComparer.Ordinal).Count() == numbers.Length
            && numbers.All(number => number.Length == 2
                && number.All(character => character is >= '0' and <= '9')
                && int.TryParse(number, out int parsed) && parsed is >= 1 and <= 49);
    }

    private sealed record LoadedConfig(
        DistributionConfig Config,
        SourceRule Source,
        HashSet<string> Labels,
        IReadOnlyDictionary<string, int>? NumberCounts,
        IReadOnlyDictionary<string, OcrRule> RulesByLabel);

    private sealed record DistributionConfig(
        [property: JsonPropertyName("targetFile")] string TargetFile,
        [property: JsonPropertyName("sources")] SourceRule[] Sources,
        [property: JsonPropertyName("targetDirectory")] string? TargetDirectory = null,
        [property: JsonPropertyName("placement")] Placement? Placement = null);

    private sealed record SourceRule(
        [property: JsonPropertyName("sourceGroup")] string SourceGroup,
        [property: JsonPropertyName("labels")] string[] Labels,
        [property: JsonPropertyName("numberCounts")] Dictionary<string, int>? NumberCounts = null);

    private sealed record Placement(
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("marker")] string Marker,
        [property: JsonPropertyName("blankLineBeforeMarker")] bool BlankLineBeforeMarker = false);
}
