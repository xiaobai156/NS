using System.Text.RegularExpressions;

namespace OcrLineTool;

/// <summary>
/// 生肖属性表：属性 → 生肖集合。数据源是用户 2026-09-26 提供的「生肖属性」表，原文完整保存在
/// <see cref="Source"/>（这是唯一事实源，别处不要再硬编码这些属性集合）。
/// 解析约定：
/// 1. 一行可以写多个「属性名：值」对（例如 琴：兔蛇鸡　棋：鼠牛狗），同一行的多个属性属于同一个
///    族（Family = 该行属性名连写，如 琴棋书画 / 梅兰竹菊），族内成员合起来正好覆盖十二生肖。
/// 2. 一行只有一个属性时自成一族（家禽、红肖、三合…），其值就是生肖串。
/// 3. 圆括号/方括号里的说明（女肖的「五宫肖」、五福肖的「[龙]」）不算值，收进 Note 留档，不猜测。
/// 4. 三合/六合是「组合关系」而不是属性集合；单/双（号码单双）随生肖年变动，本表记的是 2026 马年
///    的值。这两点都写进 Note，勿当普通集合使用。
/// </summary>
internal static class ZodiacAttributes
{
    internal sealed record AttributeEntry(string Name, string Zodiacs, string Family, string? Note);

    // 用户原始表：照抄，勿改格式（重复的两行 家禽/野兽 是原表本来就有的，解析时按同名合并）。
    internal const string Source = @"
家禽：牛、马、羊、鸡、狗、猪
野兽：鼠、虎、兔、龙、蛇、猴
吉美：兔、龙、蛇、马、羊、鸡
凶丑：鼠、牛、虎、猴、狗、猪
阴性：鼠、龙、蛇、马、狗、猪
阳性：牛、虎、兔、羊、猴、鸡
单笔：鼠、龙、马、蛇、鸡、猪
双笔：虎、猴、狗、兔、羊、牛
天肖：兔、马、猴、猪、牛、龙
地肖：蛇、羊、鸡、狗、鼠、虎
白边：鼠、牛、虎、鸡、狗、猪
黑中：兔、龙、蛇、马、羊、猴
女肖：兔、蛇、羊、鸡、猪(五宫肖)
男肖：鼠、牛、虎、龙、马、猴、狗
三合：鼠龙猴、牛蛇鸡、虎马狗、兔羊猪
六合：鼠牛、龙鸡、虎猪、蛇猴、兔狗、马羊
五福肖：鼠、虎、兔、蛇、猴[龙]
红肖：马、兔、鼠、鸡
蓝肖：蛇、虎、猪、猴
绿肖：羊、龙、牛、狗
琴：兔蛇鸡　棋：鼠牛狗　书： 虎龙马　画：羊猴猪
梅：鼠龙猴　兰：兔羊猪　竹： 虎马狗　菊：牛蛇鸡
东：兔虎龙　西：鸡猴狗　南： 马蛇羊　北：鼠猪牛
春：兔虎龙　夏：马蛇羊　秋： 鸡猴狗　冬：鼠猪牛
家禽：牛马羊鸡狗猪
野兽：鼠虎兔龙蛇猴
单: 马龙虎鼠狗猴
双: 蛇兔牛猪鸡羊
";

    // 「属性名：值」；值取到下一个「属性名：」之前（属性名不含空格和冒号）。
    private static readonly Regex PairPattern = new(
        @"(?<name>[^\s：:]{1,4})\s*[:：]\s*(?<value>.*?)(?=\s*[^\s：:]{1,4}\s*[:：]|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 圆括号 / 方括号 / 【】 里的说明文字。
    private static readonly Regex AnnotationPattern = new(
        @"[（(【\[][^）)】\]]*[）)】\]]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 卡面和既有类型用的族名与表里四字连写的族名不一致，这里显式对上。
    private static readonly Dictionary<string, string> FamilyAliases = new(StringComparer.Ordinal)
    {
        ["方位"] = "东西南北",
        ["四季"] = "春夏秋冬",
    };

    private static readonly IReadOnlyList<AttributeEntry> ParsedEntries = Parse(Source);

    private static readonly Dictionary<string, AttributeEntry> EntriesByName =
        ParsedEntries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, string[]> FamilyMembersByFamily =
        ParsedEntries
            .GroupBy(entry => entry.Family, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Name).ToArray(),
                StringComparer.Ordinal);

    internal static IReadOnlyList<AttributeEntry> Entries => ParsedEntries;

    /// <summary>查某属性的生肖串（原表顺序，去分隔符）。查不到返回 false。</summary>
    internal static bool TryGetZodiacs(string attribute, out string zodiacs)
    {
        if (EntriesByName.TryGetValue(attribute, out AttributeEntry? entry))
        {
            zodiacs = entry.Zodiacs;
            return true;
        }

        zodiacs = string.Empty;
        return false;
    }

    /// <summary>查某族（琴棋书画 / 梅兰竹菊 / 方位 / 四季…）的成员属性字。查不到返回空。</summary>
    internal static IReadOnlyList<string> FamilyMembers(string family)
    {
        if (!FamilyMembersByFamily.TryGetValue(family, out string[]? members)
            && FamilyAliases.TryGetValue(family, out string? canonical))
        {
            FamilyMembersByFamily.TryGetValue(canonical, out members);
        }

        return members ?? Array.Empty<string>();
    }

    /// <summary>族的成员属性字（四季 → 春夏秋冬），用于判断一个值是否是本族的属性字。</summary>
    internal static string FamilyCharacters(string family) =>
        string.Concat(FamilyMembers(family).Select(name => name[0]));

    private static List<AttributeEntry> Parse(string table)
    {
        var entries = new List<AttributeEntry>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string rawLine in table.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            MatchCollection pairs = PairPattern.Matches(line);
            if (pairs.Count == 0)
                continue;

            string family = string.Concat(pairs.Select(pair => pair.Groups["name"].Value));
            foreach (Match pair in pairs)
            {
                string name = pair.Groups["name"].Value;
                string raw = pair.Groups["value"].Value;
                var noteParts = new List<string>();
                foreach (Match annotation in AnnotationPattern.Matches(raw))
                    noteParts.Add(annotation.Value);
                if (name is "三合" or "六合")
                    noteParts.Add("组合关系，不是属性集合（值按分组读）");
                if (name is "单" or "双")
                    noteParts.Add("号码单双，随生肖年变动，本表是 2026 马年的值");

                string zodiacs = Regex.Replace(
                    AnnotationPattern.Replace(raw, string.Empty),
                    $"[^{RuleEngine.Zodiac}]",
                    string.Empty);
                string? note = noteParts.Count == 0 ? null : string.Join(" ", noteParts);

                if (indexByName.TryGetValue(name, out int index))
                {
                    // 原表里 家禽/野兽 各出现两遍：值一致就按同名合并，不一致必须留下痕迹。
                    if (entries[index].Zodiacs != zodiacs)
                    {
                        entries[index] = entries[index] with
                        {
                            Note = string.Join(
                                " ",
                                new[] { entries[index].Note, $"重复行的值不同：{zodiacs}" }
                                    .Where(part => !string.IsNullOrEmpty(part))),
                        };
                    }

                    continue;
                }

                indexByName[name] = entries.Count;
                entries.Add(new AttributeEntry(name, zodiacs, family, note));
            }
        }

        return entries;
    }
}
