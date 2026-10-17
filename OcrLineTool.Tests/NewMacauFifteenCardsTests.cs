using System.Text.Json;
using OcrLineTool;
namespace OcrLineTool.Tests;

public sealed class NewMacauFifteenCardsTests
{
    private static string Config => ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
    private static string Fixture(string file) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","Fixtures",file));
    private static List<Card> Cards => JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(Fixture("newmacau-15-cards.json")))!;
    private static OcrRule Rule(string label) => RuleCatalog.Load(Path.Combine(Config,"新澳六合彩资料.json")).Single(r=>r.Id==label);

    [Fact]
    public void FifteenSourcesReuseStrictGroupTypesAndExactRoutes()
    {
        Assert.Equal(15,Cards.Count);
        var templates=VisualTemplateMatcher.Load(Path.Combine(Config,"新澳六合彩资料.templates.json"));
        foreach(var card in Cards)
        {
            var rule=Rule(card.Label);
            Assert.Equal(card.Type,rule.Type);
            Assert.True(rule.StrictIssueBlock);
            Assert.False(rule.AllowNearbyValue);
            Assert.False(rule.IgnoreIssue);
            Assert.Single(templates.Templates,t=>t.RuleIds.SequenceEqual([card.Label]));
            var routes=Directory.GetFiles(Config,"*分发规则.json").Where(path=>
            {
                using var document=JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.GetProperty("sources").EnumerateArray().Any(s=>
                    s.GetProperty("sourceGroup").GetString()=="新澳六合彩资料" &&
                    s.GetProperty("labels").EnumerateArray().Any(l=>l.GetString()==card.Label));
            }).Select(path => Path.GetFileName(path)!).ToArray();
            Assert.Equal([card.Route+"分发规则.json"],routes);
        }
    }

    public static IEnumerable<object[]> Rows()
    {
        yield return ["买什么杀一肖","1肖 今期买：牛 输光光","牛"];
        yield return ["买什么杀十码","今期10码：02 19 22 23 24 25\n34 38 43 46 直接不要","02 19 22 23 24 25 34 38 43 46"];
        yield return ["买什么杀半波","1肖 红波单","红单"];
        yield return ["买什么杀头","0头","0头"];
        yield return ["买什么公式杀行","绝杀一行：火 火 火","火"];
        yield return ["买什么公式杀尾","金牌绝杀一尾：6 6 6","6尾"];
        yield return ["买什么公式杀肖","金牌絕殺一肖：雞 雞 雞","鸡"];
        yield return ["唐朝杀行","唐朝绝杀 土 土 土 稳杀","土"];
        yield return ["唐朝杀头","唐朝绝杀 一头 一头 一头 稳\n杀","1头"];
        yield return ["唐朝杀尾","唐朝绝杀 0 0 0 尾稳杀","0尾"];
        yield return ["唐朝杀肖","唐朝绝杀 猴 猴 猴 肖稳杀","猴"];
        yield return ["黑客杀两肖","数据杀特肖：兔 猴","兔猴"];
        yield return ["老码头杀七码","绝杀7码：08 11 16 18 22 32\n42 稳 稳 稳","08 11 16 18 22 32 42"];
        yield return ["锦囊杀尾","錦囊九尾：（0 1 2 3 4 6 7 8 9）","5尾"];
        yield return ["天宫杀头","天宫 杀（1）头","1头"];
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void CurrentAndChangedIssuesUseTheirOwnField(string label,string payload,string expected)
    {
        var rule=Rule(label);
        foreach(int issue in new[]{290,307})
        {
            string[] lines=["289期开奖 蛇38",$"{issue}期 {payload}","开??",$"{issue-1}期 开蛇38中"];
            Assert.Equal(expected,RuleEngine.ExtractFinalValue(lines,issue,rule));
            Assert.Null(RuleEngine.ExtractFinalValue(lines,issue-2,rule));
            var formatted=Assert.Single(RuleEngine.FormatOutput([rule],new Dictionary<string,string>{{label,expected}}));
            Assert.True(RuleEngine.IsFormattedOutputValueValid(rule,formatted[..^(label.Length+1)]));
        }
    }

    [Theory]
    [InlineData("买什么公式杀肖","金牌绝杀一肖：鸡 兔 鸡")]
    [InlineData("唐朝杀肖","唐朝绝杀 猴 兔 猴 肖稳杀")]
    [InlineData("买什么公式杀行","绝杀一行：火 土 火")]
    [InlineData("唐朝杀行","唐朝绝杀 土 火 土 稳杀")]
    [InlineData("买什么公式杀尾","金牌绝杀一尾：6 2 6")]
    [InlineData("唐朝杀尾","唐朝绝杀 0 4 0 尾稳杀")]
    [InlineData("唐朝杀头","唐朝绝杀 一头 二头 一头 稳杀")]
    [InlineData("黑客杀两肖","数据杀特肖：兔 兔")]
    [InlineData("买什么杀十码","今期10码：02 19 22 23 24 25 34 38 43 50 直接不要")]
    [InlineData("老码头杀七码","绝杀7码：08 11 16 18 22 32 32 稳稳稳")]
    [InlineData("锦囊杀尾","锦囊九尾：（0 1 2 3 4 6 7 8）")]
    [InlineData("锦囊杀尾","锦囊九尾：（0 1 2 3 4 6 7 8 8）")]
    [InlineData("天宫杀头","天宫 杀（5）头")]
    public void InvalidOrDisagreeingFieldsStayMissing(string label,string payload) =>
        Assert.Null(RuleEngine.ExtractFinalValue([$"290期 {payload} 开蛇38中","289期 牛"],290,Rule(label)));

    [Theory]
    [InlineData("买什么公式杀肖","金牌绝杀一肖：鸡 鸡 鸡","金牌绝杀一肖：兔 兔 兔")]
    [InlineData("买什么杀头","0头","1头")]
    public void CompleteSameIssueDisagreementsAreConflicts(string label,string first,string second) =>
        Assert.Equal(RuleExtractionStatus.Conflict,RuleEngine.ExtractFinalResult([$"290期 {first} 开??",$"290期 {second} 开??"],290,Rule(label)).Status);

    [Theory]
    [InlineData("买什么杀十码","今期10码：【01 02 03 04 05】【06 07 08 09 10】直接不要")]
    [InlineData("买什么杀十码","今期10码：【01 02 03 04 05 06 07 08 09 10直接不要")]
    [InlineData("买什么杀十码","今期10码：01 02 03 04 05 06 07 08 09 10】直接不要")]
    [InlineData("老码头杀七码","绝杀7码：【01 02 03】【04 05 06 07】稳稳稳")]
    [InlineData("老码头杀七码","绝杀7码：【01 02 03 04 05 06 07稳稳稳")]
    [InlineData("老码头杀七码","绝杀7码：01 02 03 04 05 06 07】稳稳稳")]
    public void WrappedNumberLabelsPreserveIncompleteContainerEvidence(string label,string payload) =>
        Assert.Null(RuleEngine.ExtractFinalValue([$"290期 {payload} 开??"],290,Rule(label)));

    [Theory]
    [InlineData("买什么杀十码","今期10码：【01 02 03 04 05 06 07 08 09 10】直接不要","01 02 03 04 05 06 07 08 09 10")]
    [InlineData("老码头杀七码","绝杀7码：【01 02 03 04 05 06 07】稳稳稳","01 02 03 04 05 06 07")]
    public void WrappedNumberLabelsAcceptOnlyIndependentlyCompleteContainers(string label,string payload,string expected) =>
        Assert.Equal(expected,RuleEngine.ExtractFinalValue([$"290期 {payload} 开??"],290,Rule(label)));

    [Fact]
    public async Task AllFifteenDistributeOnlyToTheirDesignatedFiles()
    {
        string temp=Path.Combine(Path.GetTempPath(),"ocr-fifteen-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var cards=Cards;
            var rules=cards.Select(c=>Rule(c.Label)).ToArray();
            var values=cards.ToDictionary(c=>c.Label,c=>c.Expected["290"]);
            var lines=RuleEngine.FormatOutput(rules,values);
            foreach(string config in Directory.GetFiles(Config,"*分发规则.json"))
            {
                using var document=JsonDocument.Parse(File.ReadAllText(config));
                string target=document.RootElement.GetProperty("targetFile").GetString()!.Replace("{issue}","307");
                string marker=document.RootElement.TryGetProperty("placement",out var placement)?placement.GetProperty("marker").GetString()!:"";
                string path=Path.Combine(temp,target);
                if(!File.Exists(path))await File.WriteAllTextAsync(path,"已有内容\r\n"+marker+"\r\n");
                var written=await ResultDistributor.DistributeAsync(@"C:\图片\10.17-新澳六合彩资料",307,lines,temp,config);
                string[] expected=cards.Where(c=>Path.GetFileName(config)==c.Route+"分发规则.json").Select(c=>lines.Single(l=>l.EndsWith(" "+c.Label,StringComparison.Ordinal))).ToArray();
                Assert.Equal(expected.Order(),written.Order());
                foreach(string line in expected)Assert.Contains(line,await File.ReadAllTextAsync(path));
                Assert.Empty(await ResultDistributor.DistributeAsync(@"C:\图片\10.17-新澳六合彩资料备份",307,lines,temp,config));
            }
        }
        finally { Directory.Delete(temp,true); }
    }
    [Fact]
    public void ActualMediumEvidenceMatchesAllCompleteVisibleRowsOrStaysMissing()
    {
        var snapshots=JsonSerializer.Deserialize<List<Snapshot>>(File.ReadAllText(Fixture("newmacau-15-medium.json")))!;
        int correct=0,missing=0;
        foreach(var card in Cards)
        {
            var snapshot=snapshots.Single(s=>s.Label==card.Label);
            var evidence=new OcrEvidence(@"C:\图片\10.17-新澳六合彩资料\全部图片\"+card.File,
                "fixture.png","fixture","fixture","medium",snapshot.Items,snapshot.Tokens);
            foreach(var row in card.Expected)
            {
                var result=RuleEngine.ExtractFinalResult(evidence,int.Parse(row.Key),Rule(card.Label));
                if(snapshot.Missing.Contains(int.Parse(row.Key)))
                {
                    Assert.Equal(RuleExtractionStatus.Missing,result.Status);
                    missing++;
                }
                else
                {
                    Assert.Equal(row.Value,result.Value);
                    correct++;
                }
            }
            foreach(int issue in card.Missing.Append(307))
                Assert.Equal(RuleExtractionStatus.Missing,RuleEngine.ExtractFinalResult(evidence,issue,Rule(card.Label)).Status);
        }
        Assert.Equal(151,correct);
        Assert.Equal(9,missing);
    }

    private static OcrEvidence WrappedEvidence(params OcrLineEvidence[] extra)
    {
        OcrLineEvidence[] tokens = [
            new("⑩码必杀",new(130,0,150,20)),
            new("290期",new(0,60,70,20)),new("开??",new(590,60,80,20)),
            new("今期10码：02 19 22 23 24 25",new(100,40,440,20)),
            new("34 38 43 46 直接不要",new(130,80,400,20)),
            new("289期",new(0,160,70,20)),new("蛇38中",new(590,160,80,20)),
            ..extra];
        return new(@"C:\图片\10.17-新澳六合彩资料\card.jpg","fixture","hash","hash","default",tokens,tokens);
    }

    [Fact]
    public void WrappedGeometryKeepsBothLinesInsideTheSamePhysicalCell()
    {
        var result=RuleEngine.ExtractFinalResult(WrappedEvidence(),290,Rule("买什么杀十码"));
        Assert.Equal("02 19 22 23 24 25 34 38 43 46",result.Value);
        Assert.Null(RuleEngine.ExtractFinalValue(WrappedEvidence(),288,Rule("买什么杀十码")));
        Assert.Null(RuleEngine.ExtractFinalValue(WrappedEvidence(new OcrLineEvidence("01",new(250,105,25,20))),290,Rule("买什么杀十码")));
    }

    [Theory]
    [InlineData("other","main")]
    [InlineData("default","other")]
    public void WrappedGeometryCannotJoinAnotherViewOrRegion(string view,string region)
    {
        var e=WrappedEvidence();
        var tokens=e.Items.Select(x=>x.Text.StartsWith("34 ")?x with {ViewId=view,RegionId=region}:x).ToArray();
        Assert.Null(RuleEngine.ExtractFinalValue(e with {Items=tokens,TokenItems=tokens},290,Rule("买什么杀十码")));
    }

    [Fact]
    public void WrappedGeometryKeepsCompleteConflictingRowsAndRejectsMissingIdentity()
    {
        var e=WrappedEvidence(new OcrLineEvidence("290期 今期10码：01 02 03 04 05 06 07 08 09 10 直接不要 开??",new(80,220,500,20)));
        Assert.Equal(RuleExtractionStatus.Conflict,RuleEngine.ExtractFinalResult(e,290,Rule("买什么杀十码")).Status);
        e=WrappedEvidence();
        var tokens=e.Items.Where(x=>x.Text!="⑩码必杀").ToArray();
        Assert.Null(RuleEngine.ExtractFinalValue(e with {Items=tokens,TokenItems=tokens},290,Rule("买什么杀十码")));
    }

    [Fact]
    public void WrappedGeometryPropagatesConflictingCompleteContainers()
    {
        var e=WrappedEvidence();
        var tokens=e.Items.Where(x=>!x.Text.StartsWith("今期10码") && !x.Text.StartsWith("34 "))
            .Append(new OcrLineEvidence("今期10码：【01 02 03 04 05 06 07 08 09 10】【11 12 13 14 15 16 17 18 19 20】直接不要",new(100,50,450,20))).ToArray();
        var result=RuleEngine.ExtractFinalResult(e with {Items=tokens,TokenItems=tokens},290,Rule("买什么杀十码"));
        Assert.Equal(RuleExtractionStatus.Conflict,result.Status);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("买什么公式杀行","绝杀一行：田田田")]
    [InlineData("买什么公式杀肖","金牌绝杀一肖：龙")]
    [InlineData("买什么公式杀肖","金牌绝杀一肖：鸡鸡")]
    [InlineData("买什么公式杀尾","金牌绝杀一尾：1")]
    [InlineData("买什么公式杀尾","金牌绝杀一尾：77")]
    public void BlueCellOcrLossIsNotGuessedOrAcceptedAsACompleteRepeat(string label,string payload) =>
        Assert.Null(RuleEngine.ExtractFinalValue([$"290期 {payload} 开蛇38中"],290,Rule(label)));

    public sealed record Snapshot(string Label,List<OcrLineEvidence> Items,List<OcrLineEvidence> Tokens,int[] Missing);
    public sealed record Card(string Label,string Type,string Keyword,string Route,Dictionary<string,string> Expected,int[] Missing,string File);
}
