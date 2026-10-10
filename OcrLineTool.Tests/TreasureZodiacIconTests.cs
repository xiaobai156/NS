using System.Drawing;
using System.Drawing.Imaging;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class TreasureZodiacIconTests
{
    private const string Source = @"C:\Users\Administrator\Desktop\每天工具\飞机抓图\结果\10.9-新澳六合彩资料\全部图片\20261009_172921_02b811f3_651029.jpg";

    private static OcrRule Rule(string id = "藏宝杀一肖") => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
        .Single(rule => rule.Id == id);

    public static IEnumerable<object[]> Glyphs() => "鼠牛虎兔龙蛇马羊猴鸡狗猪"
        .SelectMany(zodiac => new[] { "red", "white", "blue" }.Select(background => new object[] { zodiac.ToString(), background }));

    [Theory]
    [MemberData(nameof(Glyphs))]
    public void AllTwelveGlyphsUseOneCellAndIgnoreItsBackground(string zodiac, string background)
    {
        WithCard(zodiac, background, path =>
        {
            OcrEvidence evidence = Evidence(path);
            Assert.Equal(zodiac, RuleEngine.ExtractFinalValue(evidence, 318, Rule()));
            Assert.Null(ZodiacIconMatcher.TryExtractPair(evidence, 318));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence, 320, Rule()));
        });
    }

    [Fact]
    public void MissingOrContradictoryIssueLabelsCannotSelectAnAdjacentRow()
    {
        WithCard("兔", "red", path =>
        {
            OcrEvidence evidence = Evidence(path);
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = evidence.Items.Take(1).ToArray() }, 318, Rule()));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with
            {
                Items = [evidence.Items[0], evidence.Items[1] with { Text = "316期" }]
            }, 318, Rule()));
        });
    }

    [Fact]
    public void ABlankCellCannotUseHallucinatedTextOrTheOpeningResult()
    {
        WithCard(null, "red", path =>
        {
            OcrEvidence evidence = Evidence(path);
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with
            {
                Items = [..evidence.Items, new("318期 特肖杀【兔】 鸡10中", new OcrBox(190, 220, 600, 35))]
            }, 318, Rule()));
        });
    }

    [Fact]
    public void TheUsersCardReadsTheAnimalAndKeepsItsPartialBottomRowMissing()
    {
        if (!File.Exists(Source))
            return;
        OcrEvidence evidence = new(Source, Source, "", "", "original", Enumerable.Range(0, 12)
            .Select(index => new OcrLineEvidence($"{282 - index}期", new OcrBox(10, 210 + index * 82, 130, 65)))
            .ToArray());
        string[] expected = ["鼠", "猪", "蛇", "龙", "猪", "兔", "狗", "兔", "马", "龙", "羊"];
        for (int index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], RuleEngine.ExtractFinalValue(evidence, 282 - index, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 271, Rule()));
        Assert.Null(ZodiacIconMatcher.TryExtractPair(evidence, 282));
    }

    [Fact]
    public void ATruncatedTemplateViewCannotMoveLatestIssuesIntoHistoricalRows()
    {
        if (!File.Exists(Source))
            return;
        string cropPath = Path.Combine(Path.GetTempPath(), $"treasure-truncated-{Guid.NewGuid():N}.png");
        try
        {
            using (var source = new Bitmap(Source))
            using (Bitmap crop = source.Clone(new Rectangle(0, 90, 800, 300), PixelFormat.Format24bppRgb))
                crop.Save(cropPath, ImageFormat.Png);
            OcrEvidence evidence = new(Source, cropPath, "", "", "template-crop",
            [
                new("282期", new OcrBox(22, 132, 107, 44)),
                new("281期", new OcrBox(22, 214, 107, 44))
            ]);
            Assert.Null(RuleEngine.ExtractFinalValue(evidence, 282, Rule()));
        }
        finally { File.Delete(cropPath); }
    }

    [Fact]
    public void TheFixedCropRetainsIssueGeometryAndDoesNotReadDragonKing()
    {
        WithCard("鼠", "red", path =>
        {
            string cropPath = Path.Combine(Path.GetTempPath(), $"treasure-crop-{Guid.NewGuid():N}.png");
            try
            {
                using (var source = new Bitmap(path))
                using (Bitmap crop = source.Clone(new Rectangle(0, 90, source.Width, source.Height - 90), PixelFormat.Format24bppRgb))
                    crop.Save(cropPath, ImageFormat.Png);
                OcrEvidence evidence = Evidence(path) with
                {
                    InputPath = cropPath,
                    Items = Evidence(path).Items.Select(item => item with { Box = item.Box! with { Y = item.Box.Y - 90 } }).ToArray()
                };
                Assert.Equal("鼠", RuleEngine.ExtractFinalValue(evidence, 318, Rule()));
            }
            finally { File.Delete(cropPath); }
        });
        const string dragon = @"C:\Users\Administrator\Desktop\每天工具\飞机抓图\结果\10.9-新澳六合彩资料\全部图片\20261009_173121_02b811f3_651057.jpg";
        if (File.Exists(dragon))
            Assert.Null(RuleEngine.ExtractFinalValue(Evidence(dragon), 318, Rule()));
    }

    private static OcrEvidence Evidence(string path) => new(path, path, "", "", "original",
    [
        new("318期", new OcrBox(12, 222, 120, 35)),
        new("317期", new OcrBox(12, 304, 120, 35))
    ]);

    private static void WithCard(string? zodiac, string background, Action<string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), $"treasure-icons-{Guid.NewGuid():N}.png");
        try
        {
            using (var card = new Bitmap(800, 366))
            {
                using (Graphics graphics = Graphics.FromImage(card))
                {
                    graphics.Clear(Color.Yellow);
                    for (int y = 200; y <= 364; y += 82)
                        graphics.FillRectangle(Brushes.LightGray, 0, y, 800, 3);
                }
                if (zodiac is not null)
                {
                    using var icon = new Bitmap(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory),
                        "生肖图标", zodiac + ".png"));
                    var exterior = new HashSet<Point>();
                    var pending = new Queue<Point>();
                    for (int x = 0; x < icon.Width; x++)
                    {
                        pending.Enqueue(new Point(x, 0));
                        pending.Enqueue(new Point(x, icon.Height - 1));
                    }
                    for (int y = 0; y < icon.Height; y++)
                    {
                        pending.Enqueue(new Point(0, y));
                        pending.Enqueue(new Point(icon.Width - 1, y));
                    }
                    while (pending.TryDequeue(out Point point))
                    {
                        if (point.X < 0 || point.Y < 0 || point.X >= icon.Width || point.Y >= icon.Height || exterior.Contains(point))
                            continue;
                        Color color = icon.GetPixel(point.X, point.Y);
                        if (.299 * color.R + .587 * color.G + .114 * color.B < 175)
                            continue;
                        exterior.Add(point);
                        pending.Enqueue(new Point(point.X - 1, point.Y));
                        pending.Enqueue(new Point(point.X + 1, point.Y));
                        pending.Enqueue(new Point(point.X, point.Y - 1));
                        pending.Enqueue(new Point(point.X, point.Y + 1));
                    }
                    for (int y = 0; y < icon.Height; y++)
                    for (int x = 0; x < icon.Width; x++)
                    {
                        Color color = icon.GetPixel(x, y);
                        if (exterior.Contains(new Point(x, y)))
                            color = background == "red" ? Color.Red : background == "blue" ? Color.SkyBlue : Color.White;
                        card.SetPixel(365 + x, 220 + y, color);
                        card.SetPixel(365 + x, 302 + y, color);
                    }
                }
                card.Save(path, ImageFormat.Png);
            }
            check(path);
        }
        finally { File.Delete(path); }
    }
}
