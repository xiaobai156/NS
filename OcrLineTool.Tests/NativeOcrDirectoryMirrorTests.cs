using System.Text;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class NativeOcrDirectoryMirrorTests
{
    private static string CreateTempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"native-ocr-mirror-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string CreateStore(string folder, string existingShengxiaoValue = "马")
    {
        string path = Path.Combine(folder, "directory-data.json");
        File.WriteAllText(path, $$"""
        {
          "Shengxiao": {
            "Directories": [
              { "Id": "阿尔康奈", "Label": "阿尔康奈" },
              { "Id": "阿萨拉开", "Label": "阿萨拉开" }
            ],
            "Values": { "阿萨拉开": "{{existingShengxiaoValue}}" }
          },
          "Shahao": {
            "Directories": [ { "Id": "天空杀", "Label": "天空杀" } ],
            "Values": {}
          },
          "ShaWuma": {
            "Directories": [ { "Id": "爱晚亭", "Label": "爱晚亭" } ],
            "Values": {}
          },
          "Dawei": {
            "Directories": [ { "Id": "图库", "Label": "图库" } ],
            "Values": {}
          }
        }
        """, new UTF8Encoding(false));
        return path;
    }

    private static JsonElement Values(string storePath, string kind)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(storePath));
        return document.RootElement.GetProperty(kind).GetProperty("Values").Clone();
    }

    [Fact]
    public void WritesOnlyIntoEmptyExactNamedDirectoriesAndSkipsTheRest()
    {
        string folder = CreateTempFolder();
        try
        {
            string store = CreateStore(folder);

            NativeOcrMirrorOutcome outcome = NativeOcrDirectoryMirror.Apply(store, "Shengxiao",
            [
                ("阿尔康奈", "虎狗"),
                ("阿萨拉开", "牛"),
                ("不存在的资料", "龙")
            ]);

            Assert.Equal(1, outcome.Written);
            Assert.Equal(1, outcome.SkippedExisting);
            Assert.Equal(1, outcome.SkippedMissingDirectory);
            Assert.Empty(outcome.Errors);
            JsonElement values = Values(store, "Shengxiao");
            Assert.Equal("虎狗", values.GetProperty("阿尔康奈").GetString());
            Assert.Equal("马", values.GetProperty("阿萨拉开").GetString());
            Assert.Equal(2, values.EnumerateObject().Count());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Shahao", "天空杀", "03 18 21 0306", "03,18,21,03,06")]
    [InlineData("ShaWuma", "爱晚亭", "01 02 0306", "01,02,03,06")]
    [InlineData("Dawei", "图库", "1 2 03 49", "01,02,03,49")]
    [InlineData("Shengxiao", "阿尔康奈", "虎狗", "虎狗")]
    public void FormatsValuesLikeTheOtherApp(string kind, string label, string value, string expected)
    {
        string folder = CreateTempFolder();
        try
        {
            string store = CreateStore(folder);
            NativeOcrMirrorOutcome outcome = NativeOcrDirectoryMirror.Apply(store, kind, [(label, value)]);
            Assert.Equal(1, outcome.Written);
            Assert.Equal(expected, Values(store, kind).GetProperty(label).GetString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NineZodiacsBecomeTheMissingZodiacs()
    {
        string folder = CreateTempFolder();
        try
        {
            string store = CreateStore(folder);
            NativeOcrMirrorOutcome outcome = NativeOcrDirectoryMirror.Apply(
                store, "Shengxiao", [("阿尔康奈", "鼠牛虎兔龙蛇马羊猴")]);
            Assert.Equal(1, outcome.Written);
            Assert.Equal("鸡狗猪", Values(store, "Shengxiao").GetProperty("阿尔康奈").GetString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MissingStoreIsReportedWithoutThrowing()
    {
        string folder = CreateTempFolder();
        try
        {
            string missing = Path.Combine(folder, "directory-data.json");
            NativeOcrMirrorOutcome outcome = NativeOcrDirectoryMirror.Apply(
                missing, "Shengxiao", [("阿尔康奈", "虎狗")]);
            Assert.Equal(0, outcome.Written);
            Assert.Equal(1, outcome.Failed);
            Assert.Single(outcome.Errors);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DistributeAllMirrorsValidatedRowsIntoTheGivenStore()
    {
        string folder = CreateTempFolder();
        try
        {
            string store = CreateStore(folder);
            string config = Path.Combine(folder, "config");
            Directory.CreateDirectory(config);
            string target = Path.Combine(folder, "target");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "243期-杀数字.txt"), string.Empty, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(config, "杀数字分发规则.json"), """
            {
              "targetFile": "{issue}期-杀数字.txt",
              "native_ocr_kind": "Shahao",
              "sources": [ { "sourceGroup": "新澳高手", "labels": [ "天空杀" ] } ]
            }
            """, new UTF8Encoding(false));

            string[] lines = ["03,18,21,22,23,26,34,35,37,39 天空杀"];
            DistributionResult result = await ResultDistributor.DistributeAllAsync(
                @"C:\图片\9.12-新澳高手", 243, lines, target, config, store);

            Assert.Contains(lines[0], result.DistributedLines);
            Assert.NotNull(result.NativeOcrMirror);
            Assert.Equal(1, result.NativeOcrMirror!.Written);
            Assert.Equal("03,18,21,22,23,26,34,35,37,39", Values(store, "Shahao").GetProperty("天空杀").GetString());

            DistributionResult withoutMirror = await ResultDistributor.DistributeAllAsync(
                @"C:\图片\9.12-新澳高手", 243, lines, target, config);
            Assert.Null(withoutMirror.NativeOcrMirror);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
