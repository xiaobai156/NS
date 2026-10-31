namespace OcrLineTool.Tests;

/// <summary>
/// 每张卡的专属解析由配置里的 extractor 指定。这张表是代码侧认识的取值，
/// 少写一个或写错一个都会让那张卡静默变成「缺失」，所以这里把它钉死。
/// </summary>
public sealed class ExtractorContractTests
{
    // 规则 Id → 必须声明的 extractor。加新卡时先在这里登记，再去改配置。
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["黑客九肖"] = "nine_zodiacs_across_wrapped_rows",
        ["嫣然电报十码必杀"] = "ten_numbers_across_issue_cell",
        ["锦囊杀十码"] = "ten_numbers_straddling_issue",
        ["完美两肖"] = "perfect_two_zodiac",
        ["欧阳半波"] = "ouyang_half_wave",
        ["紫燕儿杀一肖"] = "ziyaner_kill_zodiac",
        ["君军两肖"] = "yanran_author_card",
        ["君军两尾"] = "yanran_author_card",
        ["君军合"] = "yanran_author_card",
        ["大哥6688"] = "yanran_author_card",
        ["黑客杀尾"] = "heike_nine_tails",
        ["锦囊杀尾"] = "jinnang_nine_tails",
        ["黑客杀两肖"] = "kill_two_zodiacs",
        ["公式杀一肖"] = "labelled_repeated",
        ["公式杀尾"] = "labelled_repeated",
        ["公式杀行"] = "labelled_repeated",
        ["公式杀头"] = "labelled_single_head",
        ["公式半波"] = "labelled_single_half_wave",
        ["黑客半波"] = "labelled_half_waves",
        ["红牛四行"] = "four_elements_bullseye",
        ["黑客四行"] = "four_elements_bracket",
        ["陈思思"] = "author_forbidden_zodiac",
    };

    // 代码里 switch 认得的全部取值。加分支时同步这里。
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "nine_zodiacs_across_wrapped_rows",
        "ten_numbers_across_issue_cell",
        "ten_numbers_straddling_issue",
        "perfect_two_zodiac",
        "ouyang_half_wave",
        "ziyaner_kill_zodiac",
        "yanran_author_card",
        "heike_nine_tails",
        "jinnang_nine_tails",
        "kill_two_zodiacs",
        "labelled_repeated",
        "labelled_single_head",
        "labelled_single_half_wave",
        "labelled_half_waves",
        "four_elements_bullseye",
        "four_elements_bracket",
        "author_forbidden_zodiac",
    };

    [Fact]
    public void EveryCardSpecificRuleDeclaresItsExtractor()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string group, OcrRule rule) in AllRules())
        {
            if (!Expected.TryGetValue(rule.Id, out string? expected))
                continue;
            Assert.False(string.IsNullOrEmpty(rule.Extractor),
                $"{group} 的「{rule.Id}」没有声明 extractor，会静默变成缺失。");
            Assert.Equal(expected, rule.Extractor);
            seen[rule.Id] = rule.Extractor!;
        }

        string[] missing = Expected.Keys.Where(id => !seen.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void NoConfigCarriesAnExtractorTheCodeDoesNotKnow()
    {
        foreach ((string group, OcrRule rule) in AllRules())
        {
            if (string.IsNullOrEmpty(rule.Extractor))
                continue;
            Assert.True(Known.Contains(rule.Extractor),
                $"{group} 的「{rule.Id}」写了代码不认识的 extractor：{rule.Extractor}");
        }
    }

    [Fact]
    public void RulesWithoutACardParserLeaveTheExtractorEmpty()
    {
        // 按形状取值的规则（严格期块、号码:N、汇总表）不该声明 extractor，
        // 否则会绕过类型化那一段。
        foreach ((string group, OcrRule rule) in AllRules())
        {
            if (Expected.ContainsKey(rule.Id))
                continue;
            Assert.True(string.IsNullOrEmpty(rule.Extractor),
                $"{group} 的「{rule.Id}」不该声明 extractor：{rule.Extractor}");
        }
    }

    private static IEnumerable<(string Group, OcrRule Rule)> AllRules()
    {
        string config = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        foreach (string path in Directory.GetFiles(config, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!name.EndsWith(".json", StringComparison.Ordinal) || name.Contains(".templates.", StringComparison.Ordinal))
                continue;
            if (name.StartsWith("云OCR缓存", StringComparison.Ordinal))
                continue;
            IReadOnlyList<OcrRule> rules;
            try
            {
                rules = RuleCatalog.Load(path);
            }
            catch (OcrException)
            {
                continue;
            }
            foreach (OcrRule rule in rules)
                yield return (name, rule);
        }
    }
}
