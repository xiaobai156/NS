using System.Text;
using System.Text.Json;

namespace OcrLineTool;

/// <summary>
/// 抓取设置：记录哪些群整体不抓取/不识别，以及群内哪些资料条目不识别。
/// 默认全开——文件不存在或没有记录时，所有群、所有条目都照常工作；
/// 之后新增的群、新增的规则也默认开启，不需要跟着改这个文件。
/// </summary>
internal sealed class CaptureSettings
{
    private const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    // 关掉的群（整群不抓取、不识别）。
    public List<string> DisabledGroups { get; set; } = new();
    // 群名 → 关掉的条目 Id。Id 取 OcrRule.Id（有 label 用 label，否则用 keyword），
    // 因为同一个群里存在 keyword 完全相同的多条规则，用 keyword 无法区分。
    public Dictionary<string, List<string>> DisabledRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // 设置跟规则文件一起放在“配置文件”子目录：升级只换程序目录，不会带走它；
    // 「清除结果」只清 临时文件/重要结果，也不碰这个目录。
    internal static string PathFor(string appDirectory) =>
        Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), "抓取设置.json");

    internal static CaptureSettings Load(string appDirectory)
    {
        try
        {
            string path = PathFor(appDirectory);
            if (!File.Exists(path))
                return new();

            CaptureSettings? settings = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path));
            if (settings is null)
                return new();
            settings.Normalize();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or NotSupportedException)
        {
            // 设置文件坏了按“全开”处理：抓取和识别不能因为一个坏文件停摆。
            return new();
        }
    }

    internal void Save(string appDirectory)
    {
        Normalize();
        string path = PathFor(appDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(
            path,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(true));
    }

    internal bool IsGroupEnabled(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return true;
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        return !DisabledGroups.Any(group => group.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    internal void SetGroupEnabled(string groupName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        DisabledGroups.RemoveAll(group => group.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
            DisabledGroups.Add(normalized);
    }

    internal bool IsRuleEnabled(string? groupName, OcrRule rule)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return true;
        if (!IsGroupEnabled(groupName))
            return false;
        return IsRuleEnabledIgnoringGroup(groupName, rule);
    }

    /// <summary>
    /// 只看条目自己的开关，不看群开关。界面在群关掉时仍要显示各条目上次的勾选，
    /// 分类框的三态也要靠它算；否则群关时点一次分类框会把条目全部改写。
    /// </summary>
    internal bool IsRuleEnabledIgnoringGroup(string? groupName, OcrRule rule)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return true;
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        return !DisabledRules.TryGetValue(normalized, out List<string>? disabled)
            || !disabled.Contains(rule.Id, StringComparer.Ordinal);
    }

    internal void SetRuleEnabled(string groupName, OcrRule rule, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        if (!DisabledRules.TryGetValue(normalized, out List<string>? disabled))
        {
            disabled = new List<string>();
            DisabledRules[normalized] = disabled;
        }
        disabled.RemoveAll(id => id.Equals(rule.Id, StringComparison.Ordinal));
        if (!enabled)
            disabled.Add(rule.Id);
        if (disabled.Count == 0)
            DisabledRules.Remove(normalized);
    }

    /// <summary>按设置过滤规则；群被关掉时返回空列表（当这个群没有要识别的资料）。</summary>
    internal IReadOnlyList<OcrRule> EnabledRules(string? groupName, IReadOnlyList<OcrRule> rules)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return rules;
        if (!IsGroupEnabled(groupName))
            return Array.Empty<OcrRule>();
        string normalized = RuleCatalog.NormalizeGroupName(groupName);
        if (!DisabledRules.TryGetValue(normalized, out List<string>? disabled) || disabled.Count == 0)
            return rules;
        return rules.Where(rule => !disabled.Contains(rule.Id, StringComparer.Ordinal)).ToArray();
    }

    private void Normalize()
    {
        DisabledGroups ??= new List<string>();
        DisabledGroups = DisabledGroups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(RuleCatalog.NormalizeGroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DisabledRules ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var merged = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string rawGroup, List<string>? ids) in DisabledRules)
        {
            if (string.IsNullOrWhiteSpace(rawGroup) || ids is null)
                continue;
            string group = RuleCatalog.NormalizeGroupName(rawGroup);
            if (!merged.TryGetValue(group, out List<string>? target))
            {
                target = new List<string>();
                merged[group] = target;
            }
            foreach (string id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
                if (!target.Contains(id, StringComparer.Ordinal))
                    target.Add(id);
        }
        DisabledRules = merged;
    }
}
