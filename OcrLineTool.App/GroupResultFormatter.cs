namespace OcrLineTool;

public static class GroupResultFormatter
{
    // Older releases accidentally appended the selected group directory to data lines.
    public static string RemoveLegacySourceSuffix(string line)
    {
        int index = line.IndexOf("—— 子文件夹=", StringComparison.Ordinal);
        return index < 0 ? line : line[..index].TrimEnd();
    }
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
            OcrRule? rule = ruleList.FirstOrDefault(candidate =>
                line.EndsWith(candidate.OutputLabel, StringComparison.Ordinal) ||
                line.EndsWith(candidate.OutputLabel + "（已分流）", StringComparison.Ordinal));
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
