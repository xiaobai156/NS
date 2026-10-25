namespace OcrLineTool;

// 「自动识别」窗口：按群勾选要自动跑本地主识别的群。
// 群列表实时取当前抓取路径下的一级子目录，勾选立即保存，重启后保留。
internal sealed class AutoRecognizeForm : Form
{
    private readonly string appDirectory;
    private readonly string imageRootDirectory;
    private readonly AutoLocalOcrSettings settings;
    private readonly ListBox groupList = new()
    {
        Name = "autoRecognizeGroups",
        Dock = DockStyle.Fill,
        BackColor = MainForm.CardBackground,
        ForeColor = MainForm.PrimaryText,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Microsoft YaHei UI", 10F),
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 26,
        AccessibleName = "自动识别群组列表"
    };
    private readonly Label hintLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = MainForm.SecondaryText,
        Font = new Font("Microsoft YaHei UI", 9F),
        TextAlign = ContentAlignment.TopLeft,
        Text = "勾选的群在图片稳定 1 分钟后自动执行本地主识别；每个群当天只跑一次，失败不自动重试。"
            + "勾选立即保存，重启软件后保持。嫣然心水不参与自动识别。"
    };
    private readonly Button closeButton = new() { Text = "关闭" };
    private readonly List<(string Folder, string Group)> groups = new();
    private bool loading;

    internal AutoRecognizeForm(string appDirectory, string imageRootDirectory)
    {
        this.appDirectory = appDirectory;
        this.imageRootDirectory = imageRootDirectory;
        settings = AutoLocalOcrSettings.Load(appDirectory);

        Text = "自动识别";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = MainForm.WindowBackground;
        ForeColor = MainForm.PrimaryText;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 480);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20, 18, 20, 18),
            BackColor = MainForm.WindowBackground,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var title = new Label
        {
            Text = "选择要自动识别的群",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = MainForm.PrimaryText,
            Font = new Font(MainForm.HeadingFontFamily, 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        groupList.DrawItem += DrawGroupItem;
        groupList.MouseClick += (_, e) => ToggleFromClick(e.Location);

        closeButton.Dock = DockStyle.Right;
        closeButton.Width = 104;
        closeButton.Height = 34;
        closeButton.FlatStyle = FlatStyle.Flat;
        closeButton.BackColor = MainForm.CardBackground;
        closeButton.ForeColor = MainForm.PrimaryText;
        closeButton.Click += (_, _) => Close();

        var hintHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainForm.WindowBackground,
            Padding = new Padding(0, 4, 0, 0),
            Margin = Padding.Empty
        };
        hintHost.Controls.Add(hintLabel);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(groupList, 0, 1);
        layout.Controls.Add(hintHost, 0, 2);
        layout.Controls.Add(closeButton, 0, 3);
        Controls.Add(layout);

        LoadGroups();
    }

    private void LoadGroups()
    {
        loading = true;
        groups.Clear();
        groupList.Items.Clear();
        try
        {
            string[] folders = ImageFolderScanner.ListSubfolders(imageRootDirectory);
            IReadOnlyDictionary<string, string> names = RuleCatalog.GroupNamesForFolders(appDirectory, folders);
            foreach (string folder in folders)
            {
                if (!names.TryGetValue(folder, out string? group) || string.IsNullOrWhiteSpace(group))
                    continue;
                if (AutoLocalOcrSettings.IsExcludedGroup(group))
                    continue;
                groups.Add((folder, group));
                groupList.Items.Add(group);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or OcrException or System.Text.Json.JsonException)
        {
            // 目录暂时不可访问时窗口照常打开，列表为空。
        }
        loading = false;
        groupList.Invalidate();
    }

    private void ToggleFromClick(Point location)
    {
        int index = groupList.IndexFromPoint(location);
        if (index < 0 || index >= groups.Count)
            return;
        string group = groups[index].Group;
        bool selected = !settings.IsSelectedGroup(group);
        settings.SetSelectedGroup(group, selected);
        try
        {
            settings.Save(appDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "自动识别设置保存失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // 保存失败时回滚勾选状态，避免界面与磁盘不一致。
            settings.SetSelectedGroup(group, !selected);
        }
        if (loading)
            return;
        groupList.Invalidate();
    }

    private void DrawGroupItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= groups.Count)
            return;
        bool selected = settings.IsSelectedGroup(groups[e.Index].Group);
        using (var background = new SolidBrush(MainForm.CardBackground))
            e.Graphics.FillRectangle(background, e.Bounds);
        var box = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + 6, 14, 14);
        using (var border = new Pen(MainForm.SecondaryText))
            e.Graphics.DrawRectangle(border, box);
        if (selected)
        {
            using var fill = new SolidBrush(MainForm.AccentAmber);
            e.Graphics.FillRectangle(fill, Rectangle.Inflate(box, -3, -3));
        }
        TextRenderer.DrawText(e.Graphics, groups[e.Index].Group,
            groupList.Font,
            new Rectangle(e.Bounds.Left + 32, e.Bounds.Top, e.Bounds.Width - 36, e.Bounds.Height),
            MainForm.PrimaryText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
