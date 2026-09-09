using System.Drawing.Imaging;
using OcrLineTool;

string sourceFolder = @"C:\Users\Administrator\Downloads\Telegram Desktop";
string cropFolder = @"C:\Users\Administrator\Desktop\每天工具\飞机抓到的分类\work\ocr-crops";
Directory.CreateDirectory(cropFolder);

var cases = new[]
{
    (File: "photo_4_2026-08-29_21-09-32.jpg", Keyword: "天之涯"),
    (File: "photo_5_2026-08-29_21-09-24.jpg", Keyword: "末日降临"),
    (File: "photo_9_2026-08-29_21-09-24.jpg", Keyword: "白梦")
};

IOcrClient client = OcrClientFactory.Create(CredentialSchedule.Today());
foreach (var item in cases)
{
    string source = Path.Combine(sourceFolder, item.File);
    string crop = Path.Combine(cropFolder, Path.GetFileNameWithoutExtension(item.File) + "_bottom.png");
    using (var image = new Bitmap(source))
    {
        int top = (int)(image.Height * 0.78);
        var area = new Rectangle(0, top, image.Width, image.Height - top);
        using Bitmap bottom = image.Clone(area, image.PixelFormat);
        bottom.Save(crop, ImageFormat.Png);
    }

    IReadOnlyList<string> lines = await client.RecognizeAsync(crop);
    Console.WriteLine($"=== {item.Keyword} ===");
    foreach (string line in lines)
        Console.WriteLine(line);
}
