namespace OcrLineTool.Tests;

public sealed class AutoLocalOcrSettingsTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failed")]
    [InlineData("running")]
    public void ExecutedGroupsNeverAppearAsWaitingEvenWhenTheirImagesChange(string status)
    {
        DateOnly today = new(2026, 10, 6);
        DateTime now = today.ToDateTime(new TimeOnly(12, 0));
        AutoLocalOcrSettings settings = Selected("新澳六合彩资料", "新澳高手", "新澳高级会员", "蜻蜓一套", "黄大仙新澳");
        string[] groups = ["新澳六合彩资料", "新澳高手", "新澳高级会员", "蜻蜓一套", "黄大仙新澳"];
        foreach (string group in groups)
        {
            settings.TryBeginVisibleGroup($"10.6-{group}", group, today, now);
            if (status != "running")
                settings.RecordVisibleGroupResult($"10.6-{group}", group, today, status == "success", now);
        }
        var changed = groups.Select(group => ($"10.6-{group}", group, "new-image-signature")).ToArray();

        Assert.Empty(MainForm.PendingAutomaticGroups(settings, changed, now.AddSeconds(1)));
        Assert.Equal("自动本地 OCR：当前没有待执行群。", MainForm.AutomaticWaitingStatus(
            "自动本地 OCR 等待图片稳定：黄大仙新澳（还需满 1 分钟）", null));
        Assert.All(settings.Executions.Values, execution => Assert.Equal(1, execution.Attempts));
    }

    [Fact]
    public void OnlyUnattemptedCurrentDateGroupsEnterTheStabilityWindow()
    {
        DateTime now = new(2026, 10, 6, 12, 0, 0);
        AutoLocalOcrSettings settings = Selected("新澳六合彩资料", "新澳高手", "新澳高级会员", "蜻蜓一套", "黄大仙新澳");
        (string Folder, string Group, string Signature)[] visible =
        [
            ("10.5-新澳高手", "新澳高手", "old"),
            ("10.7-新澳高手", "新澳高手", "future"),
            ("10.6-嫣然心水", "嫣然心水", "excluded"),
            ("10.6-黄大仙新澳", "黄大仙新澳", "current")
        ];
        var pending = Assert.Single(MainForm.PendingAutomaticGroups(settings, visible, now));
        var readiness = new AutomaticOcrReadiness();
        Assert.False(readiness.IsStable(pending.Folder, pending.Signature, now));
        Assert.True(readiness.IsStable(pending.Folder, pending.Signature, now.AddMinutes(1)));
        Assert.Equal("自动本地 OCR 等待图片稳定：黄大仙新澳（还需满 1 分钟）",
            MainForm.AutomaticWaitingStatus("识别完成", pending.Group));
        // 取消勾选后该群不再进入稳定观察窗口。
        settings.SetSelectedGroup("黄大仙新澳", false);
        Assert.Empty(MainForm.PendingAutomaticGroups(settings, visible, now));
    }

    [Theory]
    [InlineData("识别完成")]
    [InlineData("自动本地 OCR 执行记录保存失败，已跳过本次任务。")]
    [InlineData("自动本地 OCR 执行失败，当日不再自动重试。")]
    [InlineData("云 OCR 限流，请继续云 OCR。")]
    public void ClearingAnObsoleteWaitDoesNotEraseOtherStatusMessages(string status)
    {
        Assert.Equal(status, MainForm.AutomaticWaitingStatus(status, null));
    }

    [Fact]
    public void VisibleAutomaticSwitchDefaultsOffAndRoundTrips()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-auto-local-switch-").FullName;
        try
        {
            var settings = new AutoLocalOcrSettings();
            Assert.Empty(settings.SelectedGroups);

            settings = Selected("新澳六合彩资料", "新澳高手", "黑色六合彩");
            settings.Save(directory);

            // 勾选状态必须落盘并在重启后恢复。
            AutoLocalOcrSettings loaded = AutoLocalOcrSettings.Load(directory);
            Assert.Equal(3, loaded.SelectedGroups.Count);
            Assert.True(loaded.IsSelectedGroup("黑色六合彩"));
            Assert.False(loaded.IsSelectedGroup("嫣然电报"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // 自动识别改由「自动识别」窗口按群勾选：只有勾上的群参与，不再有固定五个群的白名单；
    // 嫣然心水始终排除。
    [Theory]
    [InlineData("新澳六合彩资料", true)]
    [InlineData("嫣然电报", true)]
    [InlineData("黑色六合彩", true)]
    [InlineData("嫣然六合彩大全", true)]
    [InlineData("未勾选的群", false)]
    [InlineData("嫣然心水", true)]
    public void OnlySelectedGroupsParticipateInAutomaticRecognition(string group, bool expected)
    {
        var settings = new AutoLocalOcrSettings();
        foreach (string name in new[] { "新澳六合彩资料", "嫣然电报", "黑色六合彩", "嫣然六合彩大全" })
            settings.SetSelectedGroup(name, true);
        // 所有群一视同仁：嫣然心水也能勾选。
        settings.SetSelectedGroup("嫣然心水", true);
        Assert.Equal(expected, settings.IsSelectedGroup(group));
    }

    // 取消勾选后该群不再参与；重复勾选不会产生重复项。
    [Fact]
    public void SelectingAndClearingAGroupIsIdempotent()
    {
        var settings = new AutoLocalOcrSettings();
        settings.SetSelectedGroup("黑色六合彩", true);
        settings.SetSelectedGroup("黑色六合彩", true);
        Assert.Single(settings.SelectedGroups);
        settings.SetSelectedGroup("黑色六合彩", false);
        Assert.Empty(settings.SelectedGroups);
        Assert.False(settings.IsSelectedGroup("黑色六合彩"));
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
        string other = Path.Combine(root, "嫣然电报");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(visible);
        Directory.CreateDirectory(excluded);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(config, "新澳高手.json"),
            "{\"group\":\"新澳高手\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllText(Path.Combine(config, "嫣然心水.json"),
            "{\"group\":\"嫣然心水\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllText(Path.Combine(config, "嫣然电报.json"),
            "{\"group\":\"嫣然电报\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllBytes(Path.Combine(visible, "card.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(excluded, "card.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(other, "card.jpg"), [1, 2, 3]);

        try
        {
            var settings = new AutoLocalOcrSettings();
            settings.SetSelectedGroup("新澳高手", true);
            settings.SetSelectedGroup("嫣然电报", true);
            settings.SetSelectedGroup("嫣然心水", true);
            IReadOnlyList<(string Folder, string Group, string Signature)> found =
                MainForm.FindVisibleAutomaticGroups(root, root, settings);

            // 所有群一视同仁：勾选的群都应出现，嫣然心水不再被排除。
            Assert.Equal(3, found.Count);
            Assert.Contains(found, item => item.Folder == visible && item.Group == "新澳高手");
            Assert.Contains(found, item => item.Folder == other && item.Group == "嫣然电报");
            Assert.Contains(found, item => item.Folder == excluded);
            Assert.All(found, item => Assert.Contains("card.jpg", item.Signature));
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
            AutoLocalOcrSettings settings = Selected("新澳六合彩资料", "新澳高手", "新澳高级会员", "蜻蜓一套", "黄大仙新澳");

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
        AutoLocalOcrSettings settings = Selected("新澳六合彩资料", "新澳高手", "新澳高级会员", "蜻蜓一套", "黄大仙新澳");

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
            AutoLocalOcrSettings settings = Selected("新澳六合彩资料", "新澳高手", "新澳高级会员", "蜻蜓一套", "黄大仙新澳");
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
            // 自动识别按群勾选，未勾选的群不参与；这里勾上被测群。
            settings.SetSelectedGroup("新澳六合彩资料", true);

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


    // 自动识别改为按群勾选：测试里把需要的群一次性勾上。
    private static AutoLocalOcrSettings Selected(params string[] groups)
    {
        var settings = new AutoLocalOcrSettings();
        foreach (string group in groups)
            settings.SetSelectedGroup(group, true);
        return settings;
    }

    // 「未勾选的群不执行」是关键路径：目录里存在但没勾的群必须被跳过。
    [Fact]
    public void UnselectedGroupsAreSkippedEvenWhenTheirFolderExists()
    {
        string root = Directory.CreateTempSubdirectory("ocr-auto-local-unselected-").FullName;
        string config = Path.Combine(root, "配置文件");
        string chosen = Path.Combine(root, "黑色六合彩");
        string skipped = Path.Combine(root, "嫣然电报");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(chosen);
        Directory.CreateDirectory(skipped);
        File.WriteAllText(Path.Combine(config, "黑色六合彩.json"),
            "{\"group\":\"黑色六合彩\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllText(Path.Combine(config, "嫣然电报.json"),
            "{\"group\":\"嫣然电报\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllBytes(Path.Combine(chosen, "a.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(skipped, "b.jpg"), [1, 2, 3]);

        try
        {
            var settings = new AutoLocalOcrSettings();
            settings.SetSelectedGroup("黑色六合彩", true);
            IReadOnlyList<(string Folder, string Group, string Signature)> found =
                MainForm.FindVisibleAutomaticGroups(root, root, settings);

            Assert.Single(found);
            Assert.Equal("黑色六合彩", found[0].Group);
            Assert.DoesNotContain(found, item => item.Group == "嫣然电报");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    // 抓取目录为空时，群列表仍要能列出配置文件里登记的所有群，否则用户无从勾选。
    // 断言落在 AutoRecognizeForm.BuildGroupList 的产出上：把三段兜底删掉这条就会失败。
    [Fact]
    public void KnownGroupsAreListedEvenWhenTheCaptureFolderIsEmpty()
    {
        string root = Directory.CreateTempSubdirectory("ocr-auto-local-empty-").FullName;
        string config = Path.Combine(root, "配置文件");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "黑色六合彩.json"),
            "{\"group\":\"黑色六合彩\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllText(Path.Combine(config, "嫣然电报.json"),
            "{\"group\":\"嫣然电报\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        File.WriteAllText(Path.Combine(config, "嫣然心水.json"),
            "{\"group\":\"嫣然心水\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        string empty = Path.Combine(root, "抓图空目录");
        Directory.CreateDirectory(empty);

        try
        {
            IReadOnlyList<string> listed = AutoRecognizeForm.BuildGroupList(empty, root, []);
            Assert.Contains("黑色六合彩", listed);
            Assert.Contains("嫣然电报", listed);
            // 所有群一视同仁，嫣然心水也应能被列出。
            Assert.Contains("嫣然心水", listed);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    // 已勾选但配置里已删除的群也要列出来，否则用户无从取消勾选。
    [Fact]
    public void SelectedGroupsSurviveEvenWhenTheirCatalogIsGone()
    {
        string root = Directory.CreateTempSubdirectory("ocr-auto-local-stale-").FullName;
        string config = Path.Combine(root, "配置文件");
        Directory.CreateDirectory(config);
        string empty = Path.Combine(root, "抓图空目录");
        Directory.CreateDirectory(empty);

        try
        {
            IReadOnlyList<string> listed =
                AutoRecognizeForm.BuildGroupList(empty, root, ["已删除的群"]);
            Assert.Contains("已删除的群", listed);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    // 去重必须用归一化名：设置里存的是「黑色六合彩」，规则文件写成「黑色 六合彩」，
    // 按原始字符串比较会列出两行。
    [Fact]
    public void GroupListDeduplicatesByNormalizedName()
    {
        string root = Directory.CreateTempSubdirectory("ocr-auto-local-dedupe-").FullName;
        string config = Path.Combine(root, "配置文件");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "黑色六合彩.json"),
            "{\"group\":\"黑色 六合彩\",\"rules\":[{\"keyword\":\"测试\",\"type\":\"生肖\"}]}");
        string empty = Path.Combine(root, "抓图空目录");
        Directory.CreateDirectory(empty);

        try
        {
            IReadOnlyList<string> listed =
                AutoRecognizeForm.BuildGroupList(empty, root, ["黑色六合彩"]);
            Assert.Single(listed.Where(name =>
                RuleCatalog.NormalizeGroupName(name) == RuleCatalog.NormalizeGroupName("黑色六合彩")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
