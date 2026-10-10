using System.Text.RegularExpressions;

namespace OcrLineTool;

// These reviewed author cards share a folder with unrelated cards. Identity
// selects the card; the complete issue field selects the value, independently
// of a cropped heading or the position of its vertical watermark.
internal static class YanranAuthorCards
{
    private const string Zodiacs = "鼠牛虎兔龙蛇马羊猴鸡狗猪";

    internal static bool Supports(OcrRule rule) => rule.Id is
        "君军两肖" or "君军两尾" or "君军合" or "大哥6688" or "紫燕儿杀一肖";

    internal static bool HasIdentity(string[] lines, OcrRule rule)
    {
        string text = RuleEngine.Normalize(string.Concat(lines));
        // These foreign cards occur in the same reviewed source folders.
        // Check again at extraction, because cached/cloud evidence can bypass
        // small candidate selection.
        if (new[] { "玩不转", "缘来如此", "老兵惜缘" }.Any(text.Contains))
            return false;
        string name = RuleEngine.Normalize(rule.Keyword);
        if (text.Contains(name, StringComparison.Ordinal))
            return true;
        // Only isolated fragments of this name may form a split watermark.
        // Data rows and arbitrary words never participate in name reconstruction.
        string fragments = string.Concat(lines.Select(RuleEngine.Normalize)
            .Where(line => line.Length > 0 && line.Length < name.Length
                && name.Contains(line, StringComparison.Ordinal)));
        if (fragments.Contains(name, StringComparison.Ordinal))
            return true;
        // Tencent may merge the vertical 紫燕 watermark into a historical
        // row and drop 儿. Only this reviewed name and its explicit folder
        // opt-in allow that short identity; values still require complete rows.
        if (rule.Id == "紫燕儿杀一肖" && rule.AllowFolderIdentity
            && lines.Any(line => RuleEngine.Normalize(line).Contains("紫燕", StringComparison.Ordinal)))
            return HasRows(lines, rule);
        return rule.Id == "大哥6688" && rule.AllowFolderIdentity
            && lines.Any(line => RuleEngine.Normalize(line) == "哥")
            && HasRows(lines, rule);
    }

    internal static bool HasRows(string[] lines, OcrRule rule) =>
        RuleEngine.FindIssues(lines).Distinct().Any(issue => Extract(lines, issue, rule).Status != RuleExtractionStatus.Missing);

    internal static RuleExtractionResult Extract(string[] lines, int issue, OcrRule rule, bool allowWrappedField = true)
    {
        lines = lines.SelectMany(line => line.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .SelectMany(line => Regex.Split(Compact(line), @"(?<!\d)(?=\d{1,6}期)"))
            .Where(line => line.Length > 0).ToArray();
        var values = new HashSet<string>(StringComparer.Ordinal);
        bool invalid = false;
        for (int index = 0; index < lines.Length; index++)
        {
            Match head = Regex.Match(Compact(lines[index]), @"^(?<issue>\d{1,6})期[:：]?(?<field>.*)$");
            if (!head.Success || !int.TryParse(head.Groups["issue"].Value, out int found) || found != issue)
                continue;
            string field = head.Groups["field"].Value;
            bool relevant = IsRelevantField(field, rule);
            bool parsed = false;
            for (int next = index; next <= index + (allowWrappedField ? 2 : 0) && next < lines.Length; next++)
            {
                if (next > index)
                {
                    if (OcrLayoutMarkers.IsBoundary(lines[next]) || RuleEngine.FindIssues([lines[next]]).Any())
                        break;
                    field += Compact(lines[next]);
                }
                string? value = ParseField(field, rule);
                if (value is null)
                    continue;
                values.Add(value);
                parsed = true;
                break;
            }
            invalid |= relevant && !parsed;
        }
        return values.Count > 1 ? RuleExtractionResult.Conflict
            : values.Count == 1 && !invalid ? RuleExtractionResult.Success(values.Single()) : RuleExtractionResult.Missing;
    }

    private static bool IsRelevantField(string field, OcrRule rule)
    {
        string name = Compact(rule.Keyword);
        if (field.StartsWith(name, StringComparison.Ordinal))
            field = field[name.Length..];
        string prefix = rule.Id switch
        {
            "君军两肖" => @"^杀(?:(?:一|二|两|1|2)肖|[【\[（(]?[鼠牛虎兔龙蛇马羊猴鸡狗猪])",
            "君军两尾" => @"^杀(?:(?:一|二|两|1|2)尾|[0-9].*尾)",
            "君军合" => @"^杀(?:(?:一|1)合|[0-9].*合)",
            "大哥6688" => "^杀[肖消]",
            _ => "^新[澳奥]杀"
        };
        return Regex.IsMatch(field, prefix);
    }

    private static string? ParseField(string field, OcrRule rule)
    {
        if (field.StartsWith(Compact(rule.Keyword), StringComparison.Ordinal))
            field = field[Compact(rule.Keyword).Length..];
        field = Regex.Split(field, "[开開]")[0];
        field = Regex.Replace(field, @"[√✓✔×✗✘Xx?？]+$", "");
        string single = $@"[【\[（(]?(?<value>[{Zodiacs}])[】\]）)]?";
        string pattern = rule.Id switch
        {
            "君军两肖" => $@"杀(?:(?:一|二|两|1|2)肖)?[【\[（(]?(?<first>[{Zodiacs}])[+＋、,，.．]*(?<second>[{Zodiacs}])[】\]）)]?",
            "君军两尾" => @"杀(?:(?:一|二|两|1|2)尾)?(?<first>[0-9])尾?[+＋、,，.．](?<second>[0-9])尾",
            "君军合" => @"杀(?:(?:一|1)合)?(?<value>0[1-9]|1[0-3])合",
            "大哥6688" => "杀[肖消]" + single,
            "紫燕儿杀一肖" => "新[澳奥]杀" + single,
            _ => "(?!)"
        };
        Match match = Regex.Match(field, "^" + pattern + "$");
        if (!match.Success)
            return null;
        if (match.Groups["first"].Success)
        {
            string first = match.Groups["first"].Value, second = match.Groups["second"].Value;
            if (first == second)
                return null;
            return rule.Type == "尾数组合" ? $"{first}尾+{second}尾" : first + second;
        }
        return match.Groups["value"].Value + (rule.Type == "合" ? "合" : "");
    }

    private static string Compact(string text) => Regex.Replace(text, @"\s+", "")
        .Replace('馬', '马').Replace('龍', '龙').Replace('雞', '鸡').Replace('豬', '猪')
        .Replace('殺', '杀').Replace('宵', '肖').Replace('開', '开');
}
