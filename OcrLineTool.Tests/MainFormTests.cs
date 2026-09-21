using OcrLineTool;
using System.Reflection;

namespace OcrLineTool.Tests;

public sealed class MainFormTests
{
    [Fact]
    public void ManualRetryRejectsCachedTextThatStillCannotProduceTheRequestedValue()
    {
        var method = typeof(MainForm).GetMethod(
            "CanReuseRetryCloudLines", BindingFlags.Static | BindingFlags.NonPublic)!;
        OcrRule[] rules = [new("金钱网必杀12个特码", "号码:12", "金钱网", StrictIssueBlock: true)];

        Assert.False((bool)method.Invoke(null,
            [@"C:\图片\新澳六合彩资料", rules, new[] { "246期", "01 02 03" }, 246])!);
        Assert.True((bool)method.Invoke(null,
            [@"C:\图片\新澳六合彩资料", rules,
                new[] { "01 02 03 04 05 06 07 08", "246期开??", "09 10 11 12", "245期" }, 246])!);
    }

    [Fact]
    public void ManualRetryUsesTheFixedCropForNewMacauZodiacPosters()
    {
        var method = typeof(MainForm).GetMethod(
            "RetryPrimaryImage", BindingFlags.Static | BindingFlags.NonPublic)!;
        OcrRule[] rules = [new("水哥杀一肖", "生肖", "水哥肖", AllowNearbyValue: true, StrictIssueBlock: true)];

        Assert.Equal("crop.png", method.Invoke(null,
            [@"C:\图片\9.3-新澳六合彩资料", "source.jpg", "crop.png", rules]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickingMissingSummaryWritesAReportWithoutReplacingRecognitionResults(bool failToOpen)
    {
        string directory = ResultFilePaths.GroupResultsDirectory(AppContext.BaseDirectory);
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "统计按钮测试" + Guid.NewGuid().ToString("N") + "_999996期.txt");
        string summary = ResultFilePaths.ForMissingSummary(AppContext.BaseDirectory);
        byte[]? previous = File.Exists(summary) ? File.ReadAllBytes(summary) : null;
        string text = "【尾】\n缺失（未找到对应图片） 按钮测试资料\n3尾 已完成（已分流）";
        var opened = new List<System.Diagnostics.ProcessStartInfo>();
        using var form = CreateUiTestForm(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), info =>
        {
            Assert.Contains("按钮测试资料", File.ReadAllText(info.FileName));
            opened.Add(info);
            if (failToOpen)
                throw new System.ComponentModel.Win32Exception("没有 TXT 关联程序");
        });
        File.WriteAllText(source, text);
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var button = Assert.Single(Descendants(form).OfType<Button>(), item => item.Text == "统计缺失");
            var status = (Label)typeof(MainForm).GetField("statusLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            var results = (TextBox)typeof(MainForm).GetField("resultsBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            results.Text = "保留当前识别结果";
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            button.EnabledChanged += (_, _) =>
            {
                if (button.Enabled && status.Text.StartsWith("统计完成", StringComparison.Ordinal))
                    completed.TrySetResult(true);
            };
            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button, [EventArgs.Empty]);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!completed.Task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Assert.True(completed.Task.IsCompleted, status.Text);

            Assert.Contains("缺失（未找到对应图片） 按钮测试资料", File.ReadAllText(summary));
            Assert.DoesNotContain("3尾 已完成", File.ReadAllText(summary));
            Assert.Equal("保留当前识别结果", results.Text);
            Assert.Equal(text, File.ReadAllText(source));
            Assert.True(button.Enabled);
            Assert.Equal(summary, Assert.Single(opened).FileName);
            Assert.True(opened[0].UseShellExecute);
            Assert.Contains(failToOpen ? "打开 TXT 失败" : "已打开", status.Text);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
            File.Delete(source);
            if (previous is null)
                File.Delete(summary);
            else
                File.WriteAllBytes(summary, previous);
        }
    }

    [Fact]
    public void MissingSummaryButtonIsAvailableWithoutSelectingAGroupAndDisabledWhileBusy()
    {
        using var form = new MainForm();
        Button button = Assert.Single(Descendants(form).OfType<Button>(), item => item.Text == "统计缺失");
        Assert.True(button.Enabled);
        Assert.Equal("统计缺失", button.AccessibleName);
        Assert.True(IsDescendant(Assert.Single(Descendants(form), item => item.Name == "toolsSection"), button));
        typeof(MainForm).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [true]);
        Assert.False(button.Enabled);
        typeof(MainForm).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [false]);
        Assert.True(button.Enabled);
    }

    [Theory]
    [InlineData(1F)]
    [InlineData(1.354167F)]
    public void MissingSummaryAndClearButtonsFitAtMinimumWindowSize(float scale)
    {
        using var form = new MainForm { Size = new Size(1100, 700) };
        Button button = Assert.Single(Descendants(form).OfType<Button>(), item => item.Text == "统计缺失");
        Button clear = Assert.Single(Descendants(form).OfType<Button>(), item => item.Text == "清除");
        form.Scale(new SizeF(scale, scale));
        form.Size = new Size((int)Math.Ceiling(1100 * scale), (int)Math.Ceiling(700 * scale));
        form.CreateControl();
        PerformLayoutRecursively(form);
        AssertControlFitsItsParent(button);
        AssertControlFitsItsParent(clear);
        Assert.False(button.RectangleToScreen(button.ClientRectangle)
            .IntersectsWith(clear.RectangleToScreen(clear.ClientRectangle)));
        Assert.True(button.Width >= 90 * scale);
    }

    [Theory]
    [InlineData("9.2-新澳六合彩资料", false, true)]
    [InlineData("新澳六合彩资料_245期", false, true)]
    [InlineData("新澳六合彩资料", true, false)]
    [InlineData("9.2-新澳高级会员", false, false)]
    [InlineData("新澳六合彩资料备份", false, false)]
    [InlineData("嫣然心水", false, false)]
    public void DefersUnmatchedTemplatesOnlyOnLiuCaiFirstRun(string folder, bool retry, bool expected)
    {
        using var form = new MainForm();
        typeof(MainForm).GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, Path.Combine(@"C:\图片", folder));
        var method = typeof(MainForm).GetMethod("DeferUnmatchedTemplates", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(form, [retry]));
    }

    [Theory]
    [InlineData("9.2-新澳六合彩资料", false, true)]
    [InlineData("9.2-新澳六合彩资料", true, false)]
    [InlineData("9.2-新澳高级会员", false, false)]
    public async Task NoTemplateMatchesWaitForManualRetryOnlyOnLiuCaiFirstRun(string folder, bool retry, bool deferred)
    {
        using var form = new MainForm();
        var context = SynchronizationContext.Current;
        try
        {
            // No UI message loop or images: neither local models nor cloud services are invoked.
            SynchronizationContext.SetSynchronizationContext(null);
            string directory = Path.Combine(@"C:\图片", folder);
            typeof(MainForm).GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(form, directory);
            var rules = RuleCatalog.Load(RuleCatalog.PathForFolder(AppContext.BaseDirectory, directory));
            var ids = rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
            var task = (Task)typeof(MainForm).GetMethod("SelectCandidatesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, [rules, 245, retry, ids, null])!;
            await task;
            object selection = task.GetType().GetProperty("Result")!.GetValue(task)!;
            string name = (string)selection.GetType().GetProperty("DisplayName")!.GetValue(selection)!;
            Assert.Equal(deferred, name.Contains("待手动复抓", StringComparison.Ordinal));
            Assert.Equal(!deferred, name == "本地OCR");
            Assert.Empty((System.Collections.IEnumerable)selection.GetType().GetProperty("Candidates")!.GetValue(selection)!);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    [Fact]
    public void WindowTitleIncludesTheApplicationVersion()
    {
        using var form = new MainForm();

        string version = typeof(MainForm).Assembly.GetName().Version!.ToString(3);
        Assert.Equal($"OCR 整行提取工具 NVIDIA CUDA版 v{version}", form.Text);
    }

    [Fact]
    public void ManualCredentialListIncludesDSlots()
    {
        using var form = new MainForm();
        var selector = (ComboBox)typeof(MainForm)
            .GetField("credentialSelector", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        Assert.Contains("百度 D", selector.Items.Cast<string>());
        Assert.Contains("腾讯 D", selector.Items.Cast<string>());
    }

    [Fact]
    public void HasADisabledRetryMissingButtonUntilAGroupFinishesWithMissingItems()
    {
        using var form = new MainForm();
        var button = (Button)typeof(MainForm)
            .GetField("retryMissingButton", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        Assert.Equal("手动复抓缺失", button.Text);
        Assert.False(button.Enabled);
    }

    [Fact]
    public void HasADisabledManualDistributionButtonUntilASelectedGroupResultExists()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        var button = (Button)type
            .GetField("manualDistributeButton", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        Assert.Equal("手动分流", button.Text);
        Assert.False(button.Enabled);

        const int issue = 999998;
        const string selectedDirectory = @"C:\图片\新澳六合彩资料";
        string groupPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedDirectory, issue);
        Directory.CreateDirectory(Path.GetDirectoryName(groupPath)!);
        File.WriteAllText(groupPath, "测试结果");
        try
        {
            type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(form, selectedDirectory);
            var issueInput = (NumericUpDown)type
                .GetField("issueInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            issueInput.Value = issue;
            type.GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [false]);

            Assert.True(button.Enabled);
        }
        finally
        {
            File.Delete(groupPath);
        }
    }

    [Fact]
    public void EnablesManualRetryWhenAnExistingGroupResultContainsMissingItems()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        const int issue = 999997;
        const string selectedDirectory = @"C:\图片\新澳六合彩资料";
        string groupPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedDirectory, issue);
        Directory.CreateDirectory(Path.GetDirectoryName(groupPath)!);
        File.WriteAllLines(groupPath, ["【一肖】", "缺失（未找到对应图片） 王者肖肖"]);
        try
        {
            type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(form, selectedDirectory);
            type.GetField("selectedRulePath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(form, RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedDirectory));
            var issueInput = (NumericUpDown)type
                .GetField("issueInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            issueInput.Value = issue;
            type.GetMethod("LoadExistingGroupResult", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, null);

            var button = (Button)type
                .GetField("retryMissingButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Assert.True(button.Enabled);
        }
        finally
        {
            File.Delete(groupPath);
        }
    }

    [Fact]
    public void LimitsLocalRetryScanningOnlyForWangzheAndJiugongInTheXinAoLiuCaiGroup()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        var policy = type.GetMethod("LimitedLocalRetryRuleIds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, @"C:\图片\新澳六合彩资料");

        var wangzhe = (IReadOnlySet<string>)policy.Invoke(form,
            [new[] { new OcrRule("王者九点禁一肖", "生肖", "王者肖肖") }])!;
        Assert.Equal(["王者肖肖"], wangzhe);

        var both = (IReadOnlySet<string>)policy.Invoke(form,
            [new[]
            {
                new OcrRule("王者九点禁一肖", "生肖", "王者肖肖"),
                new OcrRule("九宫寻肖", "生肖", "九宫格肖肖")
            }])!;
        Assert.Equal(["九宫格肖肖", "王者肖肖"], both.OrderBy(value => value));

        var other = (IReadOnlySet<string>)policy.Invoke(form,
            [new[] { new OcrRule("包公图", "生肖", "包公肖肖") }])!;
        Assert.Empty(other);

        type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, @"C:\图片\新澳高级会员");
        var otherGroup = (IReadOnlySet<string>)policy.Invoke(form,
            [new[] { new OcrRule("王者九点禁一肖", "生肖", "王者肖肖") }])!;
        Assert.Empty(otherGroup);
    }

    [Fact]
    public void UsesFullImageOnlyWhenWangzheRetryIsLimitedInXinAoLiuCai()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, @"C:\图片\新澳六合彩资料");
        var ratio = type.GetMethod("LocalRetryTitleRatio", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(1.0, ratio.Invoke(form, [new HashSet<string>(["王者肖肖"])])!);
        Assert.Equal(1.0, ratio.Invoke(form, [new HashSet<string>(["九宫格肖肖"])])!);

        type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, @"C:\图片\新澳高级会员");
        Assert.Equal(0.4, ratio.Invoke(form, [new HashSet<string>(["王者肖肖"])])!);
    }

    [Fact]
    public void UsesAThreeColumnWorkspaceForSettingsPreviewAndResults()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        var issue = (NumericUpDown)type.GetField("issueInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var retry = (Button)type.GetField("retryMissingButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var preview = (PictureBox)type.GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var results = (TextBox)type.GetField("resultsBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        TableLayoutPanel workspace = Assert.Single(
            Descendants(form).OfType<TableLayoutPanel>(), candidate =>
                candidate.ColumnCount == 3 &&
                IsDescendant(candidate, issue) &&
                IsDescendant(candidate, retry) &&
                IsDescendant(candidate, preview) &&
                IsDescendant(candidate, results));

        Control laneOne = DirectChildOf(workspace, retry);
        Control laneTwo = DirectChildOf(workspace, issue);
        Control laneThree = DirectChildOf(workspace, results);
        Assert.Same(laneTwo, DirectChildOf(workspace, preview));
        Assert.Equal(0, workspace.GetColumn(laneOne));
        Assert.Equal(1, workspace.GetColumn(laneTwo));
        Assert.Equal(2, workspace.GetColumn(laneThree));
    }

    [Fact]
    public void CentersIssueDigitsInsideTheSharedInputFrame()
    {
        using var form = new MainForm();
        var issue = (NumericUpDown)typeof(MainForm)
            .GetField("issueInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var folder = (Label)typeof(MainForm)
            .GetField("folderNameLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        form.Show();
        PerformLayoutRecursively(form);

        TextBox edit = Assert.Single(Descendants(issue).OfType<TextBox>());
        Assert.True(issue.Top > 1, $"Issue input should be vertically centered, but was {issue.Bounds}.");
        Assert.Equal(issue.Parent!.Padding.Left, issue.Left);
        int expectedTop = (issue.ClientSize.Height - edit.Height) / 2;
        Assert.InRange(Math.Abs(edit.Top - expectedTop), 0, 1);
        Assert.Equal(issue.Parent.Bounds.Left, folder.Parent!.Bounds.Left);
        Assert.Equal(issue.Parent.Bounds.Width, folder.Parent.Bounds.Width);
    }

    [Fact]
    public void UsesDarkSurfacesWithReadableResultText()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        var preview = (PictureBox)type.GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var results = (TextBox)type.GetField("resultsBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        Assert.True(IsDark(form.BackColor), $"Form background should be dark, but was {form.BackColor}.");
        Assert.True(IsDark(preview.BackColor), $"Preview background should be dark, but was {preview.BackColor}.");
        Assert.True(IsDark(results.BackColor), $"Results background should be dark, but was {results.BackColor}.");
        Assert.True(
            results.ForeColor.GetBrightness() >= 0.65F,
            $"Results text should be light on the dark surface, but was {results.ForeColor}.");
    }

    [Fact]
    public void GroupsSettingsToolsAndResultActionsByPurpose()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        var issue = (NumericUpDown)type.GetField("issueInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var credential = (ComboBox)type.GetField("credentialSelector", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var folders = (ListBox)type.GetField("folderList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var recognize = (Button)type.GetField("recognizeButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var retry = (Button)type.GetField("retryMissingButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var open = (Button)type.GetField("openGroupResultsButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var clear = (Button)type.GetField("clearResultsButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var results = (TextBox)type.GetField("resultsBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        Control settingsSection = Assert.Single(Descendants(form), item => item.Name == "settingsSection");
        Control toolsSection = Assert.Single(Descendants(form), item => item.Name == "toolsSection");
        Control resultsSection = Assert.Single(Descendants(form), item => item.Name == "resultsSection");
        Control sidebar = Assert.Single(Descendants(form), item => item.Name == "sidebarLayout");

        Assert.True(IsDescendant(settingsSection, issue));
        Assert.True(IsDescendant(settingsSection, credential));
        Assert.True(IsDescendant(settingsSection, recognize));
        Assert.True(IsDescendant(toolsSection, retry));
        Assert.True(IsDescendant(resultsSection, open));
        Assert.True(IsDescendant(resultsSection, results));
        Assert.True(IsDescendant(resultsSection, clear));
        Assert.True(IsDescendant(sidebar, folders));
        Assert.False(IsDescendant(toolsSection, clear));
        Assert.False(IsDescendant(settingsSection, folders));
        Assert.NotSame(settingsSection, toolsSection);
        Assert.NotSame(settingsSection, resultsSection);
        Assert.NotSame(toolsSection, resultsSection);
        Assert.Equal("打开群结果", open.Text);
        Assert.Equal("清除", clear.Text);
    }

    [Fact]
    public void ShowsASelectableFolderListWithoutTheRemovedChooseButton()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        var recognize = (Button)type.GetField("recognizeButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var folders = (ListBox)type.GetField("folderList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        form.Show();
        Assert.True(folders.Visible);
        Assert.DoesNotContain(Descendants(form).OfType<Button>(), button => button.Text == "选择子文件夹");
        var laneOne = Assert.IsType<TableLayoutPanel>(
            Assert.Single(Descendants(form), item => item.Name == "sidebarLayout"));
        Assert.Same(laneOne, folders.Parent);
        Control settingsSection = Assert.Single(Descendants(form), item => item.Name == "settingsSection");
        Assert.False(IsDescendant(settingsSection, folders));
    }

    [Fact]
    public void ProvidesSeparateLocalPrimaryRecognitionButtonWithoutReplacingCurrentMode()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        var current = (Button)type.GetField("recognizeButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var localPrimary = (Button)type.GetField("localPrimaryButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        Assert.Equal("开始识别", current.Text);
        Assert.Equal("本地主识别", localPrimary.Text);
        Assert.NotSame(current, localPrimary);
        Assert.False(current.Enabled);
        Assert.False(localPrimary.Enabled);
        Assert.Equal("本地主识别", localPrimary.AccessibleName);
        Assert.Same(current.Parent, localPrimary.Parent);
        Assert.True(IsDescendant(
            Assert.Single(Descendants(form), item => item.Name == "settingsSection"), localPrimary));

        typeof(MainForm).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [true]);
        Assert.False(localPrimary.Enabled);
        typeof(MainForm).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [false]);
        Assert.False(localPrimary.Enabled);
    }

    [Fact]
    public void LocalPrimaryRecognitionPerformsCudaPreflight()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "OcrLineTool.App", "MainForm.cs"));

        Assert.Contains("EnsureCudaAvailableAsync", source);
    }

    [Theory]
    [InlineData(1F)]
    [InlineData(1.354167F)]
    public void ShowsRecognitionTimingBeforeTheResultButtonsAndUpdatesFromSharedProgress(float scale)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new MainForm { Size = new Size(1100, 700) };
        var timing = (Label)typeof(MainForm).GetField("recognitionTimingLabel", flags)!.GetValue(form)!;
        var open = (Button)typeof(MainForm).GetField("openGroupResultsButton", flags)!.GetValue(form)!;
        var header = Assert.IsType<TableLayoutPanel>(timing.Parent);

        Assert.Same(header, open.Parent);
        Assert.True(header.GetColumn(timing) < header.GetColumn(open));
        Assert.Equal("识别耗时和预计剩余时间", timing.AccessibleName);

        form.Scale(new SizeF(scale, scale));
        form.Size = new Size((int)Math.Ceiling(1100 * scale), (int)Math.Ceiling(700 * scale));
        form.Show();
        typeof(MainForm).GetMethod("StartRecognitionTiming", flags)!.Invoke(form, null);
        typeof(MainForm).GetMethod("SetProgress", flags)!.Invoke(form, [1, 4]);
        typeof(MainForm).GetMethod("UpdateRecognitionTiming", flags)!.Invoke(form, null);
        PerformLayoutRecursively(form);

        Assert.True(timing.Visible);
        Assert.Contains("已用", timing.Text);
        Assert.Contains("预计剩余", timing.Text);
        AssertControlFitsItsParent(timing);
        AssertControlFitsItsParent(open);
        Assert.False(timing.Bounds.IntersectsWith(open.Bounds));

        typeof(MainForm).GetMethod("StopRecognitionTiming", flags)!.Invoke(form, null);
        Assert.StartsWith("总耗时", timing.Text);
    }

    [Fact]
    public void ClickingAVisibleFolderNameSelectsThatDirectory()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var form = new MainForm();
            Type type = typeof(MainForm);
            var folders = (ListBox)type.GetField("folderList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            folders.Items.Clear();
            folders.Items.Add(new DirectoryInfo(directory));
            folders.SelectedIndex = 0;

            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(folders, [EventArgs.Empty]);

            Assert.Equal(
                directory,
                type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void SizesTheToolsCardAndKeepsTheSidebarScrollable()
    {
        using var form = new MainForm();
        var sidebar = Assert.Single(
            Descendants(form).OfType<TableLayoutPanel>(),
            candidate => candidate.Name == "sidebarLayout");

        Assert.Equal(SizeType.Absolute, sidebar.RowStyles[0].SizeType);
        Assert.Equal(SizeType.Percent, sidebar.RowStyles[1].SizeType);
        var host = Assert.Single(Descendants(form), control => control.Name == "sidebarScrollHost");
        Assert.True(host is Panel { AutoScroll: true });
    }

    [Fact]
    public void AlignsTheToolColumnBottomWithTheLocalPrimaryButton()
    {
        using var form = CreateUiTestForm(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), _ => { });
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.Size = new Size(1400, 850);
        form.CreateControl();
        form.Show();
        PerformLayoutRecursively(form);

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var manual = (Button)typeof(MainForm).GetField("manualDistributeButton", flags)!.GetValue(form)!;
        var localPrimary = (Button)typeof(MainForm).GetField("localPrimaryButton", flags)!.GetValue(form)!;

        int manualBottom = manual.RectangleToScreen(manual.ClientRectangle).Bottom;
        int primaryBottom = localPrimary.RectangleToScreen(localPrimary.ClientRectangle).Bottom;
        Assert.Equal(primaryBottom, manualBottom);
    }

    [Theory]
    [InlineData(1400, 850, 1F, false)]
    [InlineData(1100, 700, 1F, true)]
    [InlineData(1100, 700, 1.354167F, true)]
    [InlineData(1100, 700, 1.5F, false)]
    public void GivesButtonsComfortableClickTargetsAndGaps(int width, int height, float scale, bool showCloud)
    {
        using var form = CreateUiTestForm(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), _ => { });
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.ApplyShowRecognizeButton(showCloud);
        form.Scale(new SizeF(scale, scale));
        form.Size = new Size((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
        form.CreateControl();
        form.Show();
        PerformLayoutRecursively(form);
        // Exercise the settings toggle after DPI scaling, too.
        form.ApplyShowRecognizeButton(!showCloud);
        form.ApplyShowRecognizeButton(showCloud);
        PerformLayoutRecursively(form);

        Button[] buttons = Descendants(form).OfType<Button>().Where(button => button.Visible).ToArray();
        foreach (Button button in buttons)
        {
            Assert.True(button.Height >= 40, $"{button.Text} height={button.Height}");
            AssertControlFitsItsParent(button);
        }
        Button[] toolButtons = Descendants(form).OfType<Button>()
            .Where(button => button.Visible && IsDescendant(Assert.Single(Descendants(form), control => control.Name == "toolsSection"), button))
            .ToArray();
        for (int first = 0; first < toolButtons.Length; first++)
        for (int second = first + 1; second < toolButtons.Length; second++)
        {
            Rectangle a = toolButtons[first].RectangleToScreen(toolButtons[first].ClientRectangle);
            Rectangle b = toolButtons[second].RectangleToScreen(toolButtons[second].ClientRectangle);
            int gap = (int)Math.Floor(10 * scale);
            Assert.True(a.Right + gap <= b.Left || b.Right + gap <= a.Left ||
                a.Bottom + gap <= b.Top || b.Bottom + gap <= a.Top,
                $"{toolButtons[first].Text} {a} and {toolButtons[second].Text} {b} are too close.");
        }

        Button clear = Assert.Single(buttons, button => button.Text == "清除");
        Control resultsSection = Assert.Single(Descendants(form), control => control.Name == "resultsSection");
        Assert.True(IsDescendant(resultsSection, clear));
        AssertControlFitsItsParent(clear);

        var folders = Assert.Single(Descendants(form).OfType<ListBox>());
        AssertControlFitsItsParent(folders);
        var sidebarScroll = Assert.IsAssignableFrom<Panel>(Assert.Single(Descendants(form), control => control.Name == "sidebarScrollHost"));
        Assert.True(sidebarScroll.AutoScroll);
        sidebarScroll.ScrollControlIntoView(folders);
        Assert.True(
            sidebarScroll.RectangleToScreen(sidebarScroll.ClientRectangle)
                .IntersectsWith(folders.RectangleToScreen(folders.ClientRectangle)),
            "The group list must be reachable through the lane scroll host.");
        Assert.True(folders.Height >= 80, $"The group list must stay usable, but was {folders.Height} px.");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var resume = (Button)typeof(MainForm).GetField("continueButton", flags)!.GetValue(form)!;
        var manual = (Button)typeof(MainForm).GetField("manualDistributeButton", flags)!.GetValue(form)!;
        resume.Visible = true;
        resume.BringToFront();
        PerformLayoutRecursively(form);
        Assert.Equal(manual.RectangleToScreen(manual.ClientRectangle), resume.RectangleToScreen(resume.ClientRectangle));
        AssertControlFitsItsParent(resume);
        resume.Visible = false;

    }

    [Theory]
    [InlineData(1F)]
    [InlineData(1.354167F)]
    public void KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(float scale)
    {
        using var form = new MainForm { Size = new Size(1100, 700) };
        Type type = typeof(MainForm);
        var folders = (ListBox)type.GetField("folderList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var continueButton = (Button)type.GetField("continueButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        form.Scale(new SizeF(scale, scale));
        form.Size = new Size((int)Math.Ceiling(1100 * scale), (int)Math.Ceiling(700 * scale));
        form.CreateControl();
        form.Show();
        continueButton.Visible = true;
        PerformLayoutRecursively(form);
        Assert.True(form.Height >= (int)Math.Ceiling(700 * scale),
            $"The viewport must retain the requested scale. Actual={form.Size}; DesktopLimit={SystemInformation.MaxWindowTrackSize}.");

        AssertControlFitsItsParent(folders);
        AssertControlFitsItsParent(continueButton);
    }

    [Fact]
    public void KeepsLongStatusMessagesOnOneWideEllipsizedLine()
    {
        using var form = new MainForm { Size = new Size(1100, 700) };
        var status = (Label)typeof(MainForm)
            .GetField("statusLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        status.Text = "完成：目录 169 张，实际进云 101 张，云 OCR 请求 202 次；群结果目录：C:\\Users\\Administrator\\Desktop\\每天工具\\飞机抓到的分类\\outputs\\重要结果\\群结果";

        form.CreateControl();
        PerformLayoutRecursively(form);

        var statusRow = Assert.IsType<TableLayoutPanel>(status.Parent);
        Assert.Equal("SingleLineEllipsisLabel", status.GetType().Name);
        Assert.True(status.Width >= statusRow.ClientSize.Width * 0.30, $"Status width {status.Width} is too narrow for {statusRow.ClientSize.Width}.");
        foreach (Control control in Descendants(statusRow))
            AssertControlFitsItsParent(control);
    }

    [Fact]
    public void KeepsTheStatusBarInsideItsCardAtTheCurrent135PercentDisplayScale()
    {
        const float scale = 130F / 96F;
        using var form = new MainForm { Size = new Size(1100, 700) };
        var status = (Label)typeof(MainForm)
            .GetField("statusLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        status.Text = "已打开群结果目录：C:\\Users\\Administrator\\Desktop\\每天工具\\飞机抓到的分类\\outputs\\重要结果\\群结果";

        form.Scale(new SizeF(scale, scale));
        form.Size = new Size((int)Math.Ceiling(1100 * scale), (int)Math.Ceiling(700 * scale));
        form.CreateControl();
        PerformLayoutRecursively(form);

        var statusRow = Assert.IsType<TableLayoutPanel>(status.Parent);
        foreach (Control control in Descendants(statusRow))
            AssertControlFitsItsParent(control);
    }

    [Fact]
    public void EnablesManualRetryForMissingResultsFromAnyGroupAtTheCurrentIssue()
    {
        using var form = new MainForm();
        Type type = typeof(MainForm);
        type.GetField("selectedImageDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, @"C:\图片\新澳六合彩资料");
        type.GetField("lastRules", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, new OcrRule[] { new("辣椒炒肉", "生肖") });
        type.GetField("lastIssue", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, 242);
        var issueInput = (NumericUpDown)type
            .GetField("issueInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        issueInput.Value = 242;
        type.GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [false]);
        var button = (Button)type
            .GetField("retryMissingButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        Assert.True(button.Enabled);

        issueInput.Value = 243;
        Assert.False(button.Enabled);
    }

    [Fact]
    public void OpensOnlyTheSelectedGroupsCurrentIssueTextAndNeverCreatesMissingResults()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var opened = new List<System.Diagnostics.ProcessStartInfo>();
        using var form = CreateUiTestForm(Path.GetTempPath(), opened.Add);
        var button = Assert.Single(Descendants(form).OfType<Button>(), item => item.Text == "打开群结果");
        var status = (Label)typeof(MainForm).GetField("statusLabel", flags)!.GetValue(form)!;
        var input = (NumericUpDown)typeof(MainForm).GetField("issueInput", flags)!.GetValue(form)!;
        typeof(Control).GetMethod("OnClick", flags)!.Invoke(button, [EventArgs.Empty]);
        Assert.Empty(opened);
        Assert.Contains("先选择", status.Text);

        string selected = @"C:\图片\9.2-打开测试" + Guid.NewGuid().ToString("N");
        string first = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selected, 999993);
        string second = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selected, 999994);
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        File.WriteAllText(first, "第一期结果");
        File.WriteAllText(second, "第二期结果");
        try
        {
            typeof(MainForm).GetField("selectedImageDirectory", flags)!.SetValue(form, selected);
            input.Value = 999993;
            typeof(Control).GetMethod("OnClick", flags)!.Invoke(button, [EventArgs.Empty]);
            input.Value = 999994;
            typeof(Control).GetMethod("OnClick", flags)!.Invoke(button, [EventArgs.Empty]);
            Assert.Equal(new[] { first, second }, opened.Select(info => info.FileName));
            Assert.All(opened, info => Assert.True(info.UseShellExecute));
            input.Value = 999995;
            typeof(Control).GetMethod("OnClick", flags)!.Invoke(button, [EventArgs.Empty]);
            Assert.Equal(2, opened.Count);
            Assert.Contains("未找到", status.Text);
            Assert.False(File.Exists(ResultFilePaths.ForGroup(AppContext.BaseDirectory, selected, 999995)));
            Assert.Equal("第一期结果", File.ReadAllText(first));
            Assert.Equal("第二期结果", File.ReadAllText(second));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void AutomaticFolderRefreshPreservesSelectionResultsAndBusyStateWithoutScanningImages()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string root = Directory.CreateTempSubdirectory("ocr-folder-refresh-").FullName;
        try
        {
            string selected = Directory.CreateDirectory(Path.Combine(root, "甲群")).FullName;
            using var form = CreateUiTestForm(root, _ => throw new InvalidOperationException("Must not launch files."));
            var folders = (ListBox)typeof(MainForm).GetField("folderList", flags)!.GetValue(form)!;
            folders.SelectedIndex = 0;
            typeof(Control).GetMethod("OnClick", flags)!.Invoke(folders, [EventArgs.Empty]);
            object paths = typeof(MainForm).GetField("imagePaths", flags)!.GetValue(form)!;
            var results = (TextBox)typeof(MainForm).GetField("resultsBox", flags)!.GetValue(form)!;
            results.Text = "当前结果不改变";
            typeof(MainForm).GetMethod("SetBusy", flags)!.Invoke(form, [true]);
            var timerField = typeof(MainForm).GetField("folderRefreshTimer", flags);
            Assert.NotNull(timerField);
            var timer = (System.Windows.Forms.Timer)timerField.GetValue(form)!;
            Assert.True(timer.Enabled);
            Assert.InRange(timer.Interval, 500, 2000);

            Directory.CreateDirectory(Path.Combine(root, "00新群"));
            Directory.CreateDirectory(Path.Combine(selected, "不是群的子目录"));
            File.WriteAllText(Path.Combine(selected, "new.jpg"), "not an image");
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Equal(new[] { "00新群", "甲群" }, folders.Items.Cast<DirectoryInfo>().Select(info => info.Name));
            Assert.Equal(selected, ((DirectoryInfo)folders.SelectedItem!).FullName);
            Assert.Equal(selected, typeof(MainForm).GetField("selectedImageDirectory", flags)!.GetValue(form));
            Assert.Same(paths, typeof(MainForm).GetField("imagePaths", flags)!.GetValue(form));
            Assert.Equal("当前结果不改变", results.Text);
            Assert.False(folders.Enabled);
            object unchanged = folders.Items[0];
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Same(unchanged, folders.Items[0]);
            form.Dispose();
            Assert.False(timer.Enabled);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AutomaticFolderRefreshRecoversWhenRootAppearsWithoutSelectingANewGroup()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string temp = Directory.CreateTempSubdirectory("ocr-folder-appear-").FullName;
        try
        {
            string root = Path.Combine(temp, "结果");
            using var form = CreateUiTestForm(root, _ => { });
            var folders = (ListBox)typeof(MainForm).GetField("folderList", flags)!.GetValue(form)!;
            Assert.Empty(folders.Items);
            string created = Directory.CreateDirectory(Path.Combine(root, "新群")).FullName;
            var timer = typeof(MainForm).GetField("folderRefreshTimer", flags)!.GetValue(form)!;
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Single(folders.Items);
            Assert.Null(folders.SelectedItem);
            Assert.Null(typeof(MainForm).GetField("selectedImageDirectory", flags)!.GetValue(form));
            Directory.Move(created, Path.Combine(root, "改名群"));
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Equal("改名群", Assert.IsType<DirectoryInfo>(folders.Items[0]).Name);
            Directory.Delete(Path.Combine(root, "改名群"));
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Empty(folders.Items);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task FailedSummaryGenerationDoesNotOpenThePreviousSummary()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var opened = new List<System.Diagnostics.ProcessStartInfo>();
        using var form = CreateUiTestForm(Path.GetTempPath(), opened.Add);
        string summary = ResultFilePaths.ForMissingSummary(AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(summary)!);
        byte[]? previous = File.Exists(summary) ? File.ReadAllBytes(summary) : null;
        string source = Path.Combine(Path.GetDirectoryName(summary)!, "文件占用测试" + Guid.NewGuid().ToString("N") + "_999992期.txt");
        File.WriteAllText(summary, "不可误开的旧汇总");
        File.WriteAllText(source, "【尾】\n缺失 测试");
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            using (FileStream locked = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var method = typeof(MainForm).GetMethod("GenerateAndOpenMissingSummaryAsync", flags);
                Assert.NotNull(method);
                await Assert.ThrowsAsync<IOException>(() => (Task)method.Invoke(form, null)!);
            }
            Assert.Empty(opened);
            Assert.Equal("不可误开的旧汇总", File.ReadAllText(summary));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
            File.Delete(source);
            if (previous is null) File.Delete(summary);
            else File.WriteAllBytes(summary, previous);
        }
    }

    [Fact]
    public void AutomaticFolderRefreshKeepsTheOldListDuringATransientRootFailure()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string temp = Directory.CreateTempSubdirectory("ocr-folder-offline-").FullName;
        try
        {
            string root = Path.Combine(temp, "结果");
            string offline = Path.Combine(temp, "暂不可用");
            Directory.CreateDirectory(Path.Combine(root, "原群"));
            using var form = CreateUiTestForm(root, _ => { });
            var folders = (ListBox)typeof(MainForm).GetField("folderList", flags)!.GetValue(form)!;
            var timer = typeof(MainForm).GetField("folderRefreshTimer", flags)!.GetValue(form)!;
            var status = (Label)typeof(MainForm).GetField("statusLabel", flags)!.GetValue(form)!;
            status.Text = "正在识别，不打断";
            object originalItem = folders.Items[0];
            Directory.Move(root, offline);
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Same(originalItem, Assert.Single(folders.Items.Cast<object>()));
            Assert.Equal("正在识别，不打断", status.Text);
            Directory.Move(offline, root);
            Directory.CreateDirectory(Path.Combine(root, "新群"));
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Equal(2, folders.Items.Count);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ManualRetryRescansTheSelectedGroupForNewImages()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string root = Directory.CreateTempSubdirectory("ocr-retry-rescan-").FullName;
        try
        {
            string group = Directory.CreateDirectory(Path.Combine(root, "测试群")).FullName;
            string existing = Path.Combine(group, "existing.jpg");
            File.WriteAllText(existing, "old");
            using var form = CreateUiTestForm(root, _ => { });
            typeof(MainForm).GetField("selectedImageDirectory", flags)!.SetValue(form, group);
            typeof(MainForm).GetField("imagePaths", flags)!.SetValue(form, new[] { existing });

            string added = Path.Combine(group, "新增资料", "new.png");
            Directory.CreateDirectory(Path.GetDirectoryName(added)!);
            File.WriteAllText(added, "new");

            var refresh = typeof(MainForm).GetMethod("RefreshImagesForRetry", flags);
            Assert.NotNull(refresh);
            refresh.Invoke(form, null);

            string[] paths = (string[])typeof(MainForm).GetField("imagePaths", flags)!.GetValue(form)!;
            Assert.Equal(new[] { existing, added }, paths);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MarksGroupsThatAlreadyHaveTheCurrentIssueResultAndRefreshesAutomatically()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string root = Directory.CreateTempSubdirectory("ocr-group-marker-").FullName;
        string? resultPath = null;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "甲群")).FullName;
            using var form = CreateUiTestForm(root, _ => { });
            var folders = (ListBox)typeof(MainForm).GetField("folderList", flags)!.GetValue(form)!;
            var issueInput = (NumericUpDown)typeof(MainForm).GetField("issueInput", flags)!.GetValue(form)!;
            var markers = (HashSet<string>)typeof(MainForm)
                .GetField("foldersWithCurrentIssueResult", flags)!.GetValue(form)!;

            Assert.Equal(DrawMode.OwnerDrawFixed, folders.DrawMode);

            const int issue = 999995;
            issueInput.Value = issue;
            resultPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, folder, issue);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            File.WriteAllText(resultPath, "测试结果");

            var timer = (System.Windows.Forms.Timer)typeof(MainForm).GetField("folderRefreshTimer", flags)!.GetValue(form)!;
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Contains(folder, markers);

            File.Delete(resultPath);
            resultPath = null;
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(timer, [EventArgs.Empty]);
            Assert.Empty(markers);
        }
        finally
        {
            if (resultPath is not null)
                File.Delete(resultPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreservesExistingDistributedMarkersWhenRewritingGroupResult()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-marker-").FullName;
        try
        {
            string path = Path.Combine(directory, "甲群_254期.txt");
            File.WriteAllLines(path, ["【头】", "0头 齐天大圣（已分流）", "缺失（未找到对应图片） 王者肖肖"]);
            string[] output = MainForm.PreserveDistributedMarkers(path,
                ["0头 齐天大圣", "4头 辣椒炒肉", "缺失（未找到对应图片） 王者肖肖"]);

            Assert.Equal(
                ["0头 齐天大圣（已分流）", "4头 辣椒炒肉", "缺失（未找到对应图片） 王者肖肖"],
                output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SettingsFileRoundTripsAndDefaultsToHidden()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-ui-settings-").FullName;
        try
        {
            Assert.False(UiSettings.Load(directory).ShowRecognizeButton);
            Assert.Equal(LocalOcrDevice.Gpu, UiSettings.Load(directory).OcrDevice);

            UiSettings.Save(directory, new UiSettings(true));
            Assert.True(UiSettings.Load(directory).ShowRecognizeButton);
            Assert.Equal(LocalOcrDevice.Gpu, UiSettings.Load(directory).OcrDevice);

            UiSettings.Save(directory, new UiSettings(false, true, LocalOcrDevice.Cpu));
            Assert.Equal(LocalOcrDevice.Cpu, UiSettings.Load(directory).OcrDevice);

            UiSettings.Save(directory, new UiSettings(false));
            Assert.False(UiSettings.Load(directory).ShowRecognizeButton);
            Assert.Equal(LocalOcrDevice.Gpu, UiSettings.Load(directory).OcrDevice);

            File.WriteAllText(UiSettings.PathFor(directory), "not json");
            Assert.False(UiSettings.Load(directory).ShowRecognizeButton);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void HidesTheRecognizeButtonUnlessTheSettingEnablesIt()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string settingsPath = UiSettings.PathFor(AppContext.BaseDirectory);
        bool existed = File.Exists(settingsPath);
        string? backup = existed ? File.ReadAllText(settingsPath) : null;
        string root = Directory.CreateTempSubdirectory("ocr-ui-visibility-").FullName;
        try
        {
            File.Delete(settingsPath);
            using (var form = CreateUiTestForm(root, _ => { }))
            {
                var recognize = (Button)typeof(MainForm).GetField("recognizeButton", flags)!.GetValue(form)!;
                form.Show();
                Assert.False(recognize.Visible);
                form.ApplyShowRecognizeButton(true);
                Assert.True(recognize.Visible);
                form.ApplyShowRecognizeButton(false);
                Assert.False(recognize.Visible);
            }

            UiSettings.Save(AppContext.BaseDirectory, new UiSettings(true));
            using (var form = CreateUiTestForm(root, _ => { }))
            {
                var recognize = (Button)typeof(MainForm).GetField("recognizeButton", flags)!.GetValue(form)!;
                form.Show();
                Assert.True(recognize.Visible);
            }
        }
        finally
        {
            if (existed)
                File.WriteAllText(settingsPath, backup!);
            else
                File.Delete(settingsPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SettingsDialogReturnsTheChosenVisibility()
    {
        using var dialog = new SettingsForm(new UiSettings(false));
        CheckBox box = Assert.Single(Descendants(dialog).OfType<CheckBox>());
        Assert.False(box.Checked);

        box.Checked = true;
        Button save = Assert.Single(Descendants(dialog).OfType<Button>(), button => button.Text == "保存");
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(save, [EventArgs.Empty]);

        Assert.Equal(DialogResult.OK, dialog.DialogResult);
        Assert.True(dialog.Result.ShowRecognizeButton);
    }

    [Fact]
    public void KeepsGroupListRowsComfortablySpaced()
    {
        using var form = new MainForm();
        var folders = (ListBox)typeof(MainForm).GetField("folderList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        Assert.True(
            folders.ItemHeight >= folders.Font.Height + 8,
            $"Group list item height {folders.ItemHeight} should exceed font height {folders.Font.Height}.");
    }

    [Fact]
    public void SettingsDialogReturnsTheChosenRetryOcrSource()
    {
        using var dialog = new SettingsForm(new UiSettings(false));
        RadioButton[] radios = Descendants(dialog).OfType<RadioButton>().ToArray();
        Assert.Equal(4, radios.Length);
        Assert.True(Assert.Single(radios, radio => radio.Name == "retryUsesLocalOcr").Checked);
        Assert.False(Assert.Single(radios, radio => radio.Name == "retryUsesCloudOcr").Checked);

        Assert.Single(radios, radio => radio.Name == "retryUsesCloudOcr").Checked = true;
        Button save = Assert.Single(Descendants(dialog).OfType<Button>(), button => button.Text == "保存");
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(save, [EventArgs.Empty]);

        Assert.Equal(DialogResult.OK, dialog.DialogResult);
        Assert.False(dialog.Result.RetryUsesLocalOcr);
    }

    [Fact]
    public void SettingsDialogReturnsTheChosenLocalOcrDevice()
    {
        using var dialog = new SettingsForm(new UiSettings(false));
        RadioButton[] radios = Descendants(dialog).OfType<RadioButton>().ToArray();
        Assert.True(Assert.Single(radios, radio => radio.Name == "localOcrGpu").Checked);
        Assert.False(Assert.Single(radios, radio => radio.Name == "localOcrCpu").Checked);

        Assert.Single(radios, radio => radio.Name == "localOcrCpu").Checked = true;
        Button save = Assert.Single(Descendants(dialog).OfType<Button>(), button => button.Text == "保存");
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(save, [EventArgs.Empty]);

        Assert.Equal(DialogResult.OK, dialog.DialogResult);
        Assert.Equal(LocalOcrDevice.Cpu, dialog.Result.OcrDevice);
    }

    [Fact]
    public void SettingsDialogHasRoomForAllOptionsAndActionButtons()
    {
        using var dialog = new SettingsForm(new UiSettings(false));
        dialog.Show();
        try
        {
            dialog.PerformLayout();

            Assert.True(dialog.ClientSize.Width >= 520, $"Settings width {dialog.ClientSize.Width} is too narrow for the option text.");
            Assert.True(dialog.ClientSize.Height >= 340, $"Settings height {dialog.ClientSize.Height} is too short for the hint and buttons.");

            RadioButton[] radios = Descendants(dialog).OfType<RadioButton>().ToArray();
            Assert.Equal(4, radios.Length);
            Assert.All(radios, radio =>
            {
                Assert.True(radio.Visible && radio.Enabled, $"{radio.Name} must be visible and enabled.");
                Assert.True(radio.Right <= radio.Parent!.ClientSize.Width, $"{radio.Name} text is clipped by its host.");
                Assert.True(radio.Bottom <= radio.Parent!.ClientSize.Height, $"{radio.Name} is clipped vertically.");
            });

            Button[] buttons = Descendants(dialog).OfType<Button>().Where(button => button.Text is "保存" or "取消").ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.All(buttons, button =>
            {
                Assert.True(button.Visible && button.Enabled, $"{button.Text} must be visible and enabled.");
                Assert.True(button.Width >= 96 && button.Height >= 34, $"{button.Text} click target is too small.");
                Assert.True(button.Right <= button.Parent!.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height,
                    $"{button.Text} is outside its host.");
            });
        }
        finally
        {
            dialog.Close();
        }
    }

    [Fact]
    public void SettingsDialogOptionsDoNotOverlapEachOther()
    {
        using var dialog = new SettingsForm(new UiSettings(false));
        dialog.Show();
        try
        {
            dialog.PerformLayout();

            foreach (Control host in Descendants(dialog).Where(control => control is Panel && control.Controls.OfType<RadioButton>().Any()))
            {
                RadioButton[] controls = host.Controls.OfType<RadioButton>().ToArray();
                for (int index = 0; index < controls.Length; index++)
                    for (int other = index + 1; other < controls.Length; other++)
                        Assert.False(controls[index].Bounds.IntersectsWith(controls[other].Bounds),
                            $"{controls[index].Name} overlaps {controls[other].Name}.");
            }
        }
        finally
        {
            dialog.Close();
        }
    }

    [Fact]
    public void SettingsDialogRadioAndSaveButtonsRespondToClicks()
    {
        using var dialog = new SettingsForm(new UiSettings(false));
        dialog.Show();
        try
        {
            RadioButton cpu = Assert.Single(Descendants(dialog).OfType<RadioButton>(), radio => radio.Name == "localOcrCpu");
            Button save = Assert.Single(Descendants(dialog).OfType<Button>(), button => button.Text == "保存");

            cpu.PerformClick();
            Assert.True(cpu.Checked);
            save.PerformClick();

            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.Equal(LocalOcrDevice.Cpu, dialog.Result.OcrDevice);
        }
        finally
        {
            if (!dialog.IsDisposed)
                dialog.Close();
        }
    }

    [Fact]
    public void ClearingResultsRefreshesTheResultBoxFromDisk()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string root = Directory.CreateTempSubdirectory("ocr-clear-refresh-").FullName;
        try
        {
            using var form = CreateUiTestForm(root, _ => { });
            var results = (TextBox)typeof(MainForm).GetField("resultsBox", flags)!.GetValue(form)!;
            results.Text = "【头】" + Environment.NewLine + "0头 齐天大圣";
            typeof(MainForm).GetField("selectedImageDirectory", flags)!.SetValue(form, root);

            form.RefreshResultsAfterClear();

            Assert.Equal(string.Empty, results.Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RetryOcrSourceDefaultsToLocalFirstAndRoundTrips()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-retry-settings-").FullName;
        try
        {
            Assert.True(UiSettings.Load(directory).RetryUsesLocalOcr);

            File.WriteAllText(
                UiSettings.PathFor(directory),
                "{\"ShowRecognizeButton\":true}");
            UiSettings legacy = UiSettings.Load(directory);
            Assert.True(legacy.ShowRecognizeButton);
            Assert.True(legacy.RetryUsesLocalOcr);

            UiSettings.Save(directory, new UiSettings(true, RetryUsesLocalOcr: false));
            UiSettings saved = UiSettings.Load(directory);
            Assert.True(saved.ShowRecognizeButton);
            Assert.False(saved.RetryUsesLocalOcr);

            UiSettings.Save(directory, new UiSettings(false));
            Assert.True(UiSettings.Load(directory).RetryUsesLocalOcr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MainForm CreateUiTestForm(string root, Action<System.Diagnostics.ProcessStartInfo> openFile)
    {
        var constructor = typeof(MainForm).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(string), typeof(Action<System.Diagnostics.ProcessStartInfo>)], null);
        Assert.NotNull(constructor);
        return (MainForm)constructor.Invoke([root, openFile]);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static bool IsDescendant(Control ancestor, Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static Control DirectChildOf(Control ancestor, Control descendant)
    {
        Control current = descendant;
        while (current.Parent is not null && !ReferenceEquals(current.Parent, ancestor))
            current = current.Parent;

        Assert.Same(ancestor, current.Parent);
        return current;
    }

    private static Control FindNamedSection(Control root, string heading, params Control[] members)
    {
        Control headingControl = Assert.Single(
            Descendants(root),
            control => (control is Label || control is GroupBox) && control.Text == heading);

        for (Control? section = headingControl; section is not null && !ReferenceEquals(section, root); section = section.Parent)
        {
            if (members.All(member => IsDescendant(section, member)))
                return section;
        }

        throw new Xunit.Sdk.XunitException($"Could not find the '{heading}' section containing its expected controls.");
    }

    private static bool IsDark(Color color) => color.GetBrightness() <= 0.35F;

    private static void PerformLayoutRecursively(Control root)
    {
        root.PerformLayout();
        foreach (Control child in root.Controls)
            PerformLayoutRecursively(child);
    }

    private static void AssertControlFitsItsParent(Control control)
    {
        Control parent = Assert.IsAssignableFrom<Control>(control.Parent);
        Assert.True(control.Width > 0 && control.Height > 0, $"{control.Name} should have a positive size.");
        Assert.True(
            control.Left >= parent.ClientRectangle.Left &&
            control.Top >= parent.ClientRectangle.Top &&
            control.Right <= parent.ClientRectangle.Right &&
            control.Bottom <= parent.ClientRectangle.Bottom,
            $"{control.Name} bounds {control.Bounds} should fit inside {parent.Name} client area {parent.ClientRectangle}.");
    }
}
