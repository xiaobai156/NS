namespace OcrLineTool;

public static class GroupResultFormatter
{
    // Older releases accidentally appended the selected group directory to data lines.
    public static string RemoveLegacySourceSuffix(string line)
    {
        int index = line.IndexOf("—— 子文件夹=", StringComparison.Ordinal);
        return index < 0 ? line : line[..index].TrimEnd();
    }

    // 群结果 TXT 里"已经有值"的行：可能是你手工填的，也可能是程序写好后你已确认。
    // 这类条目光看 TXT 就已经有结论，复抓不再处理、统计也不再把它们算作缺失。
    // 只有仍写着"缺失"的行才由程序负责。
    public static Dictionary<string, string> ReadConcludedValueLines(
        IEnumerable<string> lines, IEnumerable<OcrRule> rules)
    {
        OcrRule[] ruleList = rules.OrderByDescending(rule => rule.OutputLabel.Length).ToArray();
        var concluded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rawLine in lines)
        {
            string line = RemoveLegacySourceSuffix(rawLine).Trim();
            if (line.Length == 0
                || line.StartsWith("来源：", StringComparison.Ordinal)
                || line.StartsWith("缺失", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("【", StringComparison.Ordinal) && line.EndsWith("】", StringComparison.Ordinal))
                continue;
            OcrRule? rule = RuleFor(line, ruleList);
            if (rule is not null)
                concluded[rule.Id] = line;
        }
        return concluded;
    }

    // 复抓重写群结果时把这些行原样放回去：你手工修改过的结果不能被本轮识别覆盖。
    public static string[] ReapplyConcludedValueLines(
        IEnumerable<string> lines, IEnumerable<OcrRule> rules, IReadOnlyDictionary<string, string> concludedLines)
    {
        string[] source = lines.ToArray();
        if (concludedLines.Count == 0)
            return source;
        OcrRule[] ruleList = rules.OrderByDescending(rule => rule.OutputLabel.Length).ToArray();
        return source.Select(line =>
        {
            OcrRule? rule = RuleFor(line, ruleList);
            return rule is not null && concludedLines.TryGetValue(rule.Id, out string? concluded)
                ? concluded
                : line;
        }).ToArray();
    }

    // 复抓范围：程序还没有值的规则里，排除 TXT 里已经有值的那些（= 你已处理）。
    public static OcrRule[] MissingRetryRules(
        IEnumerable<OcrRule> rules, IReadOnlyCollection<string> valueRuleIds,
        IReadOnlyDictionary<string, string> concludedLines)
    {
        return rules
            .Where(rule => !valueRuleIds.Contains(rule.Id) && !concludedLines.ContainsKey(rule.Id))
            .ToArray();
    }

    private static OcrRule? RuleFor(string line, OcrRule[] ruleList) =>
        ruleList.FirstOrDefault(candidate =>
            line.EndsWith(candidate.OutputLabel, StringComparison.Ordinal) ||
            line.EndsWith(candidate.OutputLabel + "（已分流）", StringComparison.Ordinal));

    internal static readonly string[] CategoryOrder =
        ["头", "尾", "波", "半波", "半头", "一肖", "二肖", "九肖", "五行", "5个数字", "5个以上数字", "30个以上数字", "合", "段", "其他"];
    private static readonly HashSet<string> Headers =
        new(CategoryOrder.Select(category => $"【{category}】"), StringComparer.Ordinal);

    public static string[] Format(IEnumerable<OcrRule> rules, IEnumerable<string> lines)
    {
        OcrRule[] ruleList = rules
            .OrderByDescending(rule => rule.OutputLabel.Length)
            .ToArray();
        Dictionary<string, List<string>> groups = CategoryOrder
            .ToDictionary(category => category, _ => new List<string>(), StringComparer.Ordinal);

        foreach (string line in lines.Where(line =>
                     !string.IsNullOrWhiteSpace(line) && !Headers.Contains(line) &&
                     !(line.StartsWith("【", StringComparison.Ordinal) && line.EndsWith("】", StringComparison.Ordinal))))
        {
            OcrRule? rule = RuleFor(line, ruleList);
            groups[rule is null ? "其他" : CategoryFor(rule.Type)].Add(line);
        }

        var output = new List<string>();
        foreach (string category in CategoryOrder.Where(category => groups[category].Count > 0))
        {
            if (output.Count > 0)
                output.Add(string.Empty);
            output.Add($"【{category}】");
            output.AddRange(groups[category]);
        }
        return output.ToArray();
    }

    private static string CategoryFor(string type)
    {
        if (type is "头" or "头数组合" or "缺头")
            return "头";
        if (type is "尾" or "尾数组合" or "缺尾")
            return "尾";
        if (type == "波")
            return "波";
        if (type == "色单双")
            return "半波";
        if (type == "半头")
            return "半头";
        if (type is "生肖" or "单生肖" or "统计生肖")
            return "一肖";
        if (type == "生肖组合")
            return "二肖";
        if (type == "缺两肖")
            return "二肖";
        if (type == "九肖")
            return "九肖";
        // 方位型资料（藏宝九肖）输出三个方向字，不属于任何生肖数量档。
        if (type is "方位" or "琴棋书画")
            return "其他";
        if (type is "五行" or "单五行")
            return "五行";
        if (type == "合")
            return "合";
        if (type == "段")
            return "段";
        if (type.StartsWith("号码:", StringComparison.Ordinal) &&
            int.TryParse(type.AsSpan("号码:".Length), out int count))
        {
            return count >= 30 ? "30个以上数字" : count > 5 ? "5个以上数字" : "5个数字";
        }
        return "5个数字";
    }
}
