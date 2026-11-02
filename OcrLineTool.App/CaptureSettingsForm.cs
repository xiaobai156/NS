using System.Text.Json;

namespace OcrLineTool;

// 「抓取设置」窗口：三级勾选——群 → 分类 → 条目。
// 取消群：该群不抓取、不识别；取消条目：按“这条资料不存在”处理（不识别、不显示缺失）。
// 勾选立即保存，重启后保持；新出现的群、新加的规则默认开启。
internal sealed class CaptureSettingsForm : Form
{
    internal static void ShowDialog(IWin32Window owner, string appDirectory, string imageRootDirectory)
    {
        using var dialog = new CaptureSettingsForm(appDirectory, imageRootDirectory);
        dialog.ShowDialog(owner);
    }

    private enum RowKind
    {
        Group,
        Category,
        Rule,
        Info
    }

    private sealed class Row
    {
        public RowKind Kind { get; init; }
        public string Group { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public bool HasFolder { get; init; } = true;
        public OcrRule? Rule { get; init; }
        public int Indent { get; init; }
        // 群开关会牵动同群所有行的显示状态，所以这个不能在构建时写死。
        public bool GroupEnabled { get; set; } = true;
    }

    private sealed class GroupData
    {
        public string Name { get; init; } = string.Empty;
        public string Folders { get; init; } = string.Empty;
        public bool HasFolder { get; init; }
        public string? LoadError { get; set; }
        public IReadOnlyList<OcrRule> Rules { get; set; } = Array.Empty<OcrRule>();
    }

    // 群行加高留白，展开后的分类行和条目行保持紧凑，便于一眼分清哪些是群。
    private const int GroupRowHeight = 40;
    private const int SubRowHeight = 24;
    // 分类行比条目行高出一截，多出来的部分当作上方空白：区块之间留出气口。
    private const int CategoryTopGap = 10;
    private const int CategoryRowHeight = SubRowHeight + CategoryTopGap;
    private const int CheckBoxSize = 14;
    // 勾选框固定在左边同一列，层级只缩进文字；否则每深一级方框就右移，一列看起来是歪的。
    private const int TextIndentStep = 14;

    private readonly string appDirectory;
    private readonly string imageRootDirectory;
    private CaptureSettings settings;
    private readonly List<GroupData> groups = new();
    private readonly List<Row> rows = new();
    private readonly HashSet<string> expanded =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ListBox rowList = new()
    {
        Name = "captureSettingsRows",
        Dock = DockStyle.Fill,
        BackColor = MainForm.CardBackground,
        ForeColor = MainForm.PrimaryText,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Microsoft YaHei UI", 10F),
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawVariable,
        ItemHeight = SubRowHeight,
        AccessibleName = "抓取设置列表"
    };
    private readonly Label hintLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = MainForm.SecondaryText,
        Font = new Font("Microsoft YaHei UI", 9F),
        TextAlign = ContentAlignment.TopLeft,
        Text = "点群名展开条目；点方框开关。取消勾选的群不抓取也不识别；取消勾选的条目按不存在处理，"
            + "不会出现在结果里、也不算缺失。群关掉时条目按上次保存的状态显示，重新勾选群就恢复。"
            + "勾选立即保存，新出现的群和新加的规则默认开启。"
            + "条目显示的是结果里的规范名，鼠标停在群名上可看目录、停在条目上可看图片原文。"
    };
    private readonly Label summaryLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = MainForm.SecondaryText,
        Font = new Font("Microsoft YaHei UI", 9F),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button closeButton = new() { Text = "关闭" };
    private readonly ToolTip rowToolTip = new();
    private readonly Font groupFont = new(new FontFamily("Microsoft YaHei UI"), 10F, FontStyle.Bold);

    internal CaptureSettingsForm(string appDirectory, string imageRootDirectory)
    {
        this.appDirectory = appDirectory;
        this.imageRootDirectory = imageRootDirectory;
        settings = CaptureSettings.Load(appDirectory);

        Text = "抓取设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = MainForm.WindowBackground;
        ForeColor = MainForm.PrimaryText;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 620);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20, 18, 20, 18),
            BackColor = MainForm.WindowBackground,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var title = new Label
        {
            Text = "抓取设置",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = MainForm.PrimaryText,
            Font = new Font(MainForm.HeadingFontFamily, 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        rowList.DrawItem += DrawRow;
        rowList.MeasureItem += MeasureRow;
        rowList.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                HandleClick(e.Location);
        };
        rowList.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Space or Keys.Enter))
                return;
            ToggleSelected();
            e.Handled = true;
        };
        // 条目行显示的是规范名（label），和图片上的原文不一定一样；原文放进悬停提示，方便对着图核对。
        // 群行的目录也只放在提示里，列表本身只显示群名，免得一行挤满长路径。
        string lastToolTip = string.Empty;
        rowList.MouseMove += (_, e) =>
        {
            string text = string.Empty;
            int index = rowList.IndexFromPoint(e.Location);
            if (index >= 0 && index < rows.Count)
            {
                Row row = rows[index];
                if (row.Kind == RowKind.Group)
                {
                    text = row.HasFolder ? $"目录：{row.Detail}" : "目录还没出现";
                }
                else if (row.Kind == RowKind.Rule && row.Rule is { } rule
                    && !string.IsNullOrWhiteSpace(rule.Label)
                    && !string.Equals(rule.Label, rule.Keyword, StringComparison.Ordinal))
                {
                    text = $"原文：{rule.Keyword}";
                }
            }
            if (string.Equals(text, lastToolTip, StringComparison.Ordinal))
                return;
            lastToolTip = text;
            rowToolTip.SetToolTip(rowList, text);
        };
        // 群行点击文字就是展开/收起，不再单独处理双击，避免一次双击把状态翻回来。

        closeButton.Dock = DockStyle.Right;
        closeButton.Width = 104;
        closeButton.AutoSize = false;
        closeButton.FlatStyle = FlatStyle.Flat;
        closeButton.BackColor = MainForm.CardBackground;
        closeButton.ForeColor = MainForm.PrimaryText;
        closeButton.Height = 34;
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
        layout.Controls.Add(rowList, 0, 1);
        layout.Controls.Add(hintHost, 0, 2);
        layout.Controls.Add(summaryLabel, 0, 3);
        layout.Controls.Add(closeButton, 0, 4);
        Controls.Add(layout);
        CancelButton = closeButton;

        LoadGroups();
        RebuildRows();
    }

    // 群列表与「自动识别」窗口同源：抓图目录里已出现的群 + 配置文件登记的群。
    private void LoadGroups()
    {
        groups.Clear();
        IReadOnlyList<string> names;
        try
        {
            names = AutoRecognizeForm.BuildGroupList(
                imageRootDirectory, appDirectory, Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or OcrException or JsonException)
        {
            names = Array.Empty<string>();
        }

        // 已经关掉、但规则文件被删或改名的群也要列出来，否则这条关闭记录再也改不回来。
        var allNames = new List<string>(names);
        foreach (string disabled in settings.DisabledGroups)
        {
            if (!allNames.Any(name => RuleCatalog.NormalizeGroupName(name)
                .Equals(disabled, StringComparison.OrdinalIgnoreCase)))
                allNames.Add(disabled);
        }

        Dictionary<string, List<string>> foldersByGroup = FoldersByGroup();
        foreach (string name in allNames)
        {
            bool hasFolder = foldersByGroup.TryGetValue(
                RuleCatalog.NormalizeGroupName(name), out List<string>? folders);
            var data = new GroupData
            {
                Name = name,
                Folders = hasFolder ? string.Join("、", folders!) : string.Empty,
                HasFolder = hasFolder
            };
            string? path = RuleCatalog.RulePathForGroup(appDirectory, name);
            if (path is null)
            {
                data.LoadError = "没有找到这个群的规则文件";
            }
            else
            {
                try
                {
                    data.Rules = RuleCatalog.Load(path);
                }
                catch (Exception exception) when (exception is OcrException or IOException
                    or UnauthorizedAccessException or JsonException)
                {
                    data.LoadError = exception.Message;
                }
            }
            groups.Add(data);
        }
    }

    private Dictionary<string, List<string>> FoldersByGroup()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string[] folders = ImageFolderScanner.ListSubfolders(imageRootDirectory);
            IReadOnlyDictionary<string, string> names =
                RuleCatalog.GroupNamesForFolders(appDirectory, folders);
            foreach (string folder in folders)
            {
                if (!names.TryGetValue(folder, out string? group) || string.IsNullOrWhiteSpace(group))
                    continue;
                string key = RuleCatalog.NormalizeGroupName(group);
                if (!result.TryGetValue(key, out List<string>? list))
                {
                    list = new List<string>();
                    result[key] = list;
                }
                list.Add(folder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or OcrException or JsonException)
        {
            // 抓图目录暂时不可访问时只是不显示目录名，不影响开关。
        }
        return result;
    }

    private void RebuildRows()
    {
        rows.Clear();
        foreach (GroupData group in groups)
        {
            bool enabled = settings.IsGroupEnabled(group.Name);
            rows.Add(new Row
            {
                Kind = RowKind.Group,
                Group = group.Name,
                Text = group.Name,
                Detail = group.Folders,
                HasFolder = group.HasFolder,
                GroupEnabled = enabled
            });

            if (!expanded.Contains(RuleCatalog.NormalizeGroupName(group.Name)))
                continue;

            if (group.LoadError is not null)
            {
                rows.Add(new Row
                {
                    Kind = RowKind.Info,
                    Group = group.Name,
                    Text = group.LoadError,
                    Indent = 1,
                    GroupEnabled = enabled
                });
                continue;
            }

            foreach ((string category, List<OcrRule> rules) in Categorize(group.Rules))
            {
                rows.Add(new Row
                {
                    Kind = RowKind.Category,
                    Group = group.Name,
                    Category = category,
                    Text = $"{category}（{rules.Count}）",
                    Indent = 1,
                    GroupEnabled = enabled
                });
                foreach (OcrRule rule in rules)
                {
                    rows.Add(new Row
                    {
                        Kind = RowKind.Rule,
                        Group = group.Name,
                        Category = category,
                        Text = rule.OutputLabel,
                        Rule = rule,
                        Indent = 2,
                        GroupEnabled = enabled
                    });
                }
            }
        }

        // 重填列表会把滚动条弹回顶部；记住位置再恢复，展开/收起时视野不会跳。
        int topIndex = rowList.TopIndex;
        rowList.BeginUpdate();
        rowList.Items.Clear();
        foreach (Row row in rows)
            rowList.Items.Add(row.Text);
        rowList.EndUpdate();
        RestoreTopIndex(topIndex);
        rowList.Invalidate();
        UpdateSummary();
    }

    // 原生列表清空后滚动条会回到顶部，而且自绘变高行的重新测量是延迟的：
    // 立刻设一次 TopIndex 往往被按旧的默认行高算错，所以控件重排完成后（下一轮消息循环）再设一次。
    private void RestoreTopIndex(int topIndex)
    {
        if (topIndex <= 0)
            return;

        void Apply()
        {
            if (IsDisposed || !rowList.IsHandleCreated || rowList.Items.Count == 0)
                return;
            rowList.TopIndex = Math.Clamp(topIndex, 0, rowList.Items.Count - 1);
        }

        Apply();
        if (IsHandleCreated)
            BeginInvoke(Apply);
    }

    private void UpdateSummary()
    {
        int closed = groups.Count(group => !settings.IsGroupEnabled(group.Name));
        summaryLabel.Text = closed == 0
            ? $"共 {groups.Count} 个群，全部开启。"
            : $"共 {groups.Count} 个群，已关闭 {closed} 个。";
    }

    // 分类口径与写结果 TXT 完全一致（GroupResultFormatter），顺序也照它的固定顺序。
    internal static IReadOnlyList<(string Category, List<OcrRule> Rules)> Categorize(
        IReadOnlyList<OcrRule> rules)
    {
        var byCategory = new Dictionary<string, List<OcrRule>>(StringComparer.Ordinal);
        var seenOrder = new List<string>();
        foreach (OcrRule rule in rules)
        {
            string category = GroupResultFormatter.CategoryFor(rule.Type);
            if (!byCategory.TryGetValue(category, out List<OcrRule>? list))
            {
                list = new List<OcrRule>();
                byCategory[category] = list;
                seenOrder.Add(category);
            }
            list.Add(rule);
        }

        var ordered = new List<(string, List<OcrRule>)>();
        foreach (string category in GroupResultFormatter.CategoryOrder)
        {
            if (!byCategory.TryGetValue(category, out List<OcrRule>? list))
                continue;
            ordered.Add((category, list));
            byCategory.Remove(category);
        }
        foreach (string category in seenOrder)
        {
            if (byCategory.TryGetValue(category, out List<OcrRule>? list))
                ordered.Add((category, list));
        }
        return ordered;
    }

    private void HandleClick(Point location)
    {
        int index = rowList.IndexFromPoint(location);
        if (index < 0 || index >= rows.Count)
            return;
        Row row = rows[index];
        if (row.Kind == RowKind.Info)
            return;
        // 群关掉时群内条目不响应：它们显示的是上次保存的状态，点一下改动看不出效果，容易误以为生效了。
        if (row.Kind != RowKind.Group && !row.GroupEnabled)
            return;

        Rectangle itemBounds = rowList.GetItemRectangle(index);
        // 分类行的方框跟着内容一起下移了，命中判断要用同一块矩形。
        if (row.Kind == RowKind.Category)
            itemBounds = CategoryContentBounds(itemBounds);
        bool inBox = CheckBoxBounds(itemBounds).Contains(location);
        if (row.Kind == RowKind.Group)
        {
            if (inBox)
                ApplyStateChange(
                    () => settings.SetGroupEnabled(row.Group, !settings.IsGroupEnabled(row.Group)),
                    row.Group);
            else
                ToggleExpand(row.Group);
            return;
        }
        // 分类行只在方框上响应，避免点到文字误改一批条目。
        if (row.Kind == RowKind.Category && !inBox)
            return;
        if (row.Kind == RowKind.Category)
            ToggleCategory(row.Group, row.Category);
        else
            ApplyStateChange(() => settings.SetRuleEnabled(
                row.Group, row.Rule!, !settings.IsRuleEnabledIgnoringGroup(row.Group, row.Rule!)));
    }

    private void ToggleSelected()
    {
        int index = rowList.SelectedIndex;
        if (index < 0 || index >= rows.Count)
            return;
        Row row = rows[index];
        if (row.Kind != RowKind.Group && !row.GroupEnabled)
            return;
        if (row.Kind == RowKind.Group)
            ApplyStateChange(
                () => settings.SetGroupEnabled(row.Group, !settings.IsGroupEnabled(row.Group)),
                row.Group);
        else if (row.Kind == RowKind.Category)
            ToggleCategory(row.Group, row.Category);
        else if (row.Kind == RowKind.Rule)
            ApplyStateChange(() => settings.SetRuleEnabled(
                row.Group, row.Rule!, !settings.IsRuleEnabledIgnoringGroup(row.Group, row.Rule!)));
    }

    private void ToggleExpand(string group)
    {
        string key = RuleCatalog.NormalizeGroupName(group);
        if (!expanded.Remove(key))
            expanded.Add(key);
        RebuildRows();
    }

    // 分类勾选框：全开时点一下全关，否则全开。
    private void ToggleCategory(string group, string category)
    {
        GroupData? data = groups.FirstOrDefault(item =>
            item.Name.Equals(group, StringComparison.OrdinalIgnoreCase));
        if (data is null)
            return;
        List<OcrRule> rules = Categorize(data.Rules)
            .Where(item => item.Category.Equals(category, StringComparison.Ordinal))
            .SelectMany(item => item.Rules)
            .ToList();
        if (rules.Count == 0)
            return;
        // 分类勾选框：全开时点一下全关，否则全开。群关掉时整行不响应，避免把上次的条目开关改写。
        if (!settings.IsGroupEnabled(group))
            return;
        bool enable = !rules.All(rule => settings.IsRuleEnabledIgnoringGroup(group, rule));
        ApplyStateChange(() =>
        {
            foreach (OcrRule rule in rules)
                settings.SetRuleEnabled(group, rule, enable);
        });
    }

    // 点开关只是改状态：不重填列表，只刷新受影响的显示，滚动位置一点不动。
    private void ApplyStateChange(Action change, string? group = null)
    {
        change();
        if (!TrySave())
        {
            // 保存失败按磁盘内容回滚，界面跟着退回上一次真正生效的状态。
            settings = CaptureSettings.Load(appDirectory);
            RebuildRows();
            return;
        }

        if (group is not null)
        {
            bool enabled = settings.IsGroupEnabled(group);
            foreach (Row row in rows)
            {
                if (string.Equals(row.Group, group, StringComparison.OrdinalIgnoreCase))
                    row.GroupEnabled = enabled;
            }
        }

        rowList.Invalidate();
        UpdateSummary();
    }

    private bool TrySave()
    {
        try
        {
            settings.Save(appDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "抓取设置保存失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    // 勾选框永远在最左边同一列，层级只由文字缩进体现，这样一列方框是齐的。
    private static Rectangle CheckBoxBounds(Rectangle bounds)
    {
        int left = bounds.Left + 10;
        return new Rectangle(left, bounds.Top + ((bounds.Height - CheckBoxSize) / 2),
            CheckBoxSize, CheckBoxSize);
    }

    // 分类行上方留一条空白当分节线，方框和文字一起下移，免得方框看着悬在文字上面。
    private static Rectangle CategoryContentBounds(Rectangle bounds)
        => new(bounds.Left, bounds.Top + CategoryTopGap, bounds.Width, bounds.Height - CategoryTopGap);

    // 群行比分类行、条目行高，群与群之间自然拉开留白。
    private void MeasureRow(object? sender, MeasureItemEventArgs e)
    {
        e.ItemHeight = SubRowHeight;
        if (e.Index < 0 || e.Index >= rows.Count)
            return;
        e.ItemHeight = rows[e.Index].Kind switch
        {
            RowKind.Group => GroupRowHeight,
            RowKind.Category => CategoryRowHeight,
            _ => SubRowHeight
        };
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= rows.Count)
            return;
        Row row = rows[e.Index];
        bool groupOff = !row.GroupEnabled;
        using (var background = new SolidBrush(MainForm.CardBackground))
            e.Graphics.FillRectangle(background, e.Bounds);
        if ((e.State & DrawItemState.Selected) != 0)
        {
            using var highlight = new Pen(MainForm.SecondaryText);
            Rectangle frame = Rectangle.Inflate(e.Bounds, -1, -1);
            e.Graphics.DrawRectangle(highlight, frame);
        }

        int textLeft = e.Bounds.Left + 10 + CheckBoxSize + 10 + (row.Indent * TextIndentStep);
        Color textColor = row.Kind switch
        {
            RowKind.Group => groupOff ? MainForm.SecondaryText : MainForm.PrimaryText,
            // 分类行用强调色，和上下两级的正文一眼分开。
            RowKind.Category => groupOff ? MainForm.SecondaryText : MainForm.AccentAmber,
            _ => groupOff || !settings.IsRuleEnabledIgnoringGroup(row.Group, row.Rule!)
                ? MainForm.SecondaryText
                : MainForm.PrimaryText
        };
        Font font = row.Kind == RowKind.Group ? groupFont : rowList.Font;

        switch (row.Kind)
        {
            case RowKind.Info:
                TextRenderer.DrawText(e.Graphics, row.Text, font,
                    new Rectangle(textLeft, e.Bounds.Top, e.Bounds.Right - textLeft - 8, e.Bounds.Height),
                    MainForm.DangerRed,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                return;
            case RowKind.Group:
            {
                DrawCheckBox(e.Graphics, CheckBoxBounds(e.Bounds), row.GroupEnabled, triState: false);
                string arrow = expanded.Contains(RuleCatalog.NormalizeGroupName(row.Group)) ? "▾ " : "▸ ";
                string caption = arrow + row.Text;
                TextRenderer.DrawText(e.Graphics, caption, font,
                    new Rectangle(textLeft, e.Bounds.Top, e.Bounds.Right - textLeft - 8, e.Bounds.Height),
                    textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                if (!row.HasFolder)
                {
                    int nameWidth = TextRenderer.MeasureText(caption, font).Width;
                    TextRenderer.DrawText(e.Graphics, "（目录还没出现）", rowList.Font,
                        new Rectangle(textLeft + nameWidth + 8, e.Bounds.Top,
                            Math.Max(0, e.Bounds.Right - textLeft - nameWidth - 16), e.Bounds.Height),
                        MainForm.SecondaryText,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
                break;
            }
            case RowKind.Category:
            {
                GroupData? data = groups.FirstOrDefault(item =>
                    item.Name.Equals(row.Group, StringComparison.OrdinalIgnoreCase));
                List<OcrRule> categoryRules = data is null
                    ? new List<OcrRule>()
                    : Categorize(data.Rules)
                        .Where(item => item.Category.Equals(row.Category, StringComparison.Ordinal))
                        .SelectMany(item => item.Rules)
                        .ToList();
                int enabledCount = categoryRules.Count(rule =>
                    settings.IsRuleEnabledIgnoringGroup(row.Group, rule));
                bool? state = enabledCount == 0 ? false
                    : enabledCount == categoryRules.Count ? true
                    : null;
                Rectangle contentBounds = CategoryContentBounds(e.Bounds);
                DrawCheckBox(e.Graphics, CheckBoxBounds(contentBounds), state, triState: true);
                TextRenderer.DrawText(e.Graphics, row.Text, font,
                    new Rectangle(textLeft, contentBounds.Top, contentBounds.Right - textLeft - 8,
                        contentBounds.Height),
                    textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                break;
            }
            default:
            {
                bool enabled = settings.IsRuleEnabledIgnoringGroup(row.Group, row.Rule!);
                DrawCheckBox(e.Graphics, CheckBoxBounds(e.Bounds), enabled, triState: false);
                TextRenderer.DrawText(e.Graphics, row.Text, font,
                    new Rectangle(textLeft, e.Bounds.Top, e.Bounds.Right - textLeft - 8, e.Bounds.Height),
                    textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                break;
            }
        }
    }

    private static void DrawCheckBox(Graphics graphics, Rectangle box, bool? state, bool triState)
    {
        using var border = new Pen(MainForm.SecondaryText);
        graphics.DrawRectangle(border, box);
        bool filled = state == true;
        bool partial = triState && state is null;
        if (filled)
        {
            using var fill = new SolidBrush(MainForm.AccentAmber);
            graphics.FillRectangle(fill, Rectangle.Inflate(box, -3, -3));
        }
        else if (partial)
        {
            using var fill = new SolidBrush(MainForm.AccentAmber);
            graphics.FillRectangle(fill, new Rectangle(box.Left + 3, box.Top + 6, box.Width - 6, 2));
        }
    }
}
