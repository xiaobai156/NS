namespace OcrLineTool;

// 「自动识别」窗口：按群勾选要自动跑本地主识别的群。
// 群列表实时取当前抓取路径下的一级子目录，勾选立即保存，重启后保留。
internal sealed class AutoRecognizeForm : Form
{
    // 供设置窗口直接调用：打开群勾选窗口，勾选立即落盘。
    internal static void ShowDialog(IWin32Window owner, string appDirectory, string imageRootDirectory)
    {
        using var dialog = new AutoRecognizeForm(appDirectory, imageRootDirectory);
        dialog.ShowDialog(owner);
    }

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
    private readonly List<string> groups = new();
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
        // 只响应鼠标左键；空格/回车用于键盘勾选，保证不开鼠标也能操作。
        groupList.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleFromClick(e.Location);
        };
        groupList.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Space or Keys.Enter))
                return;
            ToggleAt(groupList.SelectedIndex);
            e.Handled = true;
        };

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
        foreach (string group in BuildGroupList(
            imageRootDirectory, appDirectory, settings.SelectedGroups))
        {
            groups.Add(group);
            groupList.Items.Add(group);
        }
        loading = false;
        groupList.Invalidate();
    }

    // 自动识别窗口要列出的群，按三个来源依次补齐并去重：
    //   1) 当前抓取路径下已有目录的群；
    //   2) 配置文件里登记的全部群（抓图还没跑、目录为空时仍能勾选）；
    //   3) 已勾选但前两者都没有的群（配置被删也能取消勾选）。
    // 去重一律用 RuleCatalog.NormalizeGroupName：设置里存的是归一化名，
    // 直接比原始字符串会让「黑色 六合彩」和「黑色六合彩」变成两行。
    // 单独抽成静态方法是为了能在测试里直接断言列表内容。
    internal static IReadOnlyList<string> BuildGroupList(
        string imageRootDirectory, string appDirectory, IEnumerable<string> selectedGroups)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? group)
        {
            if (string.IsNullOrWhiteSpace(group) || AutoLocalOcrSettings.IsExcludedGroup(group))
                return;
            if (seen.Add(RuleCatalog.NormalizeGroupName(group)))
                names.Add(group);
        }

        try
        {
            string[] folders = ImageFolderScanner.ListSubfolders(imageRootDirectory);
            IReadOnlyDictionary<string, string> folderNames =
                RuleCatalog.GroupNamesForFolders(appDirectory, folders);
            foreach (string folder in folders)
            {
                if (folderNames.TryGetValue(folder, out string? group))
                    Add(group);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or OcrException or System.Text.Json.JsonException)
        {
            // 目录暂时不可访问时照常打开窗口，后面两个来源仍会补齐。
        }

        try
        {
            foreach (string known in RuleCatalog.AllGroupNames(appDirectory))
                Add(known);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            // 配置目录不可读时只影响兜底，不影响已勾选的群。
        }

        foreach (string selected in selectedGroups)
            Add(selected);

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }
    private void ToggleFromClick(Point location)
    {
        // 只响应左键：右键/中键不应该改变勾选。
        int index = groupList.IndexFromPoint(location);
        ToggleAt(index);
    }

    private void ToggleAt(int index)
    {
        if (index < 0 || index >= groups.Count)
            return;
        string group = groups[index];
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
        bool selected = settings.IsSelectedGroup(groups[e.Index]);
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
        TextRenderer.DrawText(e.Graphics, groups[e.Index],
            groupList.Font,
            new Rectangle(e.Bounds.Left + 32, e.Bounds.Top, e.Bounds.Width - 36, e.Bounds.Height),
            MainForm.PrimaryText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
