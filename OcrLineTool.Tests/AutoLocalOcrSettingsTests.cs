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

    [Fact]
    public void SuccessfulRunIsLockedByFolderDateAndGroupAcrossRestarts()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-auto-local-execution-").FullName;
        try
        {
            DateOnly today = new(2026, 10, 6);
            DateTime now = today.ToDateTime(new TimeOnly(12, 0));
            var settings = new AutoLocalOcrSettings { Enabled = true };

            Assert.True(settings.TryBeginVisibleGroup("10.6-新澳高级会员", "新澳高级会员", today, now));
            settings.RecordVisibleGroupResult("10.6-新澳高级会员", "新澳高级会员", today, success: true, now);
            settings.Save(directory, today);

            AutoLocalOcrSettings reloaded = AutoLocalOcrSettings.Load(directory, today);
            Assert.False(reloaded.CanAttemptVisibleGroup(
                "10.6-新澳高级会员", "新澳高级会员", today, now.AddDays(1)));
            Assert.True(reloaded.CanAttemptVisibleGroup(
                "10.7-新澳高级会员", "新澳高级会员", today.AddDays(1), now.AddDays(1)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedRunIsLockedForTheDateWithoutAutomaticRetry()
    {
        DateOnly today = new(2026, 10, 6);
        DateTime first = today.ToDateTime(new TimeOnly(12, 0));
        var settings = new AutoLocalOcrSettings { Enabled = true };

        Assert.True(settings.TryBeginVisibleGroup("10.6-新澳高手", "新澳高手", today, first));
        settings.RecordVisibleGroupResult("10.6-新澳高手", "新澳高手", today, success: false, first);
        Assert.False(settings.CanAttemptVisibleGroup(
            "10.6-新澳高手", "新澳高手", today, first.AddMinutes(5)));
        Assert.False(settings.TryBeginVisibleGroup(
            "10.6-新澳高手", "新澳高手", today, first.AddHours(1)));
        Assert.Equal("failed", settings.Executions.Single().Value.Status);
        Assert.Null(settings.Executions.Single().Value.NextRetryAt);
        Assert.False(settings.CanAttemptVisibleGroup(
            "10.6-新澳高手", "新澳高手", today, first.AddHours(1)));
    }

    [Fact]
    public void ExecutionLogKeepsOnlyTheMostRecentSevenDates()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-auto-local-prune-").FullName;
        try
        {
            DateOnly today = new(2026, 10, 6);
            var settings = new AutoLocalOcrSettings { Enabled = true };
            foreach (int offset in Enumerable.Range(-8, 10))
            {
                DateOnly date = today.AddDays(offset);
                DateTime now = date.ToDateTime(new TimeOnly(12, 0));
                settings.TryBeginVisibleGroup($"{date:MM.dd}-新澳高手", "新澳高手", date, now);
                settings.RecordVisibleGroupResult($"{date:MM.dd}-新澳高手", "新澳高手", date, true, now);
            }

            settings.Save(directory, today);
            AutoLocalOcrSettings reloaded = AutoLocalOcrSettings.Load(directory, today);

            Assert.Equal(7, reloaded.Executions.Count);
            Assert.DoesNotContain(reloaded.Executions.Values, item => item.Date == "2026-09-29");
            Assert.Contains(reloaded.Executions.Values, item => item.Date == "2026-09-30");
            Assert.Contains(reloaded.Executions.Values, item => item.Date == "2026-10-06");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("10.6-新澳高级会员", "2026-10-06")]
    [InlineData("2026-10-06-新澳高级会员", "2026-10-06")]
    [InlineData("新澳高级会员", "2026-10-06")]
    public void FolderDateUsesRecognizableDateWrapperOrFallback(
        string folder, string expected)
    {
        DateOnly fallback = new(2026, 10, 6);
        Assert.Equal(expected, AutoLocalOcrSettings.DateForFolder(folder, fallback).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void LegacyLastRunDatesAreIgnoredSoStaleClaimsDoNotBlockRun()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-auto-local-migrate-").FullName;
        try
        {
            File.WriteAllText(
                AutoLocalOcrSettings.PathFor(directory),
                "{\"Enabled\":true,\"LastRunDates\":{\"新澳高手\":\"2026-10-06\",\"嫣然心水\":\"2026-10-06\"}}");

            AutoLocalOcrSettings settings = AutoLocalOcrSettings.Load(directory, new DateOnly(2026, 10, 6));

            Assert.Empty(settings.Executions);
            Assert.True(settings.CanAttemptVisibleGroup(
                "10.6-新澳六合彩资料", "新澳六合彩资料", new DateOnly(2026, 10, 6),
                new DateTime(2026, 10, 6, 12, 0, 0)));
            settings.Save(directory, new DateOnly(2026, 10, 6));
            Assert.DoesNotContain("LastRunDates", File.ReadAllText(AutoLocalOcrSettings.PathFor(directory)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}
