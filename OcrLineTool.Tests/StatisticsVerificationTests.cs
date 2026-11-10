using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public class StatisticsVerificationTests
{
    public sealed record Sample(string Rule, int Issue, string Expected, OcrEvidence Medium, OcrEvidence Small);
    public static Sample[] Samples() => JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "statistics-314.json")))!;
    private static OcrRule Rule(string name) => new(name, "统计生肖", name);
    private static OcrEvidence Verify(Sample sample) => StatisticsVerification.Attach(
        sample.Medium with { Reader = "paddle/medium" }, sample.Small with { Reader = "paddle/small" });

    [Fact]
    public void Real314CardsNeedIndependentCompleteEvidenceAndRejectAdjacentIssues()
    {
        foreach (var sample in Samples())
        {
            var rule = Rule(sample.Rule);
            Assert.Null(RuleEngine.ExtractFinalResult(sample.Medium, sample.Issue, rule).Value);
            var verified = Verify(sample);
            var result = RuleEngine.ExtractStatisticsEvidence(verified, sample.Issue, rule, out var reason);
            Assert.True(result.Value == sample.Expected, sample.Rule + ": " + reason);
            Assert.Null(RuleEngine.ExtractFinalResult(verified, 313, rule).Value);
            Assert.Null(RuleEngine.ExtractFinalResult(verified, 315, rule).Value);
            var restored = JsonSerializer.Deserialize<OcrEvidence>(JsonSerializer.Serialize(verified))!;
            Assert.Equal(sample.Expected, RuleEngine.ExtractFinalResult(restored, sample.Issue, rule).Value);
        }
    }

    [Fact]
    public void EqualWinnerIsInsufficientWhenAnyFrequencyOrZodiacDisagrees()
    {
        var sample = Samples()[0];
        foreach (var replacement in new[] { "[07次]龙", "[06次]鼠", "[06次]" })
        {
            var small = sample.Small with { Items = sample.Small.Items.Select(i =>
                i.Text.Contains("06次") ? i with { Text = replacement } : i).ToArray() };
            Assert.Null(RuleEngine.ExtractFinalResult(Verify(sample with { Small = small }), 314, Rule(sample.Rule)).Value);
        }
    }

    [Fact]
    public void ModelHashRegionAndGeometryCannotBeSubstituted()
    {
        var s = Samples()[0];
        var primary = s.Medium with { Reader = "paddle/medium" };
        var small = s.Small with { Reader = "paddle/small" };
        foreach (var bad in new[] {
            small with { Reader = "paddle/medium" },
            small with { InputHash = "changed" },
            small with { SourceHash = "changed" },
            small with { Items = small.Items.Select(i => i with { Box = null }).ToArray() },
            small with { Items = small.Items.Select(i => i with { Box = i.Box! with { Y = i.Box.Y + 500 } }).ToArray() },
            small with { Items = small.Items.Select((i,n) => i with { ViewId = n % 2 == 0 ? "a" : "b" }).ToArray() }
        })
            Assert.Null(RuleEngine.ExtractFinalResult(StatisticsVerification.Attach(primary, bad),314,Rule(s.Rule)).Value);
        Assert.Null(RuleEngine.ExtractFinalResult(Verify(s),314,Rule("小慧慧")).Value);
    }

    [Fact]
    public void AllHighestTiesAreReturnedAcrossRowSplitsAndWrongTotalsRemainStrictWithoutProof()
    {
        const string animals = "鼠牛虎兔龙蛇马羊猴鸡狗猪";
        for (int n = 1; n <= 12; n++)
        {
            string top = animals[..n];
            foreach (var rows in new[] { new[]{"[3次]"+top}, top.Select(c=>"[3次]"+c).ToArray() })
            {
                Assert.Equal(top, RuleEngine.ExtractFinalValue(["314期", $"总次数:{n*3}次", ..rows],314,Rule("品鉴")));
                Assert.Null(RuleEngine.ExtractFinalValue(["314期", $"总次数:{n*3+1}次", ..rows],314,Rule("品鉴")));
            }
        }
        Assert.Null(RuleEngine.ExtractFinalValue(["314期", "总次数:20次", "[3次]鼠牛", "共21人"],314,Rule("品鉴")));
        Assert.Null(RuleEngine.ExtractFinalValue(["314期", "总次数:6次", "[3次]鼠牛", "总次数:6次 共7人"],314,Rule("品鉴")));
        Assert.Null(RuleEngine.ExtractFinalValue(["314期", "[7次]龙", "[6次]龙", "[7次]鸡"],314,Rule("品鉴")));
    }

    private static OcrEvidence Positioned(params string[] lines) => new("source.jpg", "source.jpg", "hash", "hash", "original",
        lines.Select((text,n) => new OcrLineEvidence(text, new(0,n*30,400,24), .99, "original", "main")).ToArray())
        { Reader = "paddle/medium" };
    private static OcrEvidence Confirm(OcrEvidence e) => StatisticsVerification.Attach(e, e with { Reader = "paddle/small" });

    [Fact]
    public void IndependentConfirmationKeepsOneThroughTwelveTiedWinnersEvenWithLargerPrintedErrors()
    {
        const string animals = "鼠牛虎兔龙蛇马羊猴鸡狗猪";
        for (int n=1; n<=12; n++)
            foreach (bool split in new[]{false,true})
            {
                string[] rows = split ? animals[..n].Select(c=>"[7次]"+c).ToArray() : ["[7次]"+animals[..n]];
                var e = Confirm(Positioned(["品鉴", "314期", $"总次数:{n*7+10}次", "[0次]"+animals[n..], ..rows]));
                Assert.Equal(animals[..n], RuleEngine.ExtractFinalResult(e,314,Rule("品鉴")).Value);
            }
    }

    [Fact]
    public void RejectsMergedLabelsUnknownPayloadInterruptedRowsAndForeignTables()
    {
        string rest = "[0次]鼠牛虎兔蛇马羊猴狗猪";
        foreach (string bad in new[]{"[7次]龙[6次]鸡", "[7次]龙鸡待更新", "[7次]龙鸡8", "[7次]龙鸡其他作者"})
        {
            var e = Confirm(Positioned("品鉴","314期","总次数:13次",rest,bad));
            Assert.Null(RuleEngine.ExtractFinalResult(e,314,Rule("品鉴")).Value);
        }
        var interrupted = Positioned("品鉴","314期",rest,"[7次]龙鸡","本期统计说明","总次数:13次");
        Assert.Null(RuleEngine.ExtractFinalResult(interrupted,314,Rule("品鉴")).Value);
        var extraRows = Confirm(Positioned("品鉴","314期",rest,"[7次]龙","别人统计","[7次]鸡"));
        Assert.Null(RuleEngine.ExtractFinalResult(extraRows,314,Rule("品鉴")).Value);
        var foreign = Positioned("品鉴","313期","小慧慧","314期","总次数:13次",rest,"[7次]龙鸡");
        Assert.Null(RuleEngine.ExtractFinalResult(Confirm(foreign),314,Rule("品鉴")).Value);
        var otherColumn = foreign with { Items = foreign.Items.Select((i,n) => n<2 ? i : i with { Box = i.Box! with { X = 700 } }).ToArray() };
        Assert.Null(RuleEngine.ExtractFinalResult(Confirm(otherColumn),314,Rule("品鉴")).Value);
        var reordered = foreign with { Items = [foreign.Items[2],foreign.Items[0],foreign.Items[1],..foreign.Items.Skip(3)] };
        Assert.Null(RuleEngine.ExtractFinalResult(Confirm(reordered),314,Rule("品鉴")).Value);
        var mixed = Positioned("品鉴","314期 313期","总次数:13次",rest,"[7次]龙鸡");
        Assert.Null(RuleEngine.ExtractFinalResult(Confirm(mixed),314,Rule("品鉴")).Value);
        Assert.Null(RuleEngine.ExtractFinalResult(mixed.Lines,314,Rule("品鉴")).Value);
        var differentRegion = Positioned("品鉴","314期","总次数:13次",rest,"[7次]龙鸡");
        differentRegion = differentRegion with { Items = differentRegion.Items.Select((i,n)=>n==1 ? i with {RegionId="other"} : i).ToArray() };
        Assert.Null(RuleEngine.ExtractFinalResult(Confirm(differentRegion),314,Rule("品鉴")).Value);
    }

    [Fact]
    public void CorruptProofCannotCrashOrFallThroughToStrictSuccess()
    {
        var verified = Verify(Samples()[0]);
        foreach (var check in new[]{verified.StatisticsCheck! with { Items = null! },
            verified.StatisticsCheck! with { Items = [] },
            verified.StatisticsCheck! with { Items = [null!] },
            verified.StatisticsCheck! with { Version = -1 }})
            Assert.Null(RuleEngine.ExtractFinalResult(verified with {StatisticsCheck=check},314,Rule("品鉴")).Value);
    }

    [Fact]
    public async Task CacheAndRecognitionStateRetainProofAndRejectItsRemovalOrMutation()
    {
        string root = Path.Combine(Path.GetTempPath(), "ocr-statistics-test-"+Guid.NewGuid().ToString("N"));
        string group = Path.Combine(root,"嫣然心水");
        Directory.CreateDirectory(group);
        string path = Path.Combine(group,"card.jpg");
        File.WriteAllBytes(path,[1,2,3]);
        try
        {
            var fixture = Samples()[0];
            var identity = OcrEvidenceIdentity.Capture(path,path,"cloud");
            OcrEvidence Bound(OcrEvidence e,string reader) => e with {
                SourcePath=path,InputPath=path,SourceHash=identity.SourceHash,InputHash=identity.InputHash,Reader=reader };
            var cloud = StatisticsVerification.Attach(Bound(fixture.Medium,"cloud"),Bound(fixture.Small,"paddle/small"));
            var rule = Rule("品鉴");
            CloudOcrCacheStore.SaveEntry(root,"嫣然心水",314,cloud);
            var restored = CloudOcrCacheStore.LoadEvidence(root,"嫣然心水",314)[path];
            Assert.Equal("鸡", RuleEngine.ExtractFinalResult(restored,314,rule).Value);
            Assert.True(MainForm.CanReuseRetryCloudEvidence(new ResultValues(StringComparer.Ordinal),[rule],restored,314));
            Assert.False(MainForm.CanReuseRetryCloudEvidence(new ResultValues(StringComparer.Ordinal),[rule],restored with {StatisticsCheck=null},314));
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();
            MainForm.AddExtractedEvidenceValues(restored,[rule],314,values,ledger);
            Assert.Equal("鸡",values[rule.Id]);
            await RecognitionStateStore.SaveAsync(root,group,314,[rule],values,ledger);
            Assert.Equal("鸡",RecognitionStateStore.Load(root,group,314,[rule]).Values[rule.Id]);
            string state = ResultFilePaths.ForRecognitionState(root,group,314);
            string good = File.ReadAllText(state);
            var node = System.Text.Json.Nodes.JsonNode.Parse(good)!;
            node["Results"]![0]!["StatisticsEvidence"] = null;
            File.WriteAllText(state,node.ToJsonString());
            Assert.Empty(RecognitionStateStore.Load(root,group,314,[rule]).Values);
            File.WriteAllText(state,good);
            var ties = new ResultValues(StringComparer.Ordinal);
            var tieLedger = new ResultEvidenceLedger();
            foreach (string order in new[]{"龙鸡","鸡龙"})
            {
                var table = Positioned("品鉴","314期","总次数:13次","[0次]鼠牛虎兔蛇马羊猴狗猪","[7次]"+order);
                var confirmed = StatisticsVerification.Attach(Bound(table,"paddle/medium"),Bound(table,"paddle/small"));
                MainForm.AddExtractedEvidenceValues(confirmed,[rule],314,ties,tieLedger);
            }
            await RecognitionStateStore.SaveAsync(root,group,314,[rule],ties,tieLedger);
            Assert.Equal("龙鸡",RecognitionStateStore.Load(root,group,314,[rule]).Values[rule.Id]);
            File.WriteAllBytes(path,[9,8,7]);
            Assert.Empty(CloudOcrCacheStore.LoadEvidence(root,"嫣然心水",314));
            Assert.Empty(RecognitionStateStore.Load(root,group,314,[rule]).Values);
        }
        finally { Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData(LocalOcrDevice.Gpu)]
    [InlineData(LocalOcrDevice.Cpu)]
    public async Task VerificationFailureNeverSubstitutesAValueAndGpuFailurePropagates(LocalOcrDevice device)
    {
        string root = Path.Combine(Path.GetTempPath(),"ocr-statistics-failure-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root,"card.jpg");
        File.WriteAllBytes(path,[1,2,3]);
        try
        {
            var identity = OcrEvidenceIdentity.Capture(path,path,"test");
            var fixture = Samples()[0];
            var evidence = fixture.Medium with { SourcePath=path,InputPath=path,
                SourceHash=identity.SourceHash,InputHash=identity.InputHash,Reader="paddle/medium" };
            var client = new PaddleLocalOcrClient(new FailedRunner(),Path.Combine(root,"cache.json"),device);
            var rule = Rule(fixture.Rule);
            if (device == LocalOcrDevice.Gpu)
            {
                var error = await Assert.ThrowsAsync<OcrException>(() =>
                    StatisticsVerification.VerifyBatchAsync([evidence],[rule],314,client,CancellationToken.None));
                Assert.True(PaddleLocalOcrClient.IsCudaUnavailable(error));
            }
            else
            {
                var result = await StatisticsVerification.VerifyBatchAsync([evidence],[rule],314,client,CancellationToken.None);
                Assert.Null(result[0].StatisticsCheck);
                Assert.Null(RuleEngine.ExtractFinalResult(result[0],314,rule).Value);
            }
            File.WriteAllBytes(path,[9,8,7]);
            var changed = await Assert.ThrowsAsync<OcrException>(() =>
                StatisticsVerification.VerifyBatchAsync([evidence],[rule],314,client,CancellationToken.None));
            Assert.Equal("OCR_IMAGE_CHANGED",changed.Code);
        }
        finally { Directory.Delete(root,true); }
    }

    private sealed class FailedRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo,
            Action<string>? reportStandardOutputLine, CancellationToken cancellationToken, Action? reportStarted) =>
            Task.FromResult(new ProcessResult(false,1,"","test startup failure"));
    }
}
