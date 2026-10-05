namespace OcrLineTool.Tests;

public sealed class AutoLocalOcrSettingsTests
{
    [Fact]
    public void VisibleAutomaticSwitchDefaultsOffAndRoundTrips()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-auto-local-switch-").FullName;
        try
        {
            var settings = new AutoLocalOcrSettings();
            Assert.False(settings.Enabled);

            settings.Enabled = true;
            settings.Save(directory);

            Assert.True(AutoLocalOcrSettings.Load(directory).Enabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("新澳六合彩资料", true)]
    [InlineData("新澳高手", true)]
    [InlineData("新澳高级会员", true)]
    [InlineData("蜻蜓一套", true)]
    [InlineData("黄大仙新澳", true)]
    [InlineData("嫣然心水", false)]
    [InlineData("慕熙会员群", false)]
    public void VisibleAutomaticGroupsUseTheFixedFiveGroupWhitelist(string group, bool expected)
    {
        Assert.Equal(expected, AutoLocalOcrSettings.IsVisibleAutomaticGroup(group));
    }

    [Fact]
    public void ImageFolderMustRemainUnchangedForOneMinuteBeforeReady()
    {
        var readiness = new AutomaticOcrReadiness();
        DateTime started = new(2026, 10, 5, 12, 0, 0);

        Assert.False(readiness.IsStable("folder", "first", started));
        Assert.False(readiness.IsStable("folder", "first", started.AddSeconds(59)));
        Assert.True(readiness.IsStable("folder", "first", started.AddMinutes(1)));
        Assert.False(readiness.IsStable("folder", "changed", started.AddMinutes(1).AddSeconds(1)));
        Assert.False(readiness.IsStable("folder", "changed", started.AddMinutes(2)));
        Assert.True(readiness.IsStable("folder", "changed", started.AddMinutes(2).AddSeconds(1)));
    }

    [Fact]
    public void VisibleGroupDiscoveryReturnsOnlyTheFiveAutomaticGroupsWithImageSignature()
    {
        string root = Directory.CreateTempSubdirectory("ocr-auto-local-visible-").FullName;
        string config = Path.Combine(root, "配置文件");
        string visible = Path.Combine(root, "新澳高手");
        string excluded = Path.Combine(root, "嫣然心水");
        string other = Path.Combine(root, "慕熙会员群");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(visible);
        Directory.CreateDirectory(excluded);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(config, "新澳高手.json"),
            "{\"group\":\"新澳高手\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllText(Path.Combine(config, "嫣然心水.json"),
            "{\"group\":\"嫣然心水\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllText(Path.Combine(config, "慕熙会员群.json"),
            "{\"group\":\"慕熙会员群\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllBytes(Path.Combine(visible, "card.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(excluded, "card.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(other, "card.jpg"), [1, 2, 3]);

        try
        {
            IReadOnlyList<(string Folder, string Group, string Signature)> found =
                MainForm.FindVisibleAutomaticGroups(root, root);

            Assert.Single(found);
            Assert.Equal(visible, found[0].Folder);
            Assert.Equal("新澳高手", found[0].Group);
            Assert.Contains("card.jpg", found[0].Signature);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
