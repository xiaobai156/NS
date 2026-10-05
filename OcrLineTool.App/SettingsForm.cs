namespace OcrLineTool;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox showRecognizeCheckBox = new()
    {
        Name = "showRecognizeButton",
        Text = "显示“开始识别”按钮",
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "显示开始识别按钮"
    };

    private readonly Label hintLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = MainForm.SecondaryText,
        Font = new Font("Microsoft YaHei UI", 9F),
        TextAlign = ContentAlignment.TopLeft,
        Text = "隐藏后左侧只显示“本地主识别”。此设置会记住，重启软件后保持。"
            + "手动复抓默认“本地 OCR（本机优先，读不到再云兜底）”；也可切回“云 OCR”。"
    };

    private readonly Label retryCaption = new()
    {
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        Text = "手动复抓使用："
    };

    private readonly RadioButton retryCloudOcrRadio = new()
    {
        Name = "retryUsesCloudOcr",
        Text = "云 OCR",
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "手动复抓使用云 OCR",
        Margin = new Padding(0, 2, 18, 0)
    };

    private readonly RadioButton retryLocalOcrRadio = new()
    {
        Name = "retryUsesLocalOcr",
        Text = "本地 OCR（推荐）",
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "手动复抓使用本地 OCR",
        Margin = new Padding(0, 2, 0, 0)
    };

    private readonly Label localOcrCaption = new()
    {
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        Text = "本地 OCR 模式："
    };

    private readonly RadioButton localOcrGpuRadio = new()
    {
        Name = "localOcrGpu",
        Text = "GPU（默认）",
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "本地 OCR GPU 模式",
        Margin = new Padding(0, 2, 18, 0)
    };

    private readonly RadioButton localOcrCpuRadio = new()
    {
        Name = "localOcrCpu",
        Text = "CPU",
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "本地 OCR CPU 模式",
        Margin = new Padding(0, 2, 0, 0)
    };

    private readonly Button saveButton = new DarkButton() { Text = "保存" };
    private readonly Button cancelButton = new DarkButton() { Text = "取消" };
    private readonly CheckBox autoVisibleLocalOcrCheckBox = new()
    {
        Name = "autoVisibleLocalOcr",
        Text = "资料群出现后自动本地主识别（五个群，图片稳定 1 分钟后执行）",
        AutoSize = true,
        ForeColor = MainForm.PrimaryText,
        BackColor = MainForm.CardBackground,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "资料群出现后自动本地主识别"
    };
    private readonly string appDirectory;

    internal UiSettings Result { get; private set; }

    internal SettingsForm(UiSettings current) : this(current, AppContext.BaseDirectory) { }

    internal SettingsForm(UiSettings current, string appDirectory)
    {
        this.appDirectory = appDirectory;
        Result = current;
        autoVisibleLocalOcrCheckBox.Checked = AutoLocalOcrSettings.Load(appDirectory).Enabled;
        showRecognizeCheckBox.Checked = current.ShowRecognizeButton;
        retryLocalOcrRadio.Checked = current.RetryUsesLocalOcr;
        retryCloudOcrRadio.Checked = !current.RetryUsesLocalOcr;
        localOcrGpuRadio.Checked = current.OcrDevice == LocalOcrDevice.Gpu;
        localOcrCpuRadio.Checked = current.OcrDevice == LocalOcrDevice.Cpu;

        Text = "设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = MainForm.WindowBackground;
        ForeColor = MainForm.PrimaryText;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 440);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(20, 18, 20, 18),
            BackColor = MainForm.WindowBackground,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var title = new Label
        {
            Text = "界面",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = MainForm.PrimaryText,
            Font = new Font(MainForm.HeadingFontFamily, 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var checkHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainForm.CardBackground,
            Padding = new Padding(12, 0, 12, 0),
            Margin = Padding.Empty
        };
        showRecognizeCheckBox.Location = new Point(12, 6);
        checkHost.Controls.Add(showRecognizeCheckBox);

        var retryHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = MainForm.CardBackground,
            Padding = new Padding(12, 8, 12, 4),
            Margin = Padding.Empty
        };
        retryHost.Controls.Add(retryCaption);
        retryHost.Controls.Add(retryCloudOcrRadio);
        retryHost.Controls.Add(retryLocalOcrRadio);

        var localOcrHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = MainForm.CardBackground,
            Padding = new Padding(12, 8, 12, 4),
            Margin = Padding.Empty
        };
        localOcrHost.Controls.Add(localOcrCaption);
        localOcrHost.Controls.Add(localOcrGpuRadio);
        localOcrHost.Controls.Add(localOcrCpuRadio);

        var autoHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainForm.CardBackground,
            Padding = new Padding(12, 5, 12, 5),
            Margin = Padding.Empty
        };
        autoVisibleLocalOcrCheckBox.Location = new Point(12, 6);
        autoHost.Controls.Add(autoVisibleLocalOcrCheckBox);

        var buttonHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = MainForm.WindowBackground,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };
        ConfigureDialogButton(saveButton, accent: true);
        ConfigureDialogButton(cancelButton, accent: false);
        cancelButton.Click += (_, _) =>
        {
            Result = current;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        saveButton.Click += (_, _) =>
        {
            try
            {
                AutoLocalOcrSettings autoSettings = AutoLocalOcrSettings.Load(appDirectory);
                autoSettings.Enabled = autoVisibleLocalOcrCheckBox.Checked;
                autoSettings.Save(appDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, exception.Message, "自动本地 OCR 设置保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = new UiSettings(
                showRecognizeCheckBox.Checked,
                retryLocalOcrRadio.Checked,
                localOcrCpuRadio.Checked ? LocalOcrDevice.Cpu : LocalOcrDevice.Gpu);
            DialogResult = DialogResult.OK;
            Close();
        };
        buttonHost.Controls.Add(saveButton);
        buttonHost.Controls.Add(cancelButton);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(checkHost, 0, 1);
        layout.Controls.Add(retryHost, 0, 2);
        layout.Controls.Add(localOcrHost, 0, 3);
        layout.Controls.Add(autoHost, 0, 4);
        layout.Controls.Add(hintLabel, 0, 5);
        layout.Controls.Add(buttonHost, 0, 6);
        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private static void ConfigureDialogButton(Button button, bool accent)
    {
        button.Name = button.Text;
        button.AccessibleName = button.Text;
        button.AutoSize = false;
        button.Size = new Size(96, 34);
        button.Margin = new Padding(8, 0, 0, 0);
        button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = accent ? MainForm.AccentAmber : MainForm.CardBackground;
        button.ForeColor = accent ? MainForm.OnAccent : MainForm.PrimaryText;
        button.FlatAppearance.BorderColor = accent ? MainForm.AccentAmber : MainForm.BorderColor;
        button.FlatAppearance.MouseOverBackColor = accent
            ? Color.FromArgb(255, 191, 121)
            : Color.FromArgb(61, 65, 57);
        button.FlatAppearance.MouseDownBackColor = accent
            ? Color.FromArgb(216, 138, 62)
            : Color.FromArgb(74, 79, 69);
    }
}
