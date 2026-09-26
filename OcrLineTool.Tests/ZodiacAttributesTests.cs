using OcrLineTool;

namespace OcrLineTool.Tests;

public class ZodiacAttributesTests
{
    private const string Twelve = "鼠牛虎兔龙蛇马羊猴鸡狗猪";

    [Fact]
    public void EveryParsedAttributeIsAddressableByName()
    {
        Assert.NotEmpty(ZodiacAttributes.Entries);
        foreach (ZodiacAttributes.AttributeEntry entry in ZodiacAttributes.Entries)
        {
            Assert.True(ZodiacAttributes.TryGetZodiacs(entry.Name, out string zodiacs));
            Assert.Equal(entry.Zodiacs, zodiacs);
        }
    }

    [Fact]
    public void ZodiacCharactersAreTheSameTwelveTheEngineUses()
    {
        Assert.Equal(Twelve.OrderBy(value => value), RuleEngine.Zodiac.OrderBy(value => value));
    }

    [Fact]
    public void TheRepeatedSourceLinesMergeWithoutConflicts()
    {
        Assert.DoesNotContain(
            ZodiacAttributes.Entries,
            entry => entry.Note?.Contains("重复行的值不同") == true);
    }

    [Theory]
    [InlineData("家禽", "牛马羊鸡狗猪")]
    [InlineData("野兽", "鼠虎兔龙蛇猴")]
    [InlineData("吉美", "兔龙蛇马羊鸡")]
    [InlineData("凶丑", "鼠牛虎猴狗猪")]
    [InlineData("阴性", "鼠龙蛇马狗猪")]
    [InlineData("阳性", "牛虎兔羊猴鸡")]
    [InlineData("单笔", "鼠龙马蛇鸡猪")]
    [InlineData("双笔", "虎猴狗兔羊牛")]
    [InlineData("天肖", "兔马猴猪牛龙")]
    [InlineData("地肖", "蛇羊鸡狗鼠虎")]
    [InlineData("白边", "鼠牛虎鸡狗猪")]
    [InlineData("黑中", "兔龙蛇马羊猴")]
    [InlineData("女肖", "兔蛇羊鸡猪")]
    [InlineData("男肖", "鼠牛虎龙马猴狗")]
    [InlineData("红肖", "马兔鼠鸡")]
    [InlineData("蓝肖", "蛇虎猪猴")]
    [InlineData("绿肖", "羊龙牛狗")]
    [InlineData("三合", "鼠龙猴牛蛇鸡虎马狗兔羊猪")]
    [InlineData("六合", "鼠牛龙鸡虎猪蛇猴兔狗马羊")]
    [InlineData("单", "马龙虎鼠狗猴")]
    [InlineData("双", "蛇兔牛猪鸡羊")]
    public void ReadsTheRecordedAttributeSets(string attribute, string expected)
    {
        Assert.True(ZodiacAttributes.TryGetZodiacs(attribute, out string zodiacs));
        Assert.Equal(expected, zodiacs);
    }

    [Theory]
    [InlineData("琴", "兔蛇鸡")]
    [InlineData("棋", "鼠牛狗")]
    [InlineData("书", "虎龙马")]
    [InlineData("画", "羊猴猪")]
    [InlineData("梅", "鼠龙猴")]
    [InlineData("兰", "兔羊猪")]
    [InlineData("竹", "虎马狗")]
    [InlineData("菊", "牛蛇鸡")]
    [InlineData("东", "兔虎龙")]
    [InlineData("西", "鸡猴狗")]
    [InlineData("南", "马蛇羊")]
    [InlineData("北", "鼠猪牛")]
    [InlineData("春", "兔虎龙")]
    [InlineData("夏", "马蛇羊")]
    [InlineData("秋", "鸡猴狗")]
    [InlineData("冬", "鼠猪牛")]
    public void ReadsTheSingleCharacterAttributeSets(string attribute, string expected)
    {
        Assert.True(ZodiacAttributes.TryGetZodiacs(attribute, out string zodiacs));
        Assert.Equal(expected, zodiacs);
    }

    [Theory]
    [InlineData("琴棋书画", "琴棋书画")]
    [InlineData("梅兰竹菊", "梅兰竹菊")]
    [InlineData("东西南北", "东西南北")]
    [InlineData("方位", "东西南北")]
    [InlineData("四季", "春夏秋冬")]
    [InlineData("春夏秋冬", "春夏秋冬")]
    public void ResolvesTheMembersOfAnAttributeFamily(string family, string expected)
    {
        Assert.Equal(
            expected.Select(value => value.ToString()),
            ZodiacAttributes.FamilyMembers(family));
    }

    [Fact]
    public void UnknownNamesResolveToNothingAndSingleAttributesFormTheirOwnFamily()
    {
        Assert.False(ZodiacAttributes.TryGetZodiacs("四季", out string zodiacs));
        Assert.Equal(string.Empty, zodiacs);
        // 「族」= 同一条表行上的属性；单属性行自成一族，族外名字查不到。
        Assert.Equal(new[] { "红肖" }, ZodiacAttributes.FamilyMembers("红肖"));
        Assert.Empty(ZodiacAttributes.FamilyMembers("不存在"));
    }

    [Fact]
    public void EveryFamilyPartitionsTheTwelveZodiacs()
    {
        string[][] families =
        [
            ["家禽", "野兽"],
            ["吉美", "凶丑"],
            ["阴性", "阳性"],
            ["单笔", "双笔"],
            ["天肖", "地肖"],
            ["白边", "黑中"],
            ["女肖", "男肖"],
            ["红肖", "蓝肖", "绿肖"],
            ["琴", "棋", "书", "画"],
            ["梅", "兰", "竹", "菊"],
            ["东", "西", "南", "北"],
            ["春", "夏", "秋", "冬"],
            ["单", "双"],
        ];

        foreach (string[] family in families)
        {
            var union = new HashSet<char>();
            foreach (string name in family)
            {
                Assert.True(ZodiacAttributes.TryGetZodiacs(name, out string zodiacs), name);
                foreach (char zodiac in zodiacs)
                    Assert.True(union.Add(zodiac), $"{name} 与同族成员重复：{zodiac}");
            }

            Assert.Equal(Twelve.OrderBy(value => value), union.OrderBy(value => value));
        }
    }

    [Fact]
    public void AnnotationsAndRelationsAreKeptAsNotesInsteadOfGuessedValues()
    {
        Assert.True(ZodiacAttributes.TryGetZodiacs("五福肖", out string fiveBlessings));
        Assert.DoesNotContain('龙', fiveBlessings);
        Assert.Contains(
            ZodiacAttributes.Entries,
            entry => entry.Name == "五福肖" && entry.Note?.Contains("[龙]") == true);
        Assert.Contains(
            ZodiacAttributes.Entries,
            entry => entry.Name == "女肖" && entry.Note?.Contains("五宫肖") == true);
        Assert.Contains(
            ZodiacAttributes.Entries,
            entry => entry.Name == "三合" && entry.Note?.Contains("组合关系") == true);
        Assert.Contains(
            ZodiacAttributes.Entries,
            entry => entry.Name == "单" && entry.Note?.Contains("随生肖年变动") == true);
        Assert.Contains(
            ZodiacAttributes.Entries,
            entry => entry.Name == "家禽" && entry.Zodiacs == "牛马羊鸡狗猪");
    }
}
