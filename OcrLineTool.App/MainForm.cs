using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

public sealed class MainForm : Form
{
    private const string FixedImageDirectory = @"C:\Users\Administrator\Desktop\每天工具\飞机抓图\结果";
    private readonly string imageRootDirectory;
    private readonly Action<ProcessStartInfo> openFile;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private static readonly Color WindowBackground = Color.FromArgb(17, 18, 20);
    private static readonly Color CardBackground = Color.FromArgb(30, 31, 35);
    private static readonly Color InputBackground = Color.FromArgb(37, 39, 44);
    private static readonly Color CanvasBackground = Color.FromArgb(20, 21, 24);
    private static readonly Color BorderColor = Color.FromArgb(54, 56, 62);
    private static readonly Color PrimaryText = Color.FromArgb(245, 245, 247);
    private static readonly Color SecondaryText = Color.FromArgb(166, 166, 173);
    private static readonly Color AccentBlue = Color.FromArgb(10, 132, 255);
    private static readonly Color DangerRed = Color.FromArgb(255, 69, 58);
    private static readonly Color DisabledBackground = Color.FromArgb(48, 49, 54);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);

    private readonly Label credentialLabel = new() { AutoSize = true };
    private readonly Label selectionLabel = new() { AutoSize = true, Text = "尚未选择子文件夹" };
    private readonly Label folderNameLabel = new()
    {
        AutoEllipsis = true, Dock = DockStyle.Fill, Text = "尚未选择", TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 12, 0)
    };
    private readonly NumericUpDown issueInput = new DarkNumericUpDown()
    {
        Minimum = 1, Maximum = 999999,
        Value = CredentialSchedule.IssueForDate(CredentialSchedule.TodayInBeijing()), Width = 110,
        Font = new Font("Microsoft YaHei UI", 12F)
    };
    private readonly ComboBox credentialSelector = new DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 150,
        Margin = new Padding(8, 0, 0, 0)
    };
    private readonly ListBox folderList = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, FormattingEnabled = true,
        BorderStyle = BorderStyle.FixedSingle, BackColor = InputBackground, ForeColor = PrimaryText
    };
    private readonly Button deleteButton = new DarkButton() { Text = "删除目录图片", AutoSize = true, Padding = new Padding(12, 5, 12, 5), Enabled = false };
    private readonly Button recognizeButton = new DarkButton() { Text = "开始识别", AutoSize = true, Padding = new Padding(12, 5, 12, 5), Enabled = false };
    private readonly Button localPrimaryButton = new DarkButton() { Text = "本地主识别", AutoSize = true, Padding = new Padding(12, 5, 12, 5), Enabled = false };
    private readonly Button retryMissingButton = new DarkButton() { Text = "手动复抓缺失", AutoSize = true, Padding = new Padding(12, 5, 12, 5), Enabled = false };
    private readonly Button manualDistributeButton = new DarkButton() { Text = "手动分流", AutoSize = true, Padding = new Padding(12, 5, 12, 5), Enabled = false };
    private readonly Button continueButton = new DarkButton() { Text = "继续云 OCR", AutoSize = true, Padding = new Padding(12, 5, 12, 5), Enabled = false, Visible = false };
    private readonly Button copyButton = new DarkButton() { Text = "复制结果", AutoSize = true, Padding = new Padding(12, 5, 12, 5), Enabled = false };
    private readonly Button openGroupResultsButton = new DarkButton() { Text = "打开群结果", AutoSize = true, Padding = new Padding(12, 5, 12, 5) };
    private readonly Button clearResultsButton = new DarkButton() { Text = "清除", AutoSize = true, Padding = new Padding(12, 5, 12, 5) };
    private readonly Button missingSummaryButton = new DarkButton() { Text = "统计缺失" };
    private readonly Label recognitionTimingLabel = new()
    {
        AutoSize = true, Visible = false, ForeColor = SecondaryText,
        TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right,
        AccessibleName = "识别耗时和预计剩余时间",
        Font = new Font("Microsoft YaHei UI", 9F)
    };
    private readonly PictureBox preview = new()
    {
        Dock = DockStyle.Fill, BackColor = CanvasBackground,
        SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.None
    };
    private readonly TextBox resultsBox = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Both, WordWrap = false,
        Font = new Font("Microsoft YaHei UI", 11F), BorderStyle = BorderStyle.None,
        BackColor = InputBackground, ForeColor = PrimaryText
    };
    private readonly Label statusLabel = new SingleLineEllipsisLabel()
    {
        AutoSize = false, AutoEllipsis = false, Dock = DockStyle.Fill,
        Text = "请选择要读取的子文件夹。", ForeColor = SecondaryText,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Microsoft YaHei UI", 9F)
    };
    private readonly DarkProgressBar progressBar = new()
    {
        Dock = DockStyle.Fill, Minimum = 0, Maximum = 1, Value = 0,
        Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 5, 0, 0)
    };
    private readonly System.Windows.Forms.Timer dateTimer = new() { Interval = 60_000 };
    private readonly System.Windows.Forms.Timer folderRefreshTimer = new() { Interval = 2_000 };
    private readonly System.Windows.Forms.Timer recognitionTimer = new() { Interval = 1_000 };
    private readonly Stopwatch recognitionWatch = new();
    private int recognitionCompleted;
    private int recognitionTotal;
    private string[] imagePaths = [];
    private DateOnly issueDate;
    private string? selectedImageDirectory;
    private string? selectedRulePath;
    private TaskCompletionSource<bool>? cloudResume;
    private IReadOnlyList<OcrRule> lastRules = [];
    private ResultValues lastValues = new(StringComparer.Ordinal);
    private Dictionary<string, string> lastMissingReasons = new(StringComparer.Ordinal);
    private HashSet<string> lastTextRecognizedRuleIds = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyList<string>> lastCloudOcrResults = new(StringComparer.OrdinalIgnoreCase);
    private ResultEvidenceLedger lastEvidenceLedger = new();
    private int lastIssue;
    private bool isBusy;
    private bool closeWhenIdle;
    private CancellationTokenSource? activeCancellation;
    private CancellationToken ActiveToken => activeCancellation?.Token ?? CancellationToken.None;

    public MainForm() : this(FixedImageDirectory, info => Process.Start(info)) { }

    internal MainForm(string imageDirectory, Action<ProcessStartInfo> openFile)
    {
        imageRootDirectory = imageDirectory;
        this.openFile = openFile;
        Text = $"OCR 整行提取工具 NVIDIA CUDA版 v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        Size = new Size(1400, 850);
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = WindowBackground;
        ForeColor = PrimaryText;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        ConfigureControls();
        credentialSelector.Items.AddRange(["自动轮换", "腾讯 A", "腾讯 B", "腾讯 C", "百度 A", "百度 B", "百度 C", "百度 D", "腾讯 D"]);
        credentialSelector.SelectedIndex = 0;
        credentialSelector.SelectedIndexChanged += (_, _) =>
        {
            RefreshCredentialLabel();
            LogOperation($"选择OCR账号（{credentialSelector.Text}）");
        };
        issueInput.ValueChanged += (_, _) =>
        {
            if (isBusy) return;
            LoadExistingGroupResult();
            manualDistributeButton.Enabled = CanManualDistribute();
            LogOperation("修改期号");
        };

        var layout = new TableLayoutPanel
        {
            Name = "rootLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = WindowBackground, Padding = Padding.Empty, Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        layout.Controls.Add(BuildWorkspace(), 0, 0);
        layout.Controls.Add(BuildStatusSection(), 0, 1);
        Controls.Add(layout);

        folderList.Click += SelectFolderListItem;
        deleteButton.Click += DeleteDirectoryImages;
        recognizeButton.Click += RecognizeImagesAsync;
        localPrimaryButton.Click += RecognizeLocalPrimaryAsync;
        retryMissingButton.Click += RetryMissingAsync;
        manualDistributeButton.Click += ManualDistributeAsync;
        continueButton.Click += ContinueCloudOcr;
        copyButton.Click += (_, _) => CopyResults();
        openGroupResultsButton.Click += (_, _) => OpenGroupResults();
        clearResultsButton.Click += ClearOutputFiles;
        missingSummaryButton.Click += SummarizeMissingAsync;
        RegisterOperationLogging();
        dateTimer.Tick += (_, _) =>
        {
            OperationLog.ClearIfExpired(AppContext.BaseDirectory);
            RefreshCredentialLabel();
        };
        dateTimer.Start();
        OperationLog.ClearIfExpired(AppContext.BaseDirectory);
        LogOperation("启动程序");
        RefreshCredentialLabel();
        RefreshFolderList(null, EventArgs.Empty);
        folderRefreshTimer.Tick += RefreshFolderList;
        folderRefreshTimer.Start();
        recognitionTimer.Tick += (_, _) => UpdateRecognitionTiming();
    }

    private Control BuildWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Name = "workspaceLayout", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Padding = new Padding(16, 16, 16, 10), BackColor = WindowBackground,
            Margin = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 286));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Control sidebar = BuildSidebar();
        Control previewSection = BuildPreviewSection();
        Control resultsSection = BuildResultsSection();
        sidebar.Margin = new Padding(0, 0, 8, 0);
        previewSection.Margin = new Padding(8, 0, 8, 0);
        resultsSection.Margin = new Padding(8, 0, 0, 0);
        workspace.Controls.Add(sidebar, 0, 0);
        workspace.Controls.Add(previewSection, 1, 0);
        workspace.Controls.Add(resultsSection, 2, 0);
        return workspace;
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Name = "sidebarLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = WindowBackground, Margin = Padding.Empty, Padding = Padding.Empty
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 208));

        Control settings = BuildSettingsSection();
        Control tools = BuildToolsSection();
        settings.Margin = new Padding(0, 0, 0, 8);
        tools.Margin = new Padding(0, 8, 0, 0);
        sidebar.Controls.Add(settings, 0, 0);
        sidebar.Controls.Add(tools, 0, 1);
        return sidebar;
    }

    private Control BuildSettingsSection()
    {
        var card = CreateCard("settingsSection");
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12,
            Padding = new Padding(20, 12, 20, 12), BackColor = CardBackground
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 28F, 17F, 34F, 17F, 34F, 17F, 34F, 6F, 38F, 38F, 6F })
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        content.Controls.Add(SectionTitle("设置"), 0, 0);
        content.Controls.Add(FieldLabel("提取期号"), 0, 1);
        issueInput.Dock = DockStyle.Fill;
        issueInput.Margin = Padding.Empty;
        content.Controls.Add(issueInput, 0, 2);
        content.Controls.Add(FieldLabel("OCR 配置"), 0, 3);
        credentialSelector.Dock = DockStyle.Fill;
        credentialSelector.Margin = Padding.Empty;
        content.Controls.Add(credentialSelector, 0, 4);
        content.Controls.Add(FieldLabel("资料文件夹"), 0, 5);
        folderNameLabel.Margin = Padding.Empty;
        content.Controls.Add(folderNameLabel, 0, 6);
        recognizeButton.Dock = DockStyle.Fill;
        recognizeButton.Margin = Padding.Empty;
        content.Controls.Add(recognizeButton, 0, 8);
        localPrimaryButton.Dock = DockStyle.Fill;
        localPrimaryButton.Margin = Padding.Empty;
        content.Controls.Add(localPrimaryButton, 0, 9);
        folderList.Margin = Padding.Empty;
        content.Controls.Add(folderList, 0, 11);
        card.Controls.Add(content);
        return card;
    }

    private Control BuildToolsSection()
    {
        var card = CreateCard("toolsSection");
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(20, 12, 20, 12), BackColor = CardBackground
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(SectionTitle("工具"), 0, 0);

        var actions = new TableLayoutPanel
        {
            Name = "toolsActionsLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = CardBackground, Margin = Padding.Empty, Padding = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Button[] toolButtons = [deleteButton, clearResultsButton, retryMissingButton];
        for (int index = 0; index < toolButtons.Length; index++)
        {
            Button button = toolButtons[index];
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 0, 5);
            if (button == clearResultsButton)
            {
                var pair = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                    BackColor = CardBackground, Margin = new Padding(0, 0, 0, 5)
                };
                pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                button.Margin = new Padding(0, 0, 3, 0);
                missingSummaryButton.Dock = DockStyle.Fill;
                missingSummaryButton.Margin = new Padding(3, 0, 0, 0);
                pair.Controls.Add(button, 0, 0);
                pair.Controls.Add(missingSummaryButton, 1, 0);
                actions.Controls.Add(pair, 0, index);
            }
            else
                actions.Controls.Add(button, 0, index);
        }
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        var finalAction = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = CardBackground };
        manualDistributeButton.Dock = DockStyle.Fill;
        manualDistributeButton.Margin = Padding.Empty;
        continueButton.Dock = DockStyle.Fill;
        continueButton.Margin = Padding.Empty;
        finalAction.Controls.Add(manualDistributeButton);
        finalAction.Controls.Add(continueButton);
        actions.Controls.Add(finalAction, 0, 3);
        content.Controls.Add(actions, 0, 1);
        card.Controls.Add(content);
        return card;
    }

    private Control BuildPreviewSection()
    {
        var card = CreateCard("previewSection");
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = Padding.Empty, BackColor = CardBackground
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(CardHeader("图片预览"), 0, 0);

        var previewHost = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = CardBackground,
            Margin = Padding.Empty
        };
        previewHost.Controls.Add(preview);
        content.Controls.Add(previewHost, 0, 1);
        card.Controls.Add(content);
        return card;
    }

    private Control BuildResultsSection()
    {
        var card = CreateCard("resultsSection");
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = Padding.Empty, BackColor = CardBackground
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            Padding = new Padding(20, 13, 16, 11), BackColor = CardBackground,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(CardHeader("识别结果"), 0, 0);
        recognitionTimingLabel.Margin = new Padding(8, 0, 8, 0);
        copyButton.Margin = new Padding(6, 0, 6, 0);
        openGroupResultsButton.Margin = new Padding(6, 0, 0, 0);
        header.Controls.Add(recognitionTimingLabel, 1, 0);
        header.Controls.Add(copyButton, 2, 0);
        header.Controls.Add(openGroupResultsButton, 3, 0);
        content.Controls.Add(header, 0, 0);

        var resultsHost = new DarkRoundedPanel
        {
            Name = "resultsHost", Dock = DockStyle.Fill, BackColor = InputBackground,
            BorderColor = BorderColor, CornerRadius = 10, Padding = new Padding(18),
            Margin = new Padding(16, 0, 16, 16)
        };
        resultsHost.Controls.Add(resultsBox);
        content.Controls.Add(resultsHost, 0, 1);
        card.Controls.Add(content);
        return card;
    }

    private Control BuildStatusSection()
    {
        var card = CreateCard("statusSection");
        card.Margin = new Padding(16, 0, 16, 14);
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Padding = new Padding(18, 10, 18, 10), BackColor = CardBackground
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = CardBackground, Margin = new Padding(0, 0, 18, 0)
        };
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        selectionLabel.AutoSize = false;
        selectionLabel.AutoEllipsis = true;
        selectionLabel.Dock = DockStyle.Fill;
        selectionLabel.ForeColor = PrimaryText;
        selectionLabel.TextAlign = ContentAlignment.BottomLeft;
        credentialLabel.AutoSize = false;
        credentialLabel.AutoEllipsis = true;
        credentialLabel.Dock = DockStyle.Fill;
        credentialLabel.ForeColor = SecondaryText;
        credentialLabel.TextAlign = ContentAlignment.TopLeft;
        summary.Controls.Add(selectionLabel, 0, 0);
        summary.Controls.Add(credentialLabel, 0, 1);
        content.Controls.Add(summary, 0, 0);

        var progress = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = CardBackground, Margin = new Padding(0, 2, 18, 0)
        };
        progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        progress.Controls.Add(FieldLabel("处理进度"), 0, 0);
        progressBar.Margin = new Padding(0, 4, 0, 0);
        progress.Controls.Add(progressBar, 0, 1);
        content.Controls.Add(progress, 1, 0);
        statusLabel.Padding = new Padding(4, 0, 0, 0);
        content.Controls.Add(statusLabel, 2, 0);
        card.Controls.Add(content);
        return card;
    }

    private static DarkRoundedPanel CreateCard(string name) => new()
    {
        Name = name, Dock = DockStyle.Fill, BackColor = CardBackground,
        BorderColor = BorderColor, CornerRadius = 12, Padding = new Padding(1)
    };

    private static Label SectionTitle(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoSize = false, ForeColor = PrimaryText,
        Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label CardHeader(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoSize = false, ForeColor = PrimaryText,
        Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(18, 0, 0, 0)
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoSize = false, ForeColor = SecondaryText,
        Font = new Font("Microsoft YaHei UI", 9F), TextAlign = ContentAlignment.BottomLeft
    };

    private void ConfigureControls()
    {
        issueInput.Name = "issueInput";
        issueInput.AccessibleName = "提取期号";
        issueInput.TabIndex = 0;
        issueInput.BackColor = InputBackground;
        issueInput.ForeColor = PrimaryText;
        issueInput.BorderStyle = BorderStyle.FixedSingle;
        issueInput.Font = new Font("Microsoft YaHei UI", 11F);

        credentialSelector.Name = "credentialSelector";
        credentialSelector.AccessibleName = "OCR 配置";
        credentialSelector.TabIndex = 1;
        credentialSelector.BackColor = InputBackground;
        credentialSelector.ForeColor = PrimaryText;
        credentialSelector.FlatStyle = FlatStyle.Flat;
        credentialSelector.DrawMode = DrawMode.OwnerDrawFixed;
        credentialSelector.ItemHeight = 28;
        credentialSelector.DrawItem += DrawCredentialItem;

        foreach (Control child in issueInput.Controls)
        {
            child.BackColor = InputBackground;
            child.ForeColor = PrimaryText;
        }

        folderNameLabel.Name = "selectedFolder";
        folderNameLabel.AccessibleName = "当前资料文件夹";
        folderNameLabel.BackColor = InputBackground;
        folderNameLabel.ForeColor = PrimaryText;
        folderNameLabel.BorderStyle = BorderStyle.FixedSingle;

        folderList.Name = "folderList";
        folderList.AccessibleName = "可选资料群";
        folderList.TabIndex = 4;
        folderList.DisplayMember = nameof(DirectoryInfo.Name);
        folderList.Font = new Font("Microsoft YaHei UI", 10F);

        preview.Name = "imagePreview";
        preview.AccessibleName = "图片预览";
        resultsBox.Name = "recognitionResults";
        resultsBox.AccessibleName = "识别结果";
        progressBar.Name = "ocrProgress";
        progressBar.AccessibleName = "OCR 处理进度";
        progressBar.BackColor = InputBackground;
        progressBar.ForeColor = AccentBlue;

        ConfigureButton(recognizeButton, "开始识别", AccentBlue, Color.White, AccentBlue, 3);
        ConfigureButton(localPrimaryButton, "本地主识别", CardBackground, PrimaryText, BorderColor, 4);
        ConfigureButton(deleteButton, "删除目录图片", CardBackground, DangerRed, Color.FromArgb(112, 45, 42), 8);
        ConfigureButton(clearResultsButton, "清除结果", CardBackground, PrimaryText, BorderColor, 9);
        ConfigureButton(missingSummaryButton, "统计缺失", CardBackground, PrimaryText, BorderColor, 13);
        ConfigureButton(retryMissingButton, "手动复抓缺失", CardBackground, PrimaryText, BorderColor, 10);
        ConfigureButton(manualDistributeButton, "手动分流", CardBackground, PrimaryText, BorderColor, 12);
        ConfigureButton(continueButton, "继续云 OCR", CardBackground, AccentBlue, AccentBlue, 11);
        ConfigureButton(copyButton, "复制结果", CardBackground, PrimaryText, BorderColor, 5);
        ConfigureButton(openGroupResultsButton, "打开群结果", CardBackground, PrimaryText, BorderColor, 6);

    }

    private static void ConfigureButton(
        Button button,
        string accessibleName,
        Color enabledBackground,
        Color enabledForeground,
        Color enabledBorder,
        int tabIndex)
    {
        button.Name = accessibleName;
        button.AccessibleName = accessibleName;
        button.TabIndex = tabIndex;
        button.AutoSize = false;
        button.Height = 38;
        button.Padding = Padding.Empty;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = enabledBackground == AccentBlue
            ? Color.FromArgb(34, 148, 255)
            : Color.FromArgb(43, 45, 50);
        button.FlatAppearance.MouseDownBackColor = enabledBackground == AccentBlue
            ? Color.FromArgb(0, 105, 220)
            : Color.FromArgb(50, 52, 58);

        void RefreshStyle(object? _, EventArgs __)
        {
            button.BackColor = button.Enabled ? enabledBackground : DisabledBackground;
            button.ForeColor = button.Enabled ? enabledForeground : Color.FromArgb(119, 120, 126);
            button.FlatAppearance.BorderColor = button.Enabled ? enabledBorder : BorderColor;
        }

        button.EnabledChanged += RefreshStyle;
        RefreshStyle(null, EventArgs.Empty);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int enabled = 1;
            int result = DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
            if (result != 0)
                _ = DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyDarkNativeTheme(issueInput, string.Empty);
        ApplyDarkNativeTheme(credentialSelector, string.Empty);
        ApplyDarkNativeTheme(folderList, "DarkMode_Explorer");
        ApplyDarkNativeTheme(resultsBox, "DarkMode_Explorer");
    }

    private static void ApplyDarkNativeTheme(Control control, string theme)
    {
        try
        {
            if (!control.IsHandleCreated)
                control.CreateControl();
            _ = SetWindowTheme(control.Handle, theme, null);
            foreach (Control child in control.Controls)
            {
                child.BackColor = InputBackground;
                child.ForeColor = PrimaryText;
                ApplyDarkNativeTheme(child, theme);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void DrawCredentialItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color background = selected ? Color.FromArgb(52, 54, 60) : InputBackground;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index >= 0)
        {
            string text = comboBox.GetItemText(comboBox.Items[e.Index]) ?? string.Empty;
            TextRenderer.DrawText(
                e.Graphics,
                text,
                comboBox.Font,
                e.Bounds with { X = e.Bounds.X + 8, Width = Math.Max(0, e.Bounds.Width - 12) },
                PrimaryText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        e.DrawFocusRectangle();
    }

    private void RefreshCredentialLabel()
    {
        CloudOcrCacheStore.ClearExpired(AppContext.BaseDirectory);
        DateOnly date = CredentialSchedule.TodayInBeijing();
        if (issueDate != date && !isBusy)
        {
            issueInput.Value = CredentialSchedule.IssueForDate(date);
            issueDate = date;
        }
        if (credentialSelector.SelectedIndex <= 0)
        {
            credentialLabel.Text = $"北京时间 {date:yyyy-MM-dd} · 今日使用：{CredentialSchedule.DescribeDate(date).DisplayName} · 每日 00:00 自动轮换";
            return;
        }

        credentialLabel.Text = $"北京时间 {date:yyyy-MM-dd} · 临时使用：{CredentialSchedule.DescribeSlot(credentialSelector.SelectedIndex - 1).DisplayName} · 不改变自动轮换";
    }

    private void RefreshFolderList(object? sender, EventArgs e)
    {
        string[] folders;
        try
        {
            folders = ImageFolderScanner.ListSubfolders(imageRootDirectory);
        }
        catch (Exception exception) when (exception is OcrException or IOException or UnauthorizedAccessException)
        {
            // A transient polling failure must not clear the list or interrupt OCR with a dialog.
            if (sender is null)
                statusLabel.Text = "群目录暂不可用，恢复后会自动更新。";
            return;
        }
        if (statusLabel.Text == "群目录暂不可用，恢复后会自动更新。")
            statusLabel.Text = "请在列表中选择群组。";
        if (folderList.Items.Cast<DirectoryInfo>().Select(folder => folder.FullName)
            .SequenceEqual(folders, StringComparer.OrdinalIgnoreCase))
            return;

        int topIndex = folderList.TopIndex;
        folderList.BeginUpdate();
        try
        {
            folderList.Items.Clear();
            folderList.Items.AddRange(folders.Select(folder => new DirectoryInfo(folder)).ToArray());
            folderList.SelectedIndex = Array.FindIndex(folders,
                folder => string.Equals(folder, selectedImageDirectory, StringComparison.OrdinalIgnoreCase));
            if (folders.Length > 0)
                folderList.TopIndex = Math.Clamp(topIndex, 0, folders.Length - 1);
        }
        finally { folderList.EndUpdate(); }
    }

    private void SelectFolderListItem(object? sender, EventArgs e)
    {
        if (folderList.SelectedItem is DirectoryInfo folder)
        {
            SelectImageDirectory(folder.FullName);
            if (string.Equals(selectedImageDirectory, folder.FullName, StringComparison.OrdinalIgnoreCase))
                LogOperation("选择群组");
        }
    }

    private void RegisterOperationLogging()
    {
        (Button Button, string Action)[] actions =
        [
            (deleteButton, "删除目录图片"),
            (recognizeButton, "开始识别"),
            (localPrimaryButton, "本地主识别"),
            (retryMissingButton, "手动复抓缺失"),
            (manualDistributeButton, "手动分流"),
            (continueButton, "继续云OCR"),
            (copyButton, "复制结果"),
            (openGroupResultsButton, "打开群结果"),
            (clearResultsButton, "清除"),
            (missingSummaryButton, "统计缺失")
        ];
        foreach ((Button button, string action) in actions)
            button.Click += (_, _) => LogOperation(action);
    }

    private void LogOperation(string action)
    {
        string? group = selectedImageDirectory is null
            ? null
            : RuleCatalog.BaseGroupName(selectedImageDirectory);
        OperationLog.Append(AppContext.BaseDirectory, action, group, Decimal.ToInt32(issueInput.Value));
    }

    private void SelectImageDirectory(string directory)
    {
        try
        {
            imagePaths = ImageFolderScanner.Scan(directory);
        }
        catch (OcrException exception)
        {
            statusLabel.Text = "固定目录不可用。";
            MessageBox.Show(this, exception.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        selectedImageDirectory = directory;
        folderNameLabel.Text = Path.GetFileName(directory);
        selectedRulePath = RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedImageDirectory);
        ClearRetryState();
        LoadExistingGroupResult();
        bool hasRules = File.Exists(selectedRulePath);
        selectionLabel.Text = imagePaths.Length == 0
            ? $"所选目录为空：{selectedImageDirectory}"
            : $"所选目录：{selectedImageDirectory} · 已找到 {imagePaths.Length} 张图片 · 规则：{Path.GetFileName(selectedRulePath)}";
        recognizeButton.Enabled = imagePaths.Length > 0 && hasRules;
        localPrimaryButton.Enabled = imagePaths.Length > 0 && hasRules;
        deleteButton.Enabled = imagePaths.Length > 0;
        manualDistributeButton.Enabled = CanManualDistribute();
        if (imagePaths.Length > 0)
            ShowPreview(imagePaths[0]);
        else
            preview.Image = null;
        statusLabel.Text = imagePaths.Length == 0
            ? "所选子文件夹中没有支持的图片。"
            : hasRules
                ? "图片和对应规则已就绪，点击“开始识别”或“本地主识别”。"
                : $"未找到对应规则文件：{Path.GetFileName(selectedRulePath)}";
        SetProgress(0, imagePaths.Length);
    }

    private async void RecognizeLocalPrimaryAsync(object? sender, EventArgs e)
    {
        await RunLocalPrimaryRecognitionAsync();
    }

    private async Task RunLocalPrimaryRecognitionAsync()
    {
        int issue = Decimal.ToInt32(issueInput.Value);
        string? temporaryCropFolder = null;
        ClearRetryState();
        SetBusy(true);
        StartRecognitionTiming();
        resultsBox.Clear();
        copyButton.Enabled = false;

        try
        {
            RefreshImagesForRetry();
            if (imagePaths.Length == 0) throw new OcrException("所选目录没有可识别图片。");
            var localClient = new PaddleLocalOcrClient();
            await localClient.EnsureCudaAvailableAsync(ActiveToken);

            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
                ?? throw new OcrException("请先选择要读取的子文件夹。"));
            string groupName = RuleCatalog.GroupNameForFolder(AppContext.BaseDirectory, selectedImageDirectory!);

            // 复用旧模式的候选筛选、模板裁剪和目录约束；新模式只替换最终识别模型。
            CandidateSelection selection = await SelectCandidatesAsync(
                rules,
                issue,
                allowTemplateSubset: false,
                completeRuleIds: rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal));
            IReadOnlyList<RecognitionCandidate> selectedCandidates = selection.Candidates;
            temporaryCropFolder = selection.TemporaryCropFolder;

            string[] mediumPaths = selectedCandidates
                .Select(candidate => candidate.OcrPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var localWatch = Stopwatch.StartNew();
            var localProgress = new Progress<LocalOcrProgress>(item =>
            {
                if (string.IsNullOrEmpty(item.Path))
                {
                    SetIndeterminateProgress();
                    statusLabel.Text = $"本地主识别：{item.Stage} · 已用 {FormatDuration(localWatch.Elapsed)}";
                    return;
                }

                SetProgress(item.Completed, item.Total);
                int percent = item.Total == 0 ? 0 : item.Completed * 100 / item.Total;
                statusLabel.Text = $"本地主识别：本地 OCR {item.Completed}/{item.Total}（{percent}%） · 当前：{ShortPath(item.Path)}";
            });

            var mediumResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (mediumPaths.Length > 0)
                {
                    IReadOnlyDictionary<string, IReadOnlyList<string>> results =
                        await localClient.RecognizeBatchAsync(
                            mediumPaths,
                            localProgress,
                            titleRatio: 1.0,
                            detectionMaxSide: null,
                            model: PaddleOcrModels.LocalPrimary, cancellationToken: ActiveToken);
                    foreach ((string path, IReadOnlyList<string> lines) in results)
                        mediumResults[path] = lines;
                }
            }
            catch (OcrException exception) when (PaddleLocalOcrClient.IsCudaUnavailable(exception))
            {
                // N 卡版本没有 CUDA 时必须停止，不能把整批任务伪装成云 OCR 完成。
                throw;
            }
            catch (OcrException exception)
            {
                statusLabel.Text = $"本地主识别失败，准备云 OCR 兜底：{exception.Message}";
            }

            IReadOnlyList<RecognitionCandidate> candidates = selectedCandidates
                .Select(candidate => candidate with
                {
                    LocalLines = mediumResults.TryGetValue(candidate.OcrPath, out IReadOnlyList<string>? lines)
                        ? lines
                        : [],
                    LocalEvidence = BindPaddleEvidence(localClient, candidate, "local-primary/medium")
                })
                .ToArray();
            var values = new ResultValues(StringComparer.Ordinal);
            var evidenceLedger = new ResultEvidenceLedger();
            var recognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);

            void ApplyLocalValues(IEnumerable<RecognitionCandidate> source)
            {
                foreach (RecognitionCandidate candidate in source)
                {
                    OcrEvidence? evidence = candidate.LocalEvidence;
                    if (evidence is null)
                        continue;
                    if (evidence.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)))
                        recognizedRuleIds.UnionWith(candidate.Rules.Select(rule => rule.Id));
                    AddExtractedEvidenceValues(
                        evidence, candidate.Rules, issue, values, evidenceLedger);
                }
            }

            ApplyLocalValues(candidates);

            IReadOnlyList<IReadOnlyList<string>> localSamples = candidates
                .Where(candidate => candidate.IsPrimary)
                .Select(candidate => candidate.LocalEvidence?.Lines ?? [])
                .Take(10)
                .ToArray();
            int? detectedIssue = localSamples.Count == 10
                ? RuleEngine.DetectIssueMismatch(localSamples, issue)
                : null;
            if (detectedIssue is not null)
            {
                statusLabel.Text = $"已停止：图片主要为 {detectedIssue}期，软件当前选择 {issue}期。";
                MessageBox.Show(
                    this,
                    $"软件当前选择：{issue}期{Environment.NewLine}本地 OCR 前10张图片主要识别到：{detectedIssue}期{Environment.NewLine}图片期数与软件期数不一致，已停止本地主识别。请修改期数后重新开始。",
                    "期数不一致",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var cloudRulesByPath = new Dictionary<(string SourcePath, string OcrPath), List<OcrRule>>();
            void AddCloudRule(string sourcePath, string ocrPath, OcrRule rule)
            {
                var key = (sourcePath, ocrPath);
                if (!cloudRulesByPath.TryGetValue(key, out List<OcrRule>? list))
                    cloudRulesByPath[key] = list = [];
                if (!list.Any(item => item.Id == rule.Id))
                    list.Add(rule);
            }

            foreach (RecognitionCandidate candidate in candidates)
            {
                foreach (OcrRule rule in RulesForAlreadyRequestedCloudFallback(candidate.Rules, values))
                    AddCloudRule(candidate.SourcePath, candidate.OcrPath, rule);
            }

            var cloudCandidates = cloudRulesByPath
                .Select(item =>
                {
                    IReadOnlyList<OcrRule> candidateRules = item.Value;
                    string sourcePath = item.Key.SourcePath;
                    string ocrPath = item.Key.OcrPath;
                    mediumResults.TryGetValue(ocrPath, out IReadOnlyList<string>? lines);
                    return new RecognitionCandidate(
                        sourcePath,
                        ocrPath,
                        candidateRules,
                        true,
                        "本地主识别云兜底",
                        null,
                        lines ?? []);
                })
                .ToArray();

            // 新模式的兜底必须按本模式的原图/杰少压缩图重新请求，不能复用旧模式的裁剪云缓存。
            lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var localPrimaryCloudEvidence = new Dictionary<string, OcrEvidence>(StringComparer.OrdinalIgnoreCase);
            var cloudDeduplicators = new Dictionary<OcrProvider, CloudImageDeduplicator>();
            var providerSpacing = new Dictionary<OcrProvider, Stopwatch>();
            var providerStarted = new HashSet<OcrProvider>();
            OcrCredential selectedCredential = credentialSelector.SelectedIndex <= 0
                ? CredentialSchedule.DescribeDate(CredentialSchedule.TodayInBeijing())
                : CredentialSchedule.DescribeSlot(credentialSelector.SelectedIndex - 1);
            int cloudRequests = 0;
            int completed = 0;
            SetProgress(0, cloudCandidates.Length);

            async Task<OcrEvidence> RequestCloudFallbackAsync(RecognitionCandidate candidate)
            {
                OcrException? lastError = null;
                foreach (OcrCredential credential in CredentialSchedule.RotationFrom(selectedCredential))
                {
                    if (string.IsNullOrWhiteSpace(credential.Id) || string.IsNullOrWhiteSpace(credential.Secret))
                        continue;

                    IOcrClient client;
                    try
                    {
                        client = OcrClientFactory.CreateDeferred(credential);
                    }
                    catch (OcrException exception)
                    {
                        lastError = exception;
                        continue;
                    }

                    if (!cloudDeduplicators.TryGetValue(credential.Provider, out CloudImageDeduplicator? deduplicator))
                        cloudDeduplicators[credential.Provider] = deduplicator = new CloudImageDeduplicator();
                    OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                        candidate.SourcePath, candidate.OcrPath,
                        $"local-primary/cloud/{credential.Provider}/{candidate.SelectionMode}");
                    try
                    {
                        OcrEvidence evidence = await deduplicator.RecognizeEvidenceAsync(
                            selectedImageDirectory!,
                            candidate.OcrPath,
                            candidate.Rules,
                            async () =>
                            {
                                if (!providerSpacing.TryGetValue(credential.Provider, out Stopwatch? spacing))
                                    providerSpacing[credential.Provider] = spacing = new Stopwatch();
                                if (providerStarted.Contains(credential.Provider))
                                    await WaitForPacingAsync(spacing, CloudOcrPolicy.MinimumInterval(credential.Provider));
                                spacing.Restart();
                                providerStarted.Add(credential.Provider);
                                cloudRequests++;
                                statusLabel.Text = $"本地主识别：云 OCR 兜底 {credential.DisplayName} · {completed + 1}/{cloudCandidates.Length} · 当前：{ShortPath(candidate.SourcePath)}";
                                return await client.RecognizeEvidenceAsync(candidate.OcrPath, ActiveToken);
                            });
                        return evidence.Bind(identity);
                    }
                    catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(credential.Provider, exception))
                    {
                        lastError = exception;
                        statusLabel.Text = $"本地主识别：{credential.DisplayName} 没有额度，切换下一个账号……";
                    }
                    catch (OcrException exception)
                    {
                        lastError = exception;
                        break;
                    }
                }

                throw lastError ?? new OcrException("没有可用的云 OCR 账号。");
            }

            var diagnostics = candidates.Select(candidate => (object)new
            {
                file = Path.GetFileName(candidate.SourcePath),
                mode = "本地主识别",
                model = "medium",
                selection = candidate.SelectionMode,
                template_distance = candidate.TemplateDistance,
                rules = candidate.Rules.Select(rule => rule.Id),
                lines = candidate.LocalLines,
                error = localClient.LastImageErrors.GetValueOrDefault(candidate.OcrPath)
            }).ToList();
            foreach (RecognitionCandidate candidate in cloudCandidates)
            {
                // The candidate list was frozen while these rules were missing. Do not
                // drop a rule merely because an earlier competing candidate succeeded;
                // compare every already-planned observation so conflicts stay visible.
                OcrRule[] evidenceRules = candidate.Rules.ToArray();
                if (evidenceRules.Length == 0)
                    continue;

                OcrEvidence? cloudEvidence = null;
                string? cloudProvider = null;
                string? cloudError = null;
                if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                    localPrimaryCloudEvidence.TryGetValue(candidate.SourcePath, out OcrEvidence? cachedEvidence) &&
                    IsEvidenceCurrent(cachedEvidence))
                {
                    cloudEvidence = cachedEvidence;
                    cloudProvider = "本次运行证据复用";
                }
                else
                {
                    try
                    {
                        cloudEvidence = await RequestCloudFallbackAsync(candidate);
                        cloudProvider = "云 OCR";
                    }
                    catch (OcrException exception)
                    {
                        cloudError = exception.Message;
                    }
                }

                IReadOnlyList<string> cloudLines = cloudEvidence?.Lines ?? [];
                if (cloudEvidence is not null)
                {
                    AddExtractedEvidenceValues(
                        cloudEvidence, evidenceRules, issue, values, evidenceLedger);
                    recognizedRuleIds.UnionWith(evidenceRules.Select(rule => rule.Id));
                }

                if (cloudEvidence is not null && cloudLines.Count > 0)
                {
                    lastCloudOcrResults[candidate.SourcePath] = cloudLines;
                    if (candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase))
                        localPrimaryCloudEvidence[candidate.SourcePath] = cloudEvidence;
                    CloudOcrCacheStore.SaveEntry(
                        AppContext.BaseDirectory, groupName, issue, cloudEvidence);
                }
                diagnostics.Add(new
                {
                    file = Path.GetFileName(candidate.SourcePath),
                    ocr_path = candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : Path.GetFileName(candidate.OcrPath),
                    rules = evidenceRules.Select(rule => rule.Id),
                    provider = cloudProvider,
                    lines = cloudLines,
                    source_hash = cloudEvidence?.SourceHash,
                    input_hash = cloudEvidence?.InputHash,
                    view = cloudEvidence?.ViewId,
                    regions = cloudEvidence?.Items.Select(item => item.ViewId + "/" + item.RegionId).Distinct(),
                    minimum_confidence = cloudEvidence?.MinimumConfidence,
                    error = cloudError
                });
                completed++;
                SetProgress(completed, cloudCandidates.Length);
            }

            HashSet<string> candidateRuleIds = candidates
                .Concat(cloudCandidates)
                .SelectMany(candidate => candidate.Rules)
                .Select(rule => rule.Id)
                .ToHashSet(StringComparer.Ordinal);
            var missingReasons = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (OcrRule rule in rules.Where(rule => !values.ContainsKey(rule.Id)))
                missingReasons[rule.Id] = RuleEngine.DescribeMissing(
                    candidateRuleIds.Contains(rule.Id),
                    recognizedRuleIds.Contains(rule.Id));

            ActiveToken.ThrowIfCancellationRequested();
            string[] outputLines = RuleEngine.FormatOutput(rules, values, missingReasons);
            ResultFilePaths.EnsureOutputDirectories(AppContext.BaseDirectory);
            string groupOutputPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory!, issue);
            string diagnosticPath = ResultFilePaths.ForDiagnostic(
                AppContext.BaseDirectory, selectedImageDirectory!, issue);
            await AtomicFile.WriteAllLinesAsync(groupOutputPath, outputLines, new UTF8Encoding(true));
            await RecognitionStateStore.SaveAsync(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules, values, evidenceLedger, ActiveToken);
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, issue, outputLines);
            string[] groupLines = GroupResultFormatter.Format(
                rules,
                ResultDistributor.MarkDistributedLines(outputLines, distribution.DistributedLines));
            await AtomicFile.WriteAllLinesAsync(groupOutputPath, groupLines, new UTF8Encoding(true));
            await AtomicFile.WriteAllTextAsync(
                diagnosticPath,
                JsonSerializer.Serialize(new
                {
                    group = groupName,
                    issue,
                    mode = "本地主识别",
                    local_model = "PP-OCRv6_medium_det + PP-OCRv6_medium_rec",
                    image_count = imagePaths.Length,
                    cloud_request_count = cloudRequests,
                    matched_rule_count = values.Count,
                    missing_rules = rules.Select(rule => rule.Id).Where(id => !values.ContainsKey(id)),
                    missing_reasons = missingReasons,
                    images = diagnostics
                }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(true));
            LogMissingDetails("本地主识别", groupName, issue, rules, values, candidates);
            resultsBox.Text = string.Join(Environment.NewLine, groupLines);
            copyButton.Enabled = outputLines.Length > 0;
            lastRules = rules;
            lastValues = new ResultValues(values, StringComparer.Ordinal);
            lastMissingReasons = new Dictionary<string, string>(missingReasons, StringComparer.Ordinal);
            lastTextRecognizedRuleIds = new HashSet<string>(recognizedRuleIds, StringComparer.Ordinal);
            lastEvidenceLedger = evidenceLedger;
            lastIssue = issue;
            SetProgress(1, 1);
            statusLabel.Text = $"本地主识别完成{(rules.Count == values.Count ? "" : "，但有缺失")}：目录 {imagePaths.Length} 张，本地优先，云 OCR 兜底 {cloudRequests} 次，提取 {values.Count} 条，缺失 {rules.Count - values.Count} 条，成功分流 {distribution.DistributedLines.Count} 条{DistributionErrorText(distribution)}。群TXT：{groupOutputPath}；诊断：{diagnosticPath}";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "识别已取消；未完成结果不会自动分流。";
        }
        catch (OcrException exception)
        {
            statusLabel.Text = "本地主识别失败。";
            MessageBox.Show(this, exception.Message, "本地主识别失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            OperationLog.AppendException(AppContext.BaseDirectory, "本地主识别", RuleCatalog.BaseGroupName(selectedImageDirectory ?? ""), issue, exception);
            statusLabel.Text = "本地主识别失败。";
            MessageBox.Show(this, "发生未预期错误，请关闭软件后重试。", "本地主识别失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (temporaryCropFolder is not null)
            {
                try { Directory.Delete(temporaryCropFolder, recursive: true); } catch { }
            }
            StopRecognitionTiming();
            SetBusy(false);
            RefreshCredentialLabel();
        }
    }

    private static bool IsUnderDirectory(string path, string directoryName)
    {
        string? directory = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Path.GetFileName(directory).Equals(directoryName, StringComparison.OrdinalIgnoreCase))
                return true;
            directory = Path.GetDirectoryName(directory);
        }
        return false;
    }

    private async void RecognizeImagesAsync(object? sender, EventArgs e)
    {
        int issue = Decimal.ToInt32(issueInput.Value);
        string? temporaryCropFolder = null;
        ClearRetryState();
        SetBusy(true);
        StartRecognitionTiming();
        resultsBox.Clear();
        copyButton.Enabled = false;

        try
        {
            RefreshImagesForRetry();
            if (imagePaths.Length == 0) throw new OcrException("所选目录没有可识别图片。");
            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
                ?? throw new OcrException("请先选择要读取的子文件夹。"));
            string groupName = RuleCatalog.GroupNameForFolder(AppContext.BaseDirectory, selectedImageDirectory!);
            lastCloudOcrResults = CloudOcrCacheStore.Load(AppContext.BaseDirectory, groupName, issue);
            CandidateSelection selection = await SelectCandidatesAsync(
                rules,
                issue,
                allowTemplateSubset: false,
                completeRuleIds: rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal));
            IReadOnlyList<RecognitionCandidate> candidates = selection.Candidates;
            temporaryCropFolder = selection.TemporaryCropFolder;

            OcrCredential credential = credentialSelector.SelectedIndex <= 0
                ? CredentialSchedule.DescribeDate(CredentialSchedule.TodayInBeijing())
                : CredentialSchedule.DescribeSlot(credentialSelector.SelectedIndex - 1);
            IOcrClient cloudClient = OcrClientFactory.CreateDeferred(credential);
            OcrCredential fallbackCredential = CredentialSchedule.DescribeFallback(credential);
            IOcrClient fallbackClient = OcrClientFactory.CreateDeferred(fallbackCredential);
            var values = new ResultValues(StringComparer.Ordinal);
            var evidenceLedger = new ResultEvidenceLedger();
            var missingReasons = new Dictionary<string, string>(StringComparer.Ordinal);
            var textRecognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> candidateRuleIds = candidates
                .SelectMany(candidate => candidate.Rules)
                .Select(rule => rule.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (OcrRule rule in rules.Where(rule => !candidateRuleIds.Contains(rule.Id)))
                missingReasons[rule.Id] = RuleEngine.DescribeMissing(foundImage: false, recognizedText: false);
            var diagnostics = new List<object>();
            int cloudRequests = 0;
            var primaryImageDeduplicator = new CloudImageDeduplicator();
            var fallbackImageDeduplicator = new CloudImageDeduplicator();
            var cloudWatch = Stopwatch.StartNew();
            var requestSpacing = Stopwatch.StartNew();
            var fallbackRequestSpacing = Stopwatch.StartNew();
            bool requestStarted = false;
            bool fallbackRequestStarted = false;
            TimeSpan minimumInterval = CloudOcrPolicy.MinimumInterval(credential.Provider);
            TimeSpan fallbackMinimumInterval = CloudOcrPolicy.MinimumInterval(fallbackCredential.Provider);
            int plannedCloudImages = candidates.Count(candidate => candidate.IsPrimary);

            async Task<OcrEvidence> RecognizePrimaryAsync(
                RecognitionCandidate candidate,
                int displayIndex,
                int displayTotal,
                string stage)
            {
                OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.OcrPath, $"cloud-primary/{candidate.SelectionMode}");
                int automaticRetry = 0;
                while (true)
                {
                    if (requestStarted)
                    {
                        TimeSpan pacingDelay = minimumInterval - requestSpacing.Elapsed;
                        if (pacingDelay > TimeSpan.Zero)
                        {
                            statusLabel.Text = $"{stage}：按免费额度限速，{pacingDelay.TotalMilliseconds:0} 毫秒后处理 {displayIndex}/{displayTotal} · 当前：{ShortPath(candidate.SourcePath)}";
                            await Task.Delay(pacingDelay, ActiveToken);
                        }
                    }

                    int completedBeforeCall = displayIndex - 1;
                    TimeSpan remaining = completedBeforeCall == 0
                        ? TimeSpan.Zero
                        : TimeSpan.FromSeconds(cloudWatch.Elapsed.TotalSeconds / completedBeforeCall * (displayTotal - completedBeforeCall));
                    string remainingText = completedBeforeCall == 0 ? "计算中" : FormatDuration(remaining);
                    statusLabel.Text = $"{stage}：正在处理 {displayIndex}/{displayTotal} · 当前：{ShortPath(candidate.SourcePath)} · 已用 {FormatDuration(cloudWatch.Elapsed)} · 预计剩余 {remainingText}";
                    requestSpacing.Restart();
                    requestStarted = true;
                    cloudRequests++;
                    try
                    {
                        OcrEvidence evidence = await cloudClient.RecognizeEvidenceAsync(candidate.OcrPath, ActiveToken);
                        return evidence.Bind(identity);
                    }
                    catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(credential.Provider, exception))
                    {
                        if (automaticRetry < CloudOcrPolicy.MaxAutomaticRetries)
                        {
                            automaticRetry++;
                            TimeSpan retryDelay = CloudOcrPolicy.RetryDelay(automaticRetry);
                            statusLabel.Text = $"{stage}触发限流：停在 {displayIndex}/{displayTotal}，{retryDelay.TotalSeconds:0} 秒后自动重试（{automaticRetry}/{CloudOcrPolicy.MaxAutomaticRetries}）";
                            await Task.Delay(retryDelay, ActiveToken);
                            continue;
                        }

                        resultsBox.Text = string.Join(Environment.NewLine, RuleEngine.FormatOutput(rules, values, missingReasons));
                        await WaitForCloudResumeAsync(displayIndex, displayTotal, candidate.SourcePath);
                        automaticRetry = 0;
                    }
                }
            }

            const int issueCheckCount = 10;
            RecognitionCandidate[] issueCheckCandidates = candidates
                .Where(candidate => candidate.IsPrimary)
                .Take(issueCheckCount)
                .ToArray();
            var checkedCloudEvidence = new Dictionary<string, OcrEvidence>(StringComparer.Ordinal);
            var issueCheckEvidence = new List<OcrEvidence>();
            async Task<OcrEvidence> RecognizePrimaryDeduplicatedAsync(
                RecognitionCandidate candidate, int displayIndex, int displayTotal, string stage)
                => await primaryImageDeduplicator.RecognizeEvidenceAsync(
                    selectedImageDirectory!, candidate.OcrPath, candidate.Rules,
                    () => RecognizePrimaryAsync(candidate, displayIndex, displayTotal, stage));

            async Task<OcrEvidence?> RecognizeFallbackAsync(
                RecognitionCandidate candidate,
                int cloudIndex,
                Action<string> setError)
            {
                OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.SourcePath, "cloud-fallback/original");
                int fallbackRetry = 0;
                while (true)
                {
                    if (fallbackRequestStarted)
                    {
                        TimeSpan pacingDelay = fallbackMinimumInterval - fallbackRequestSpacing.Elapsed;
                        if (pacingDelay > TimeSpan.Zero)
                        {
                            statusLabel.Text = $"备用 {fallbackCredential.DisplayName}：等待 {pacingDelay.TotalMilliseconds:0} 毫秒 · {cloudIndex}/{plannedCloudImages} · 当前：{ShortPath(candidate.SourcePath)}";
                            await Task.Delay(pacingDelay, ActiveToken);
                        }
                    }

                    fallbackRequestSpacing.Restart();
                    fallbackRequestStarted = true;
                    cloudRequests++;
                    statusLabel.Text = $"主云结果不完整，正在用 {fallbackCredential.DisplayName} 整图补识别 {cloudIndex}/{plannedCloudImages} · 当前：{ShortPath(candidate.SourcePath)}";
                    try
                    {
                        OcrEvidence evidence = await fallbackClient.RecognizeEvidenceAsync(candidate.SourcePath, ActiveToken);
                        return evidence.Bind(identity);
                    }
                    catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(fallbackCredential.Provider, exception))
                    {
                        if (fallbackRetry < CloudOcrPolicy.MaxAutomaticRetries)
                        {
                            fallbackRetry++;
                            TimeSpan retryDelay = CloudOcrPolicy.RetryDelay(fallbackRetry);
                            statusLabel.Text = $"备用 {fallbackCredential.DisplayName} 触发限流，{retryDelay.TotalSeconds:0} 秒后重试（{fallbackRetry}/{CloudOcrPolicy.MaxAutomaticRetries}）";
                            await Task.Delay(retryDelay, ActiveToken);
                            continue;
                        }

                        resultsBox.Text = string.Join(Environment.NewLine, RuleEngine.FormatOutput(rules, values, missingReasons));
                        await WaitForCloudResumeAsync(cloudIndex, plannedCloudImages, candidate.SourcePath);
                        fallbackRetry = 0;
                    }
                    catch (OcrException exception)
                    {
                        setError(exception.Message);
                        return null;
                    }
                }
            }

            if (issueCheckCandidates.Length == issueCheckCount)
            {
                SetProgress(0, issueCheckCount);
                for (int index = 0; index < issueCheckCandidates.Length; index++)
                {
                    RecognitionCandidate candidate = issueCheckCandidates[index];
                    OcrEvidence precheckEvidence = await RecognizePrimaryDeduplicatedAsync(
                        candidate, index + 1, issueCheckCount, "云 OCR 期数检查");
                    checkedCloudEvidence[CloudEvidenceKey(precheckEvidence)] = precheckEvidence;
                    issueCheckEvidence.Add(precheckEvidence);
                    SetProgress(index + 1, issueCheckCount);
                }

                int? detectedIssue = RuleEngine.DetectIssueMismatch(
                    issueCheckEvidence.Select(evidence => evidence.Lines),
                    issue);
                if (detectedIssue is not null)
                {
                    string message = $"软件当前选择：{issue}期{Environment.NewLine}前10张图片主要识别到：{detectedIssue}期{Environment.NewLine}图片期数与软件期数不一致，已停止云 OCR。请修改期数后重新开始。";
                    statusLabel.Text = $"已停止：图片主要为 {detectedIssue}期，软件当前选择 {issue}期。";
                    MessageBox.Show(this, message, "期数不一致", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            SetProgress(0, plannedCloudImages);
            int candidateImages = 0;
            foreach (RecognitionCandidate candidate in candidates)
            {
                // Selected competing candidates must be compared, not skipped after first success.
                OcrRule[] activeRules = candidate.Rules.ToArray();
                if (activeRules.Length == 0)
                    continue;

                if (!candidate.IsPrimary)
                    plannedCloudImages++;
                candidateImages++;
                int cloudIndex = candidateImages;
                OcrEvidence cloudEvidence;
                OcrEvidenceIdentity currentPrimaryIdentity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.OcrPath, $"cloud-primary/{candidate.SelectionMode}");
                if (!checkedCloudEvidence.TryGetValue(CloudEvidenceKey(currentPrimaryIdentity), out cloudEvidence!))
                {
                    cloudEvidence = await RecognizePrimaryDeduplicatedAsync(
                        candidate, cloudIndex, plannedCloudImages, "云 OCR");
                }
                IReadOnlyList<string> cloudLines = cloudEvidence.Lines;

                SetProgress(cloudIndex, plannedCloudImages);
                var matchedValues = new List<string>();
                var missingRules = new List<OcrRule>();
                foreach (OcrRule rule in activeRules)
                {
                    string? value = RuleEngine.ExtractFinalValue(cloudEvidence, issue, rule);
                    if (value is not null)
                    {
                        evidenceLedger.Observe(values, rule, value, cloudEvidence);
                        matchedValues.Add($"{value} {rule.OutputLabel}");
                    }
                    else if (!values.ContainsKey(rule.Id))
                    {
                        missingRules.Add(rule);
                    }
                }

                OcrEvidence? fallbackEvidence = null;
                string? fallbackError = null;
                if (missingRules.Count > 0)
                {
                    fallbackEvidence = await fallbackImageDeduplicator.RecognizeEvidenceAsync(
                        selectedImageDirectory!, candidate.SourcePath, candidate.Rules,
                        async () => await RecognizeFallbackAsync(candidate, cloudIndex, error => fallbackError = error)
                            ?? OcrEvidence.FromLines(candidate.SourcePath, [], "fallback-empty"));

                    if (fallbackEvidence.Items.Count > 0)
                    {
                        foreach (OcrRule rule in activeRules)
                        {
                            string? value = RuleEngine.ExtractFinalValue(fallbackEvidence, issue, rule);
                            if (value is null)
                                continue;
                            evidenceLedger.Observe(values, rule, value, fallbackEvidence);
                            matchedValues.Add($"{value} {rule.OutputLabel}");
                        }
                    }
                }
                IReadOnlyList<string>? fallbackLines = fallbackEvidence?.Lines;

                bool recognizedText = cloudEvidence.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text))
                    || fallbackEvidence?.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)) == true;
                lastCloudOcrResults[candidate.SourcePath] = cloudLines;
                CloudOcrCacheStore.SaveEntry(
                    AppContext.BaseDirectory, groupName, issue, cloudEvidence);
                if (recognizedText)
                    textRecognizedRuleIds.UnionWith(activeRules.Select(rule => rule.Id));
                foreach (OcrRule rule in activeRules)
                {
                    if (values.ContainsKey(rule.Id))
                        missingReasons.Remove(rule.Id);
                    else
                        missingReasons[rule.Id] = RuleEngine.DescribeMissing(
                            foundImage: true,
                            recognizedText: textRecognizedRuleIds.Contains(rule.Id));
                }

                diagnostics.Add(new
                {
                    file = Path.GetFileName(candidate.SourcePath),
                    ocr_crop = candidate.OcrPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : Path.GetFileName(candidate.OcrPath),
                    selection = candidate.SelectionMode,
                    template_distance = candidate.TemplateDistance,
                    rules = activeRules.Select(rule => rule.Id),
                    matches = matchedValues,
                    primary_provider = credential.DisplayName,
                    primary_lines = cloudLines,
                    primary_source_hash = cloudEvidence.SourceHash,
                    primary_input_hash = cloudEvidence.InputHash,
                    primary_view = cloudEvidence.ViewId,
                    primary_regions = cloudEvidence.Items.Select(item => item.ViewId + "/" + item.RegionId).Distinct(),
                    primary_minimum_confidence = cloudEvidence.MinimumConfidence,
                    fallback_provider = fallbackEvidence is null ? null : fallbackCredential.DisplayName,
                    fallback_lines = fallbackLines,
                    fallback_source_hash = fallbackEvidence?.SourceHash,
                    fallback_input_hash = fallbackEvidence?.InputHash,
                    fallback_view = fallbackEvidence?.ViewId,
                    fallback_regions = fallbackEvidence?.Items.Select(item => item.ViewId + "/" + item.RegionId).Distinct(),
                    fallback_minimum_confidence = fallbackEvidence?.MinimumConfidence,
                    fallback_error = fallbackError
                });
            }

            ActiveToken.ThrowIfCancellationRequested();
            string[] outputLines = RuleEngine.FormatOutput(rules, values, missingReasons);
            ResultFilePaths.EnsureOutputDirectories(AppContext.BaseDirectory);
            string groupOutputPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory!, issue);
            string diagnosticPath = ResultFilePaths.ForDiagnostic(
                AppContext.BaseDirectory, selectedImageDirectory!, issue);
            await AtomicFile.WriteAllLinesAsync(groupOutputPath, outputLines, new UTF8Encoding(true));
            await RecognitionStateStore.SaveAsync(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules, values, evidenceLedger, ActiveToken);
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, issue, outputLines);
            string[] groupLines = GroupResultFormatter.Format(
                rules,
                ResultDistributor.MarkDistributedLines(outputLines, distribution.DistributedLines));
            await AtomicFile.WriteAllLinesAsync(groupOutputPath, groupLines, new UTF8Encoding(true));
            await AtomicFile.WriteAllTextAsync(
                diagnosticPath,
                JsonSerializer.Serialize(new
                {
                    group = groupName,
                    issue,
                    image_count = imagePaths.Length,
                    candidate_selection = selection.DisplayName,
                    candidate_image_count = candidateImages,
                    cloud_request_count = cloudRequests,
                    matched_rule_count = values.Count,
                    missing_rules = rules.Select(rule => rule.Id).Where(id => !values.ContainsKey(id)),
                    missing_reasons = rules
                        .Where(rule => !values.ContainsKey(rule.Id))
                        .ToDictionary(rule => rule.Id, rule => missingReasons[rule.Id]),
                    images = diagnostics
                }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(true));
            LogMissingDetails("开始识别", groupName, issue, rules, values, candidates);
            resultsBox.Text = string.Join(Environment.NewLine, groupLines);
            copyButton.Enabled = outputLines.Length > 0;
            lastRules = rules;
            lastValues = new ResultValues(values, StringComparer.Ordinal);
            lastMissingReasons = new Dictionary<string, string>(missingReasons, StringComparer.Ordinal);
            lastTextRecognizedRuleIds = new HashSet<string>(textRecognizedRuleIds, StringComparer.Ordinal);
            lastEvidenceLedger = evidenceLedger;
            lastIssue = issue;
            SetProgress(1, 1);
            statusLabel.Text = $"完成{(rules.Count == values.Count ? "" : "，但有缺失")}：目录 {imagePaths.Length} 张，候选 {candidateImages} 张，云 OCR 请求 {cloudRequests} 次，提取 {values.Count} 条，缺失 {rules.Count - values.Count} 条，成功分流 {distribution.DistributedLines.Count} 条{DistributionErrorText(distribution)}。群TXT：{groupOutputPath}；诊断：{diagnosticPath}";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "识别已取消；未完成结果不会自动分流。";
        }
        catch (OcrException exception)
        {
            statusLabel.Text = "识别失败。";
            MessageBox.Show(this, exception.Message, "识别失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            OperationLog.AppendException(AppContext.BaseDirectory, "开始识别", RuleCatalog.BaseGroupName(selectedImageDirectory ?? ""), issue, exception);
            statusLabel.Text = "识别失败。";
            MessageBox.Show(this, "发生未预期错误，请关闭软件后重试。", "识别失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (temporaryCropFolder is not null)
            {
                try { Directory.Delete(temporaryCropFolder, recursive: true); } catch { }
            }
            StopRecognitionTiming();
            SetBusy(false);
            RefreshCredentialLabel();
        }
    }

    private async void ManualDistributeAsync(object? sender, EventArgs e)
    {
        if (!CanManualDistribute())
        {
            statusLabel.Text = "请先选择资料文件夹，并确认当前群已有结果。";
            return;
        }

        int issue = Decimal.ToInt32(issueInput.Value);
        SetBusy(true);
        try
        {
            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
                ?? RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedImageDirectory!));
            string[] outputLines = RecognitionStateStore.BuildTrustedOutputLines(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules);
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, issue, outputLines);
            string[] groupLines = GroupResultFormatter.Format(
                rules,
                ResultDistributor.MarkDistributedLines(outputLines, distribution.DistributedLines));
            string groupOutputPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory!, issue);
            ResultFilePaths.EnsureOutputDirectories(AppContext.BaseDirectory);
            await AtomicFile.WriteAllLinesAsync(groupOutputPath, groupLines, new UTF8Encoding(true));
            resultsBox.Text = string.Join(Environment.NewLine, groupLines);
            copyButton.Enabled = outputLines.Length > 0;
            statusLabel.Text = $"手动分流完成：成功 {distribution.DistributedLines.Count} 条{DistributionErrorText(distribution)}。群TXT：{groupOutputPath}";
        }
        catch (OcrException exception)
        {
            statusLabel.Text = $"手动分流失败：{exception.Message}";
        }
        catch (Exception exception)
        {
            OperationLog.AppendException(AppContext.BaseDirectory, "手动分流", RuleCatalog.BaseGroupName(selectedImageDirectory ?? ""), (int)issueInput.Value, exception);
            statusLabel.Text = "手动分流失败，请稍后重试。";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RetryMissingAsync(object? sender, EventArgs e)
    {
        if (!CanRetryMissing())
            return;

        string? temporaryCropFolder = null;
        SetBusy(true);
        StartRecognitionTiming();
        try
        {
            RefreshImagesForRetry();
            OcrRule[] missingRules = lastRules.Where(rule => !lastValues.ContainsKey(rule.Id)).ToArray();
            string groupName = RuleCatalog.GroupNameForFolder(AppContext.BaseDirectory, selectedImageDirectory!);
            // Revalidate disk/image identity; structured cache entries retain the
            // exact source hash and OCR evidence. Known conflicts stay sticky.
            Dictionary<string, OcrEvidence> retryCloudEvidence =
                CloudOcrCacheStore.LoadEvidence(AppContext.BaseDirectory, groupName, lastIssue);
            lastCloudOcrResults = retryCloudEvidence.ToDictionary(
                pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Lines.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            CandidateSelection selection = await SelectCandidatesAsync(
                missingRules,
                lastIssue,
                allowTemplateSubset: true,
                completeRuleIds: lastRules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal),
                localLimitedRuleIds: LimitedLocalRetryRuleIds(missingRules));
            temporaryCropFolder = selection.TemporaryCropFolder;
            HashSet<string> retryCandidateRuleIds = selection.Candidates
                .SelectMany(candidate => candidate.Rules)
                .Select(rule => rule.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (OcrRule rule in missingRules.Where(rule => !retryCandidateRuleIds.Contains(rule.Id)))
                lastMissingReasons.TryAdd(
                    rule.Id,
                    RuleEngine.DescribeMissing(foundImage: false, recognizedText: false));

            OcrCredential credential = credentialSelector.SelectedIndex <= 0
                ? CredentialSchedule.DescribeDate(CredentialSchedule.TodayInBeijing())
                : CredentialSchedule.DescribeSlot(credentialSelector.SelectedIndex - 1);
            IOcrClient cloudClient = OcrClientFactory.CreateDeferred(credential);
            OcrCredential fallbackCredential = CredentialSchedule.DescribeFallback(credential);
            IOcrClient fallbackClient = OcrClientFactory.CreateDeferred(fallbackCredential);
            var primarySpacing = Stopwatch.StartNew();
            var fallbackSpacing = Stopwatch.StartNew();
            var primaryImageDeduplicator = new CloudImageDeduplicator();
            var fallbackImageDeduplicator = new CloudImageDeduplicator();
            bool primaryStarted = false;
            bool fallbackStarted = false;
            int cloudRequests = 0;
            int completed = 0;
            SetProgress(0, selection.Candidates.Count);

            foreach (RecognitionCandidate candidate in selection.Candidates)
            {
                // Selection contains the rules missing at retry start, but an already
                // obtained whole-image response must also be compared against sibling rules
                // that share the same configured source folder.
                OcrRule[] candidateMissing = candidate.Rules.ToArray();
                if (candidateMissing.Length == 0)
                    continue;
                OcrRule[] evidenceRules = RetryEvidenceRules(candidate, lastRules);

                OcrEvidence? primaryEvidence = null;
                bool reusedCache = retryCloudEvidence.TryGetValue(candidate.SourcePath, out OcrEvidence? cachedEvidence)
                    && CanReuseRetryCloudEvidence(lastValues, candidateMissing, cachedEvidence, lastIssue);
                if (reusedCache)
                {
                    primaryEvidence = cachedEvidence!;
                    AddExtractedEvidenceValues(primaryEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);
                }
                else
                {
                    if (primaryStarted)
                        await WaitForPacingAsync(primarySpacing, CloudOcrPolicy.MinimumInterval(credential.Provider));
                    primarySpacing.Restart();
                    primaryStarted = true;
                    try
                    {
                        statusLabel.Text = $"复抓缺失：主云 {completed + 1}/{selection.Candidates.Count} · {ShortPath(candidate.SourcePath)}";
                        string primaryInputPath = RetryPrimaryImage(
                            selectedImageDirectory!, candidate.SourcePath, candidate.OcrPath, candidateMissing);
                        primaryEvidence = await primaryImageDeduplicator.RecognizeEvidenceAsync(
                            selectedImageDirectory!, primaryInputPath, candidateMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryEvidenceAsync(
                                    cloudClient, credential, candidate.SourcePath, primaryInputPath,
                                    completed + 1, selection.Candidates.Count);
                            });
                        AddExtractedEvidenceValues(primaryEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);
                    }
                    catch (OcrException exception)
                    {
                        statusLabel.Text = $"复抓主云失败，改用 {fallbackCredential.DisplayName}：{exception.Message}";
                    }
                }

                OcrRule[] fallbackMissing = candidateMissing
                    .Where(rule => !lastValues.ContainsKey(rule.Id))
                    .ToArray();
                OcrEvidence? fallbackEvidence = null;
                if (fallbackMissing.Length > 0)
                {
                    if (fallbackStarted)
                        await WaitForPacingAsync(fallbackSpacing, CloudOcrPolicy.MinimumInterval(fallbackCredential.Provider));
                    fallbackSpacing.Restart();
                    fallbackStarted = true;
                    try
                    {
                        statusLabel.Text = $"复抓缺失：备用 {fallbackCredential.DisplayName} {completed + 1}/{selection.Candidates.Count} · {ShortPath(candidate.SourcePath)}";
                        fallbackEvidence = await fallbackImageDeduplicator.RecognizeEvidenceAsync(
                            selectedImageDirectory!, candidate.SourcePath, fallbackMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryEvidenceAsync(
                                    fallbackClient, fallbackCredential, candidate.SourcePath, candidate.SourcePath,
                                    completed + 1, selection.Candidates.Count);
                            });
                        AddExtractedEvidenceValues(fallbackEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);
                    }
                    catch (OcrException)
                    {
                        // 保留“缺失”，允许用户再次复抓。
                    }
                }

                if (!reusedCache && primaryEvidence is not null)
                {
                    lastCloudOcrResults[candidate.SourcePath] = primaryEvidence.Lines;
                    CloudOcrCacheStore.SaveEntry(
                        AppContext.BaseDirectory, groupName, lastIssue, primaryEvidence);
                }

                bool recognizedText = primaryEvidence?.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)) == true
                    || fallbackEvidence?.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)) == true;
                if (recognizedText)
                    lastTextRecognizedRuleIds.UnionWith(candidate.Rules.Select(rule => rule.Id));
                foreach (OcrRule rule in candidate.Rules)
                {
                    if (lastValues.ContainsKey(rule.Id))
                        lastMissingReasons.Remove(rule.Id);
                    else
                        lastMissingReasons[rule.Id] = RuleEngine.DescribeMissing(
                            foundImage: true,
                            recognizedText: lastTextRecognizedRuleIds.Contains(rule.Id));
                }

                completed++;
                SetProgress(completed, selection.Candidates.Count);
                resultsBox.Text = string.Join(
                    Environment.NewLine,
                    RuleEngine.FormatOutput(lastRules, lastValues, lastMissingReasons));
            }

            ActiveToken.ThrowIfCancellationRequested();
            string[] outputLines = RuleEngine.FormatOutput(lastRules, lastValues, lastMissingReasons);
            ResultFilePaths.EnsureOutputDirectories(AppContext.BaseDirectory);
            string groupOutputPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory!, lastIssue);
            await AtomicFile.WriteAllLinesAsync(groupOutputPath, outputLines, new UTF8Encoding(true));
            await RecognitionStateStore.SaveAsync(
                AppContext.BaseDirectory, selectedImageDirectory!, lastIssue, lastRules, lastValues, lastEvidenceLedger, ActiveToken);
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, lastIssue, outputLines);
            string[] groupLines = GroupResultFormatter.Format(
                lastRules,
                ResultDistributor.MarkDistributedLines(outputLines, distribution.DistributedLines));
            await AtomicFile.WriteAllLinesAsync(groupOutputPath, groupLines, new UTF8Encoding(true));
            LogMissingDetails(
                "手动复抓缺失", groupName, lastIssue, lastRules, lastValues, selection.Candidates);
            resultsBox.Text = string.Join(Environment.NewLine, groupLines);
            copyButton.Enabled = outputLines.Length > 0;
            int remaining = lastRules.Count(rule => !lastValues.ContainsKey(rule.Id));
            statusLabel.Text = selection.Candidates.Count == 0
                ? $"复抓未找到 {missingRules.Length} 条缺失项对应的图片，缺失原因已更新；成功分流 {distribution.DistributedLines.Count} 条{DistributionErrorText(distribution)}。TXT：{groupOutputPath}"
                : $"复抓完成：云 OCR 请求 {cloudRequests} 次，补回 {missingRules.Length - remaining} 条，仍缺失 {remaining} 条，成功分流 {distribution.DistributedLines.Count} 条{DistributionErrorText(distribution)}。TXT：{groupOutputPath}";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "识别已取消；未完成结果不会自动分流。";
        }
        catch (OcrException exception)
        {
            statusLabel.Text = $"复抓失败：{exception.Message}";
        }
        catch (Exception exception)
        {
            OperationLog.AppendException(AppContext.BaseDirectory, "手动复抓", RuleCatalog.BaseGroupName(selectedImageDirectory ?? ""), lastIssue, exception);
            statusLabel.Text = "复抓失败，请稍后重试。";
        }
        finally
        {
            if (temporaryCropFolder is not null)
            {
                try { Directory.Delete(temporaryCropFolder, recursive: true); } catch { }
            }
            StopRecognitionTiming();
            SetBusy(false);
            RefreshCredentialLabel();
        }
    }

    private void RefreshImagesForRetry() =>
        imagePaths = ImageFolderScanner.Scan(selectedImageDirectory!);

    private async Task<OcrEvidence> RecognizeRetryEvidenceAsync(
        IOcrClient client,
        OcrCredential credential,
        string sourcePath,
        string inputPath,
        int current,
        int total)
    {
        OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
            sourcePath, inputPath, $"retry/{credential.Provider}");
        int retry = 0;
        while (true)
        {
            try
            {
                OcrEvidence evidence = await client.RecognizeEvidenceAsync(inputPath, ActiveToken);
                return evidence.Bind(identity);
            }
            catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(credential.Provider, exception))
            {
                if (retry < CloudOcrPolicy.MaxAutomaticRetries)
                {
                    TimeSpan delay = CloudOcrPolicy.RetryDelay(++retry);
                    statusLabel.Text = $"复抓触发限流：{delay.TotalSeconds:0} 秒后重试 {current}/{total}";
                    await Task.Delay(delay, ActiveToken);
                    continue;
                }

                await WaitForCloudResumeAsync(current, total, sourcePath);
                retry = 0;
            }
        }
    }

    private async Task<IReadOnlyList<string>> RecognizeRetryAsync(
        IOcrClient client,
        OcrCredential credential,
        string imagePath,
        int current,
        int total,
        IReadOnlyList<OcrRule> rules,
        int issue,
        IDictionary<string, string> values)
    {
        int retry = 0;
        while (true)
        {
            try
            {
                IReadOnlyList<string> lines = await client.RecognizeAsync(imagePath, ActiveToken);
                AddExtractedValues(lines, rules, issue, values);
                return lines;
            }
            catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(credential.Provider, exception))
            {
                if (retry < CloudOcrPolicy.MaxAutomaticRetries)
                {
                    TimeSpan delay = CloudOcrPolicy.RetryDelay(++retry);
                    statusLabel.Text = $"复抓触发限流：{delay.TotalSeconds:0} 秒后重试 {current}/{total}";
                    await Task.Delay(delay, ActiveToken);
                    continue;
                }

                await WaitForCloudResumeAsync(current, total, imagePath);
                retry = 0;
            }
        }
    }

    private async Task WaitForPacingAsync(Stopwatch spacing, TimeSpan minimumInterval)
    {
        TimeSpan delay = minimumInterval - spacing.Elapsed;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ActiveToken);
    }

    private static void AddExtractedValues(
        IReadOnlyList<string> lines,
        IEnumerable<OcrRule> rules,
        int issue,
        IDictionary<string, string> values)
    {
        foreach (OcrRule rule in rules)
        {
            RuleExtractionResult result = RuleEngine.ExtractFinalResult(lines, issue, rule);
            if (result.Status == RuleExtractionStatus.Conflict)
                ResultValues.MarkConflict(values, rule.Id);
            else if (result.Status == RuleExtractionStatus.Success)
                ResultValues.AddTo(values, rule.Id, result.Value!);
        }
    }

    internal static void AddExtractedEvidenceValues(
        OcrEvidence evidence,
        IEnumerable<OcrRule> rules,
        int issue,
        ResultValues values,
        ResultEvidenceLedger ledger)
    {
        foreach (OcrRule rule in rules)
        {
            RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, issue, rule);
            if (result.Status == RuleExtractionStatus.Conflict)
                ledger.ObserveConflict(values, rule, evidence);
            else if (result.Status == RuleExtractionStatus.Success)
                ledger.Observe(values, rule, result.Value!, evidence);
        }
    }

    internal static bool IsEvidenceCurrent(OcrEvidence evidence)
    {
        try
        {
            OcrEvidenceIdentity current = OcrEvidenceIdentity.Capture(
                evidence.SourcePath, evidence.InputPath, evidence.ViewId);
            return current.SourceHash.Equals(evidence.SourceHash, StringComparison.OrdinalIgnoreCase)
                && current.InputHash.Equals(evidence.InputHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (OcrException)
        {
            return false;
        }
    }

    internal static string CloudEvidenceKey(OcrEvidenceIdentity identity) =>
        string.Join("|", identity.SourcePath, identity.SourceHash, identity.InputPath, identity.InputHash, identity.ViewId);

    private static string CloudEvidenceKey(OcrEvidence evidence) =>
        string.Join("|", evidence.SourcePath, evidence.SourceHash, evidence.InputPath, evidence.InputHash, evidence.ViewId);

    internal static bool CanReuseRetryCloudEvidence(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<OcrRule> rules,
        OcrEvidence evidence,
        int issue)
    {
        if (rules.Any(rule => ResultValues.IsConflict(values, rule.Id)))
            return false;
        try
        {
            if (!File.Exists(evidence.SourcePath) ||
                !LocalOcrIdentity.Image(evidence.SourcePath).Equals(evidence.SourceHash, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
        return rules.Any(rule => RuleEngine.ExtractFinalValue(evidence, issue, rule) is not null);
    }

    private static OcrRule[] RetryEvidenceRules(RecognitionCandidate candidate, IReadOnlyList<OcrRule> allRules)
    {
        HashSet<string> selectedIds = candidate.Rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> folders = candidate.Rules
            .Select(rule => rule.Folder)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return allRules.Where(rule => selectedIds.Contains(rule.Id)
            || !string.IsNullOrWhiteSpace(rule.Folder) && folders.Contains(rule.Folder!)).ToArray();
    }

    private bool CanRetryMissing() =>
        selectedImageDirectory is not null
        && Decimal.ToInt32(issueInput.Value) == lastIssue
        && lastRules.Any(rule => !lastValues.ContainsKey(rule.Id));

    private bool CanManualDistribute() =>
        selectedImageDirectory is not null
        && File.Exists(ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value)));

    private void LoadExistingGroupResult()
    {
        if (isBusy) return;
        if (selectedImageDirectory is null)
        {
            ClearRetryState();
            resultsBox.Clear();
            copyButton.Enabled = false;
            return;
        }

        string path = ResultFilePaths.ForGroup(
            AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value));
        try
        {
            if (!File.Exists(path))
            {
                resultsBox.Clear();
                copyButton.Enabled = false;
                retryMissingButton.Enabled = CanRetryMissing();
                return;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8)
                .Select(GroupResultFormatter.RemoveLegacySourceSuffix).ToArray();
            resultsBox.Text = string.Join(Environment.NewLine, lines);
            copyButton.Enabled = resultsBox.TextLength > 0;
            RestoreRetryState(lines);
        }
        catch (Exception exception) when (exception is IOException or OcrException or JsonException)
        {
            ClearRetryState();
            resultsBox.Clear();
            copyButton.Enabled = false;
        }
    }

    private void RestoreRetryState(IReadOnlyList<string> lines)
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
            ?? throw new OcrException("请先选择要读取的子文件夹。"));
        lastRules = rules;
        lastMissingReasons = new Dictionary<string, string>(StringComparer.Ordinal);
        lastTextRecognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        lastIssue = Decimal.ToInt32(issueInput.Value);

        RecognitionStateLoad restored = selectedImageDirectory is null
            ? new(new ResultValues(StringComparer.Ordinal), new ResultEvidenceLedger())
            : RecognitionStateStore.Load(
                AppContext.BaseDirectory, selectedImageDirectory, lastIssue, rules);
        lastValues = restored.Values;
        lastEvidenceLedger = restored.Evidence;
        lastTextRecognizedRuleIds.UnionWith(lastValues.Keys);

        foreach (string line in lines)
        {
            OcrRule? rule = rules
                .OrderByDescending(candidate => candidate.OutputLabel.Length)
                .FirstOrDefault(candidate =>
                    line.EndsWith(candidate.OutputLabel + "（已分流）", StringComparison.Ordinal) ||
                    line.EndsWith(candidate.OutputLabel, StringComparison.Ordinal));
            if (rule is null)
                continue;

            string distributedSuffix = rule.OutputLabel + "（已分流）";
            string suffix = line.EndsWith(distributedSuffix, StringComparison.Ordinal)
                ? distributedSuffix
                : rule.OutputLabel;
            string value = line[..^suffix.Length].Trim();
            if (value.StartsWith("缺失", StringComparison.Ordinal))
            {
                if (!lastValues.ContainsKey(rule.Id))
                    lastMissingReasons[rule.Id] = MissingReasonFrom(value);
                continue;
            }

            if (lastValues.ContainsKey(rule.Id))
                continue; // Structured state, not the TXT text, is the trusted source.
            lastMissingReasons[rule.Id] = RuleEngine.IsFormattedOutputValueValid(rule, value)
                ? "保存TXT仅用于展示，未找到有效来源状态"
                : "保存结果未通过当前规则校验";
        }

        retryMissingButton.Enabled = CanRetryMissing();
    }

    private static string MissingReasonFrom(string value)
    {
        const string prefix = "缺失（";
        if (value.StartsWith(prefix, StringComparison.Ordinal))
        {
            int end = value.IndexOf('）', prefix.Length);
            if (end > prefix.Length)
                return value[prefix.Length..end];
        }

        return RuleEngine.DescribeMissing(foundImage: false, recognizedText: false);
    }

    private void ClearRetryState()
    {
        lastRules = [];
        lastValues = new ResultValues(StringComparer.Ordinal);
        lastMissingReasons = new Dictionary<string, string>(StringComparer.Ordinal);
        lastTextRecognizedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        lastCloudOcrResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        lastEvidenceLedger = new ResultEvidenceLedger();
        lastIssue = 0;
        retryMissingButton.Enabled = false;
        manualDistributeButton.Enabled = CanManualDistribute();
    }

    private async Task<CandidateSelection> SelectCandidatesAsync(
        IReadOnlyList<OcrRule> rules,
        int issue,
        bool allowTemplateSubset,
        IReadOnlySet<string> completeRuleIds,
        IReadOnlySet<string>? localLimitedRuleIds = null)
    {
        var templateCandidates = new List<RecognitionCandidate>();
        string? templateCropFolder = null;
        bool deferUnmatchedTemplates = DeferUnmatchedTemplates(allowTemplateSubset);
        if (selectedImageDirectory is not null && VisualTemplateMatcher.Supports(selectedImageDirectory))
        {
            string templatePath = VisualTemplateMatcher.ConfigPath(
                AppContext.BaseDirectory, selectedImageDirectory);
            if (File.Exists(templatePath) || deferUnmatchedTemplates)
            {
                try
                {
                    VisualTemplateSet catalog = VisualTemplateMatcher.Load(templatePath);
                    var ruleMap = rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
                    string[] configuredIds = catalog.Templates.SelectMany(item => item.RuleIds).ToArray();
                    HashSet<string> configuredRuleIds = configuredIds.ToHashSet(StringComparer.Ordinal);
                    if (configuredIds.Distinct(StringComparer.Ordinal).Count() != configuredIds.Length ||
                        !configuredRuleIds.SetEquals(completeRuleIds))
                        throw new OcrException($"{catalog.Folder}标题模板与规则不一致。");

                    IReadOnlyList<VisualTemplateDefinition> templates = allowTemplateSubset
                        ? VisualTemplateMatcher.SelectForRules(
                            catalog.Templates,
                            ruleMap.Keys.ToHashSet(StringComparer.Ordinal))
                        : catalog.Templates;
                    if ((!allowTemplateSubset && !configuredRuleIds.SetEquals(ruleMap.Keys)) ||
                        templates.Count == 0)
                        throw new OcrException($"{catalog.Folder}标题模板与规则不一致。");

                    var watch = Stopwatch.StartNew();
                    var progress = new Progress<VisualTemplateProgress>(item =>
                    {
                        SetProgress(item.Completed, item.Total);
                        int percent = item.Total == 0 ? 0 : item.Completed * 100 / item.Total;
                        statusLabel.Text = $"标题模板：{item.Completed}/{item.Total}（{percent}%） · 当前：{ShortPath(item.Path)} · 已用 {FormatDuration(watch.Elapsed)}";
                    });
                    IReadOnlyList<VisualTemplateMatch> matches = await Task.Run(() =>
                        VisualTemplateMatcher.Match(imagePaths, templates, catalog.MaxDistance, progress)
                            .Where(match => match.Template.RuleIds.All(ruleMap.ContainsKey))
                            .ToArray());
                    bool hasCompleteTemplateMatches = VisualTemplateMatcher.HasUsableMatches(
                        matches,
                        templates,
                        ruleMap.Keys.ToHashSet(StringComparer.Ordinal));
                    if (deferUnmatchedTemplates && matches.Count == 0)
                        return new CandidateSelection([], null, "标题模板（未命中，待手动复抓）");

                    if (hasCompleteTemplateMatches || ((allowTemplateSubset || deferUnmatchedTemplates) && matches.Count > 0))
                    {
                        templateCropFolder = Path.Combine(
                            Path.GetTempPath(), "OcrLineTool", "visual-templates-" + Guid.NewGuid().ToString("N"));
                        templateCandidates = new List<RecognitionCandidate>(matches.Count);
                        int index = 0;
                        foreach (VisualTemplateMatch match in matches)
                        {
                            string cropPath = Path.Combine(templateCropFolder, $"{index++:D2}.png");
                            string ocrPath = cropPath;
                            try
                            {
                                VisualTemplateMatcher.CreateCrop(match, cropPath,
                                    includeRemainingRows: match.Template.RuleIds.Any(id =>
                                        ruleMap[id].StrictIssueBlock && !ruleMap[id].AllowNearbyValue),
                                    scale: match.Template.RuleIds.Contains("翩翩公子尾") ? 2 : 1);
                            }
                            catch (Exception exception) when (exception is ArgumentException or IOException or ExternalException)
                            {
                                ocrPath = match.SourcePath;
                            }
                            templateCandidates.Add(new RecognitionCandidate(
                                match.SourcePath,
                                ocrPath,
                                match.Template.RuleIds.Select(id => ruleMap[id]).ToArray(),
                                true,
                                "标题模板",
                                match.Distance));
                        }

                        if (hasCompleteTemplateMatches)
                            return new CandidateSelection(templateCandidates, templateCropFolder, "标题模板");

                        if (deferUnmatchedTemplates)
                            return new CandidateSelection(templateCandidates, templateCropFolder,
                                "标题模板（部分命中，未匹配项待手动复抓）");

                        HashSet<string> matchedRuleIds = templateCandidates
                            .SelectMany(candidate => candidate.Rules)
                            .Select(rule => rule.Id)
                            .ToHashSet(StringComparer.Ordinal);
                        rules = rules.Where(rule => !matchedRuleIds.Contains(rule.Id)).ToArray();
                        if (rules.Count == 0)
                            return new CandidateSelection(templateCandidates, templateCropFolder, "标题模板");
                    }
                    else
                    {
                        statusLabel.Text = $"标题模板未命中可用图片（{matches.Count}/{templates.Count}），正在回退本地 OCR……";
                    }
                }
                catch (OcrException exception)
                {
                    if (deferUnmatchedTemplates)
                        throw;
                    statusLabel.Text = $"标题模板不可用（{exception.Message}），正在回退本地 OCR……";
                }
            }
        }

        var localWatch = Stopwatch.StartNew();
        var localProgress = new Progress<LocalOcrProgress>(item =>
        {
            if (string.IsNullOrEmpty(item.Path))
            {
                SetIndeterminateProgress();
                statusLabel.Text = $"{item.Stage} 已用 {FormatDuration(localWatch.Elapsed)}";
                return;
            }

            SetProgress(item.Completed, item.Total);
            int percent = item.Total == 0 ? 0 : item.Completed * 100 / item.Total;
            TimeSpan remaining = item.Completed == 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(localWatch.Elapsed.TotalSeconds / item.Completed * (item.Total - item.Completed));
            statusLabel.Text = $"本地 OCR：{item.Completed}/{item.Total}（{percent}%） · 当前：{ShortPath(item.Path)} · 已用 {FormatDuration(localWatch.Elapsed)} · 预计剩余 {FormatDuration(remaining)}";
        });
        IReadOnlyList<string> localImagePaths = imagePaths;
        HashSet<string> limitedImagePaths = localLimitedRuleIds is null
            ? []
            : imagePaths.Take(60).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (localLimitedRuleIds is not null && rules.All(rule => localLimitedRuleIds.Contains(rule.Id)))
            localImagePaths = imagePaths.Take(60).ToArray();

        var localClient = new PaddleLocalOcrClient();
        IReadOnlyDictionary<string, IReadOnlyList<string>> localResults =
            await localClient.RecognizeBatchAsync(
                localImagePaths,
                localProgress,
                LocalRetryTitleRatio(localLimitedRuleIds),
                PaddleLocalOcrClient.DetectionMaxSideFor(selectedImageDirectory!), cancellationToken: ActiveToken);
        var candidateResults = new Dictionary<string, IReadOnlyList<string>>(localResults, StringComparer.OrdinalIgnoreCase);
        foreach ((string failedPath, string error) in localClient.LastImageErrors)
        {
            candidateResults[failedPath] = [];
            statusLabel.Text = $"本地 OCR 单图失败：{ShortPath(failedPath)}；{error}";
        }
        localResults = candidateResults;
        await Task.Yield();
        var localCandidates = new List<RecognitionCandidate>();
        IReadOnlyList<OcrRule> completeIdentityRules = selectedRulePath is null
            ? rules
            : RuleCatalog.Load(selectedRulePath);
        bool isYanran = RuleCatalog.IsGroupFolder(selectedImageDirectory!, "嫣然心水");
        if (isYanran)
        {
            foreach (LocalCandidatePlan plan in LocalCandidatePlanner.Build(
                imagePaths, localResults, rules, issue, completeIdentityRules))
            {
                string ocrPath = PrepareLocalCloudImage(selectedImageDirectory!, plan.Path, plan.Rules, ref templateCropFolder);
                localCandidates.Add(new RecognitionCandidate(
                    plan.Path,
                    ocrPath,
                    plan.Rules,
                    plan.IsPrimary,
                    (plan.IsPrimary ? "本地OCR首选" : "本地OCR备选") + (ocrPath == plan.Path ? "" : "（杰少密集表横向压缩整图）"),
                    null,
                    localResults.TryGetValue(plan.Path, out IReadOnlyList<string>? planLines) ? planLines : [],
                    BindPaddleEvidence(localClient, plan.Path, plan.Path, "candidate-small")));
            }
            return new CandidateSelection(templateCandidates.Concat(localCandidates).ToArray(), templateCropFolder, "本地OCR");
        }

        foreach (string path in localImagePaths)
        {
            if (!localResults.TryGetValue(path, out IReadOnlyList<string>? lines))
                continue;
            IReadOnlyList<OcrRule> matched = RuleEngine.FindMatches(path, lines, rules, completeIdentityRules);
            if (localLimitedRuleIds is not null && limitedImagePaths.Count > 0 &&
                !limitedImagePaths.Contains(path))
            {
                matched = matched
                    .Where(rule => !localLimitedRuleIds.Contains(rule.Id))
                    .ToArray();
            }
            if (matched.Count > 0)
                localCandidates.Add(new RecognitionCandidate(
                    path, path, matched, true, "本地OCR", null, lines,
                    BindPaddleEvidence(localClient, path, path, "candidate-small")));
        }
        return new CandidateSelection(templateCandidates.Concat(localCandidates).ToArray(), templateCropFolder, "本地OCR");
    }

    internal static IReadOnlyList<OcrRule> RulesForAlreadyRequestedCloudFallback(
        IReadOnlyList<OcrRule> candidateRules,
        IReadOnlyDictionary<string, string> acceptedValues) =>
        candidateRules.Any(rule => !acceptedValues.ContainsKey(rule.Id))
            ? candidateRules
            : Array.Empty<OcrRule>();

    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        RecognitionCandidate candidate,
        string stage) =>
        BindPaddleEvidence(client, candidate.SourcePath, candidate.OcrPath, stage + "/" + candidate.SelectionMode);

    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        string sourcePath,
        string inputPath,
        string stage)
    {
        if (!client.LastEvidence.TryGetValue(inputPath, out OcrEvidence? evidence))
            return null;
        OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(sourcePath, inputPath, stage);
        return evidence.Bind(identity);
    }

    private static bool IsCompactJieshaoTable(string groupDirectory, IReadOnlyList<OcrRule> rules) =>
        RuleCatalog.IsGroupFolder(groupDirectory, "嫣然心水") &&
        rules.Any(rule => rule.Folder == "杰少" && (rule.Id, rule.Type) is
            ("杰少杀一肖", "生肖") or ("杰少杀一尾", "尾") or ("杰少禁一尾", "尾"));

    private static string RetryPrimaryImage(
        string groupDirectory,
        string sourcePath,
        string ocrPath,
        IReadOnlyList<OcrRule> rules) =>
        IsCompactJieshaoTable(groupDirectory, rules)
        || RuleCatalog.IsGroupFolder(groupDirectory, "新澳六合彩资料")
            && rules.Any(rule => rule.Id is "水哥肖" or "九宫格肖肖")
            ? ocrPath
            : sourcePath;

    private static bool CanReuseRetryCloudLines(
        string groupDirectory, IReadOnlyList<OcrRule> rules, IReadOnlyList<string> lines, int issue) =>
        lines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
        rules.All(rule => RuleEngine.ExtractFinalValue(lines, issue, rule) is not null);

    private void LogMissingDetails(
        string operation,
        string groupName,
        int issue,
        IReadOnlyList<OcrRule> rules,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<RecognitionCandidate> candidates)
    {
        foreach (OcrRule rule in rules.Where(rule => !values.ContainsKey(rule.Id)))
        {
            RecognitionCandidate? candidate = candidates.FirstOrDefault(item =>
                item.Rules.Any(candidateRule => candidateRule.Id == rule.Id));
            IReadOnlyList<string> lines = candidate is null
                ? []
                : lastCloudOcrResults.TryGetValue(candidate.SourcePath, out IReadOnlyList<string>? cloudLines)
                    ? cloudLines
                    : candidate.LocalLines ?? [];
            string reason = candidate is null
                ? "未找到对应图片"
                : RuleEngine.DescribeExtractionFailure(lines, issue, rule);
            string file = candidate is null ? "-" : Path.GetFileName(candidate.SourcePath);
            OperationLog.Append(
                AppContext.BaseDirectory,
                $"{operation}缺失：{rule.OutputLabel}；图片={file}；原因={reason}",
                groupName,
                issue);
        }
    }

    private static string PrepareLocalCloudImage(
        string groupDirectory, string sourcePath, IReadOnlyList<OcrRule> rules, ref string? temporaryFolder)
    {
        if (!IsCompactJieshaoTable(groupDirectory, rules))
            return sourcePath;

        try
        {
            // Keep every row, but avoid the column layout seen in the saved cloud response.
            using var source = Image.FromFile(sourcePath);
            using var compact = new Bitmap(Math.Max(1, (int)(source.Width * 0.75)), source.Height);
            using (var graphics = Graphics.FromImage(compact))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, new Rectangle(0, 0, compact.Width, compact.Height));
            }
            temporaryFolder ??= Path.Combine(Path.GetTempPath(), "OcrLineTool", "jieshao-cloud-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryFolder);
            string destination = Path.Combine(temporaryFolder, Guid.NewGuid().ToString("N") + ".png");
            compact.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
            return destination;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or ExternalException)
        {
            return sourcePath;
        }
    }

    private bool DeferUnmatchedTemplates(bool allowTemplateSubset) =>
        !allowTemplateSubset && selectedImageDirectory is not null &&
        RuleCatalog.IsGroupFolder(selectedImageDirectory, "新澳六合彩资料");

    private IReadOnlySet<string> LimitedLocalRetryRuleIds(IReadOnlyList<OcrRule> missingRules)
    {
        if (selectedImageDirectory is null ||
            !RuleCatalog.IsGroupFolder(selectedImageDirectory, "新澳六合彩资料"))
            return new HashSet<string>(StringComparer.Ordinal);

        return missingRules
            .Where(rule => rule.Id is "王者肖肖" or "九宫格肖肖")
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private double LocalRetryTitleRatio(IReadOnlySet<string>? localLimitedRuleIds)
    {
        if (localLimitedRuleIds is not null &&
            (localLimitedRuleIds.Contains("王者肖肖") || localLimitedRuleIds.Contains("九宫格肖肖")) &&
            selectedImageDirectory is not null &&
            RuleCatalog.IsGroupFolder(selectedImageDirectory, "新澳六合彩资料"))
            return 1.0;

        return PaddleLocalOcrClient.TitleRatioFor(selectedImageDirectory!);
    }

    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null,
        OcrEvidence? LocalEvidence = null);

    private sealed record CandidateSelection(
        IReadOnlyList<RecognitionCandidate> Candidates,
        string? TemporaryCropFolder,
        string DisplayName);

    private void DeleteDirectoryImages(object? sender, EventArgs e)
    {
        if (selectedImageDirectory is null)
            return;

        string[] currentImages;
        try
        {
            currentImages = ImageFolderScanner.Scan(selectedImageDirectory);
        }
        catch (OcrException exception)
        {
            MessageBox.Show(this, exception.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (currentImages.Length == 0)
        {
            statusLabel.Text = "所选子文件夹中没有可删除的图片。";
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"确定将所选子文件夹中的 {currentImages.Length} 张图片移入回收站吗？\n\n{selectedImageDirectory}",
            "确认删除目录图片",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        DeleteImagesResult result = ImageFolderCleaner.DeleteImages(selectedImageDirectory);
        imagePaths = [];
        ClearRetryState();
        Image? previous = preview.Image;
        preview.Image = null;
        previous?.Dispose();
        resultsBox.Clear();
        copyButton.Enabled = false;
        recognizeButton.Enabled = false;
        localPrimaryButton.Enabled = false;
        deleteButton.Enabled = false;
        selectionLabel.Text = $"所选目录：{selectedImageDirectory} · 当前 0 张图片";
        statusLabel.Text = result.Failed == 0
            ? $"已将 {result.Deleted} 张图片移入回收站。"
            : $"已将 {result.Deleted} 张图片移入回收站，{result.Failed} 张删除失败。";
        SetProgress(0, 0);
    }

    private void SetBusy(bool busy)
    {
        isBusy = busy;
        if (busy)
        {
            activeCancellation?.Dispose();
            activeCancellation = new CancellationTokenSource();
        }
        folderList.Enabled = !busy;
        deleteButton.Enabled = !busy && imagePaths.Length > 0;
        recognizeButton.Enabled = !busy && imagePaths.Length > 0 && selectedRulePath is not null && File.Exists(selectedRulePath);
        localPrimaryButton.Enabled = !busy && imagePaths.Length > 0 && selectedRulePath is not null && File.Exists(selectedRulePath);
        issueInput.Enabled = !busy;
        credentialSelector.Enabled = !busy;
        retryMissingButton.Enabled = !busy && CanRetryMissing();
        manualDistributeButton.Enabled = !busy && CanManualDistribute();
        clearResultsButton.Enabled = !busy;
        missingSummaryButton.Enabled = !busy;
        UseWaitCursor = busy;
        if (!busy)
        {
            continueButton.Enabled = false;
            continueButton.Visible = false;
            cloudResume = null;
            activeCancellation?.Dispose();
            activeCancellation = null;
            if (closeWhenIdle && !IsDisposed && IsHandleCreated) BeginInvoke(new Action(Close));
        }
    }

    private Task WaitForCloudResumeAsync(int current, int total, string path)
    {
        cloudResume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        continueButton.Visible = true;
        continueButton.BringToFront();
        continueButton.Enabled = true;
        UseWaitCursor = false;
        statusLabel.Text = $"云 OCR 多次限流，已暂停在 {current}/{total}：{ShortPath(path)}。点击“继续云 OCR”从此处继续。";
        return cloudResume.Task.WaitAsync(ActiveToken);
    }

    private void ContinueCloudOcr(object? sender, EventArgs e)
    {
        if (cloudResume is null)
            return;
        continueButton.Enabled = false;
        continueButton.Visible = false;
        UseWaitCursor = true;
        statusLabel.Text = "正在从云 OCR 断点继续……";
        cloudResume.TrySetResult(true);
    }

    private void SetProgress(int completed, int total)
    {
        recognitionCompleted = completed;
        recognitionTotal = total;
        progressBar.MarqueeAnimationSpeed = 0;
        progressBar.Style = ProgressBarStyle.Continuous;
        progressBar.Maximum = Math.Max(1, total);
        progressBar.Value = Math.Clamp(completed, 0, progressBar.Maximum);
        UpdateRecognitionTiming();
    }

    private void SetIndeterminateProgress()
    {
        recognitionCompleted = 0;
        recognitionTotal = 0;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 25;
        UpdateRecognitionTiming();
    }

    private void StartRecognitionTiming()
    {
        recognitionCompleted = 0;
        recognitionTotal = 0;
        recognitionWatch.Restart();
        recognitionTimingLabel.Visible = true;
        recognitionTimer.Start();
        UpdateRecognitionTiming();
    }

    private void UpdateRecognitionTiming()
    {
        if (!recognitionWatch.IsRunning)
            return;
        string remaining = recognitionCompleted > 0 && recognitionTotal > 0
            ? FormatDuration(TimeSpan.FromSeconds(
                recognitionWatch.Elapsed.TotalSeconds * Math.Max(0, recognitionTotal - recognitionCompleted) / recognitionCompleted))
            : "计算中";
        recognitionTimingLabel.Text = $"已用 {FormatDuration(recognitionWatch.Elapsed)} · 预计剩余 {remaining}";
    }

    private void StopRecognitionTiming()
    {
        recognitionTimer.Stop();
        recognitionWatch.Stop();
        recognitionTimingLabel.Text = $"总耗时 {FormatDuration(recognitionWatch.Elapsed)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private static string DistributionErrorText(DistributionResult result) =>
        result.Errors.Count == 0
            ? string.Empty
            : $"，失败 {result.Errors.Count} 类（{result.Errors[0]}）";

    private static string ShortPath(string path)
    {
        string relative = Path.GetRelativePath(FixedImageDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(path) : relative;
    }

    private void CopyResults()
    {
        if (resultsBox.TextLength == 0)
            return;
        Clipboard.SetText(resultsBox.Text.TrimEnd());
        statusLabel.Text = "结果已复制到剪贴板。";
    }

    private async void SummarizeMissingAsync(object? sender, EventArgs e)
    {
        SetBusy(true);
        statusLabel.Text = "正在统计所有群的缺失及未分流数据……";
        try
        {
            await GenerateAndOpenMissingSummaryAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            statusLabel.Text = "统计失败，未更新汇总文件。";
            MessageBox.Show(this, exception.Message, "统计缺失失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task GenerateAndOpenMissingSummaryAsync()
    {
        var result = await MissingResultSummary.WriteAsync(AppContext.BaseDirectory, imageRootDirectory);
        string summary = $"统计完成：缺失或未分流共 {result.Count} 条。汇总TXT：{result.Path}";
        statusLabel.Text = TryOpenTextFile(result.Path)
            ? summary + "（已打开）"
            : summary + "；" + statusLabel.Text;
    }

    private void OpenGroupResults()
    {
        if (selectedImageDirectory is null)
        {
            statusLabel.Text = "请先选择群组，再打开对应的群结果 TXT。";
            return;
        }
        string path = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory,
            Decimal.ToInt32(issueInput.Value));
        if (TryOpenTextFile(path))
            statusLabel.Text = $"已打开群结果 TXT：{path}";
    }

    private bool TryOpenTextFile(string path)
    {
        if (!File.Exists(path))
        {
            statusLabel.Text = $"未找到对应的 TXT：{path}";
            return false;
        }
        try
        {
            openFile(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            statusLabel.Text = $"打开 TXT 失败：{path}。{exception.Message}";
            return false;
        }
    }

    private void ClearOutputFiles(object? sender, EventArgs e)
    {
        string temporary = ResultFilePaths.TemporaryFilesDirectory(AppContext.BaseDirectory);
        string important = ResultFilePaths.ImportantResultsDirectory(AppContext.BaseDirectory);
        DialogResult answer = MessageBox.Show(
            this,
            $"确定将以下目录及其子目录中的所有文件移入回收站吗？\n\n{temporary}\n{important}\n\n文件夹结构会保留。",
            "确认清除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        ClearFilesResult result = OutputFileCleaner.Clear([temporary, important]);
        statusLabel.Text = result.Deleted == 0 && result.Failed == 0
            ? "两个目录中没有需要清除的文件。"
            : result.Failed == 0
                ? $"已将 {result.Deleted} 个文件移入回收站，文件夹已保留。"
                : $"已将 {result.Deleted} 个文件移入回收站，{result.Failed} 个文件清除失败。";
        manualDistributeButton.Enabled = CanManualDistribute();
    }

    private void ShowPreview(string path)
    {
        try
        {
            using Image source = Image.FromFile(path);
            var copy = new Bitmap(source.Width, source.Height);
            copy.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            using (Graphics graphics = Graphics.FromImage(copy))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(source, 0, 0);
            }
            Image? previous = preview.Image;
            preview.Image = copy;
            previous?.Dispose();
        }
        catch
        {
            preview.Image = null;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (isBusy && keyData == Keys.Escape)
        {
            activeCancellation?.Cancel();
            statusLabel.Text = "正在取消本次任务……";
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (isBusy)
        {
            e.Cancel = true;
            closeWhenIdle = true;
            activeCancellation?.Cancel();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            activeCancellation?.Cancel();
            activeCancellation?.Dispose();
            activeCancellation = null;
            dateTimer.Dispose();
            folderRefreshTimer.Dispose();
            recognitionTimer.Dispose();
            preview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class SingleLineEllipsisLabel : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        OnPaintBackground(e);
        Rectangle bounds = new(
            Padding.Left,
            Padding.Top,
            Math.Max(0, ClientSize.Width - Padding.Horizontal),
            Math.Max(0, ClientSize.Height - Padding.Vertical));
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            bounds,
            Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }
}

internal sealed class DarkButton : Button
{
    private bool hovered;
    private bool pressed;

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        pressed = mevent.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Color background = pressed && Enabled
            ? FlatAppearance.MouseDownBackColor
            : hovered && Enabled
                ? FlatAppearance.MouseOverBackColor
                : BackColor;
        pevent.Graphics.Clear(background);

        if (FlatAppearance.BorderSize > 0)
        {
            using var borderPen = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize);
            Rectangle border = ClientRectangle;
            border.Width -= 1;
            border.Height -= 1;
            pevent.Graphics.DrawRectangle(borderPen, border);
        }

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            ClientRectangle,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(pevent.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, background);
    }
}

internal sealed class DarkComboBox : ComboBox
{
    private const int PaintMessage = 0x000F;
    private const int PrintMessage = 0x0317;
    private const int PrintClientMessage = 0x0318;
    private static readonly Color Surface = Color.FromArgb(37, 39, 44);
    private static readonly Color Border = Color.FromArgb(70, 72, 78);
    private static readonly Color Arrow = Color.FromArgb(210, 211, 215);

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (DropDownStyle == ComboBoxStyle.Simple || Width <= 2 || Height <= 2)
            return;

        if (message.Msg == PaintMessage)
        {
            using Graphics graphics = CreateGraphics();
            DrawChrome(graphics);
            return;
        }

        if ((message.Msg == PrintMessage || message.Msg == PrintClientMessage) && message.WParam != IntPtr.Zero)
        {
            using Graphics graphics = Graphics.FromHdc(message.WParam);
            DrawChrome(graphics);
        }
    }

    private void DrawChrome(Graphics graphics)
    {
        int arrowWidth = Math.Min(28, Math.Max(20, Height));
        var arrowArea = new Rectangle(Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
        using var surfaceBrush = new SolidBrush(Surface);
        using var borderPen = new Pen(Border);
        using var arrowPen = new Pen(Enabled ? Arrow : Color.FromArgb(119, 120, 126), 1.5F);
        graphics.FillRectangle(surfaceBrush, arrowArea);
        graphics.DrawLine(borderPen, arrowArea.Left, arrowArea.Top, arrowArea.Left, arrowArea.Bottom);
        graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        int centerX = arrowArea.Left + arrowArea.Width / 2;
        int centerY = arrowArea.Top + arrowArea.Height / 2;
        graphics.DrawLines(arrowPen,
        new Point[]
        {
            new Point(centerX - 4, centerY - 2),
            new Point(centerX, centerY + 2),
            new Point(centerX + 4, centerY - 2)
        });
    }
}

internal sealed class DarkNumericUpDown : NumericUpDown
{
    private static readonly Color Surface = Color.FromArgb(37, 39, 44);
    private static readonly Color Border = Color.FromArgb(70, 72, 78);
    private static readonly Color Arrow = Color.FromArgb(210, 211, 215);
    private readonly Control? buttons;

    public DarkNumericUpDown()
    {
        buttons = Controls.Count > 0 ? Controls[0] : null;
        if (buttons is null)
            return;

        buttons.BackColor = Surface;
        buttons.ForeColor = Arrow;
        buttons.Paint += DrawButtons;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        buttons?.Invalidate();
        base.OnEnabledChanged(e);
    }

    private void DrawButtons(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control || control.Width <= 1 || control.Height <= 1)
            return;

        e.Graphics.Clear(Surface);
        int middle = control.Height / 2;
        using var borderPen = new Pen(Border);
        using var arrowPen = new Pen(Enabled ? Arrow : Color.FromArgb(119, 120, 126), 1.4F);
        e.Graphics.DrawLine(borderPen, 0, 0, 0, control.Height);
        e.Graphics.DrawLine(borderPen, 0, middle, control.Width, middle);

        int centerX = control.Width / 2;
        int upperCenter = Math.Max(3, middle / 2);
        int lowerCenter = middle + Math.Max(3, (control.Height - middle) / 2);
        e.Graphics.DrawLines(arrowPen,
        new Point[]
        {
            new Point(centerX - 3, upperCenter + 1),
            new Point(centerX, upperCenter - 2),
            new Point(centerX + 3, upperCenter + 1)
        });
        e.Graphics.DrawLines(arrowPen,
        new Point[]
        {
            new Point(centerX - 3, lowerCenter - 1),
            new Point(centerX, lowerCenter + 2),
            new Point(centerX + 3, lowerCenter - 1)
        });
    }
}

internal sealed class DarkProgressBar : Control
{
    private readonly System.Windows.Forms.Timer marqueeTimer = new();
    private int minimum;
    private int maximum = 100;
    private int currentValue;
    private int marqueeAnimationSpeed;
    private int marqueeOffset;
    private ProgressBarStyle progressStyle = ProgressBarStyle.Continuous;

    public DarkProgressBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.FromArgb(37, 39, 44);
        ForeColor = Color.FromArgb(10, 132, 255);
        marqueeTimer.Tick += (_, _) =>
        {
            marqueeOffset = Width <= 0 ? 0 : (marqueeOffset + 10) % Math.Max(1, Width + Math.Max(28, Width / 4));
            Invalidate();
        };
    }

    public int Minimum
    {
        get => minimum;
        set
        {
            minimum = value;
            if (maximum < minimum)
                maximum = minimum;
            Value = currentValue;
        }
    }

    public int Maximum
    {
        get => maximum;
        set
        {
            maximum = Math.Max(minimum, value);
            Value = currentValue;
        }
    }

    public int Value
    {
        get => currentValue;
        set
        {
            currentValue = Math.Clamp(value, minimum, maximum);
            Invalidate();
        }
    }

    public ProgressBarStyle Style
    {
        get => progressStyle;
        set
        {
            progressStyle = value;
            UpdateMarqueeTimer();
            Invalidate();
        }
    }

    public int MarqueeAnimationSpeed
    {
        get => marqueeAnimationSpeed;
        set
        {
            marqueeAnimationSpeed = Math.Max(0, value);
            UpdateMarqueeTimer();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateMarqueeTimer();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 1 || ClientSize.Height <= 1)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle track = new(0, 1, Width - 1, Math.Max(1, Height - 2));
        using GraphicsPath trackPath = RoundedRectangle(track, Math.Min(7, track.Height / 2));
        using var trackBrush = new SolidBrush(BackColor);
        using var borderPen = new Pen(Color.FromArgb(64, 66, 72));
        e.Graphics.FillPath(trackBrush, trackPath);
        e.Graphics.DrawPath(borderPen, trackPath);

        Rectangle fill = progressStyle == ProgressBarStyle.Marquee
            ? MarqueeRectangle(track)
            : ValueRectangle(track);
        if (fill.Width <= 0)
            return;

        GraphicsState state = e.Graphics.Save();
        e.Graphics.SetClip(trackPath);
        using var fillBrush = new SolidBrush(ForeColor);
        e.Graphics.FillRectangle(fillBrush, fill);
        e.Graphics.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            marqueeTimer.Dispose();
        base.Dispose(disposing);
    }

    private Rectangle ValueRectangle(Rectangle track)
    {
        int range = maximum - minimum;
        double ratio = range <= 0 ? 0 : (double)(currentValue - minimum) / range;
        return new Rectangle(track.X, track.Y, (int)Math.Round(track.Width * ratio), track.Height);
    }

    private Rectangle MarqueeRectangle(Rectangle track)
    {
        int blockWidth = Math.Max(28, track.Width / 4);
        int x = track.X + marqueeOffset - blockWidth;
        return Rectangle.Intersect(track, new Rectangle(x, track.Y, blockWidth, track.Height));
    }

    private void UpdateMarqueeTimer()
    {
        bool shouldRun = Visible && progressStyle == ProgressBarStyle.Marquee && marqueeAnimationSpeed > 0;
        if (!shouldRun)
        {
            marqueeTimer.Stop();
            return;
        }

        marqueeTimer.Interval = Math.Clamp(marqueeAnimationSpeed, 15, 1000);
        marqueeTimer.Start();
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(Math.Min(radius * 2, bounds.Width), bounds.Height);
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DarkRoundedPanel : Panel
{
    private int cornerRadius = 12;
    private Color borderColor = Color.FromArgb(54, 56, 62);

    public DarkRoundedPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public int CornerRadius
    {
        get => cornerRadius;
        set
        {
            cornerRadius = Math.Max(0, value);
            UpdateRegion();
            Invalidate();
        }
    }

    public Color BorderColor
    {
        get => borderColor;
        set
        {
            borderColor = value;
            Invalidate();
        }
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        UpdateRegion();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (Width <= 1 || Height <= 1)
            return;

        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), cornerRadius);
        using var pen = new Pen(borderColor);
        eventArgs.Graphics.DrawPath(pen, path);
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;

        using GraphicsPath path = RoundedRectangle(ClientRectangle, cornerRadius);
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(Math.Min(radius * 2, bounds.Width), bounds.Height);
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
