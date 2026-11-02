namespace OcrLineTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 只允许开一个：两个实例会抢同一批群、同一个 GPU，还会互相覆盖结果文件，
        // 抓图完成的通知也会被随机分到其中一个。
        using var instanceLock = new Mutex(initiallyOwned: true, @"Local\OcrLineTool.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "这个程序已经在运行了，请不要重复打开。",
                "OCR 整行提取工具",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
