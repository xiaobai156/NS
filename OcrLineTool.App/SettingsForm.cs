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
    };

    private readonly Button saveButton = new DarkButton() { Text = "保存" };
    private readonly Button cancelButton = new DarkButton() { Text = "取消" };

    internal UiSettings Result { get; private set; }

    internal SettingsForm(UiSettings current)
    {
        Result = current;
        showRecognizeCheckBox.Checked = current.ShowRecognizeButton;

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
        ClientSize = new Size(440, 196);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20, 16, 20, 16),
            BackColor = MainForm.WindowBackground,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var title = new Label
        {
            Text = "界面",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = MainForm.PrimaryText,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
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
            Result = new UiSettings(showRecognizeCheckBox.Checked);
            DialogResult = DialogResult.OK;
            Close();
        };
        buttonHost.Controls.Add(saveButton);
        buttonHost.Controls.Add(cancelButton);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(checkHost, 0, 1);
        layout.Controls.Add(hintLabel, 0, 2);
        layout.Controls.Add(buttonHost, 0, 3);
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
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = accent ? MainForm.AccentBlue : MainForm.CardBackground;
        button.ForeColor = accent ? Color.White : MainForm.PrimaryText;
        button.FlatAppearance.BorderColor = accent ? MainForm.AccentBlue : MainForm.BorderColor;
        button.FlatAppearance.MouseOverBackColor = accent
            ? Color.FromArgb(86, 156, 255)
            : Color.FromArgb(34, 47, 72);
        button.FlatAppearance.MouseDownBackColor = accent
            ? Color.FromArgb(28, 100, 215)
            : Color.FromArgb(42, 58, 88);
    }
}
