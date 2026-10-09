using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcrLineTool;

/// <summary>
/// Reads the two fixed animal cells on the 龙王庙【绝杀两肖】 card. The card
/// uses bitmap glyphs, so ordinary OCR text is only a fallback for this rule.
/// </summary>
public static class ZodiacIconMatcher
{
    private const int NormalizedWidth = 55;
    private const int NormalizedHeight = 42;
    private const double FirstCellCenter = 0.4325;
    private const double SecondCellCenter = 0.5095;
    private const double CellWidth = 0.07;
    private static readonly object CacheLock = new();
    private static string? cachedConfigPath;
    private static IReadOnlyList<IconTemplate>? cachedTemplates;

    private sealed record IconTemplate(string Zodiac, byte[] Pixels);
    private sealed record IconTemplateConfig(string Zodiac, string File);
    private sealed record IconTemplateCatalog(IconTemplateConfig[] Templates);
    private sealed record YellowRow(int Top, int Bottom)
    {
        public int Center => (Top + Bottom) / 2;
    }

    public static string? TryExtractPair(OcrEvidence evidence, int issue) =>
        TryExtractPair(evidence, issue, AppContext.BaseDirectory);

    internal static string? TryExtractPair(OcrEvidence evidence, int issue, string appDirectory)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (issue <= 0 || string.IsNullOrWhiteSpace(evidence.SourcePath) ||
            !File.Exists(evidence.SourcePath))
            return null;

        IReadOnlyList<IconTemplate> templates = LoadTemplates(appDirectory);
        if (templates.Select(item => item.Zodiac).Distinct(StringComparer.Ordinal).Count() != 12)
            return null;

        try
        {
            using var image = new Bitmap(evidence.SourcePath);
            IReadOnlyList<YellowRow> rows = FindRows(image);
            if (rows.Count == 0)
                return null;

            YellowRow? row = FindTargetRow(image, evidence, issue, rows);
            if (row is null)
                return null;

            Rectangle[] cells = CellRectangles(image, row);
            string? first = MatchCell(image, cells[0], templates);
            string? second = MatchCell(image, cells[1], templates);
            if (first is null || second is null || first == second)
                return null;
            return first + second;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<IconTemplate> LoadTemplates(string appDirectory)
    {
        string configPath = Path.Combine(
            ResultFilePaths.ConfigurationDirectory(appDirectory), "生肖图标", "catalog.json");
        lock (CacheLock)
        {
            if (string.Equals(cachedConfigPath, configPath, StringComparison.OrdinalIgnoreCase) &&
                cachedTemplates is not null)
                return cachedTemplates;

            try
            {
                IconTemplateCatalog? catalog = JsonSerializer.Deserialize<IconTemplateCatalog>(
                    File.ReadAllText(configPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var loaded = new List<IconTemplate>();
                foreach (IconTemplateConfig item in catalog?.Templates ?? [])
                {
                    if (string.IsNullOrWhiteSpace(item.Zodiac) || string.IsNullOrWhiteSpace(item.File))
                        continue;
                    string path = Path.Combine(
                        Path.GetDirectoryName(configPath)!,
                        item.File.Replace('/', Path.DirectorySeparatorChar));
                    using var image = new Bitmap(path);
                    loaded.Add(new IconTemplate(item.Zodiac, Normalize(image)));
                }

                cachedConfigPath = configPath;
                cachedTemplates = loaded.ToArray();
                return cachedTemplates;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
                cachedConfigPath = configPath;
                cachedTemplates = [];
                return cachedTemplates;
            }
        }
    }

    private static IReadOnlyList<YellowRow> FindRows(Bitmap image)
    {
        int startY = (int)Math.Round(image.Height * 0.15);
        int[] centers =
        [
            (int)Math.Round(image.Width * FirstCellCenter),
            (int)Math.Round(image.Width * SecondCellCenter)
        ];
        int halfWidth = Math.Max(8, (int)Math.Round(image.Width * CellWidth / 2));
        var active = new List<int>();
        for (int y = startY; y < image.Height; y++)
        {
            int yellow = 0;
            foreach (int center in centers)
            {
                int left = Math.Max(0, center - halfWidth);
                int right = Math.Min(image.Width - 1, center + halfWidth);
                for (int x = left; x <= right; x++)
                    if (IsYellow(image.GetPixel(x, y)))
                        yellow++;
            }
            if (yellow >= 2)
                active.Add(y);
        }

        var rows = new List<YellowRow>();
        foreach (int y in active)
        {
            if (rows.Count == 0 || y - rows[^1].Bottom > 3)
                rows.Add(new YellowRow(y, y));
            else
                rows[^1] = rows[^1] with { Bottom = y };
        }
        return rows.Where(row => row.Bottom - row.Top >= 8).ToArray();
    }

    private static Rectangle[] CellRectangles(Bitmap image, YellowRow row)
    {
        int width = Math.Max(12, (int)Math.Round(image.Width * CellWidth));
        int padY = Math.Max(2, (int)Math.Round(image.Width * 0.006));
        int top = Math.Max(0, row.Top - padY);
        int bottom = Math.Min(image.Height, row.Bottom + padY + 1);
        // The first row can use white icon backgrounds, leaving only a short
        // yellow edge for row detection. Keep a full icon-height crop in that
        // case instead of stretching a partial scan band.
        if (bottom - top < 24)
        {
            int height = Math.Max(42, (int)Math.Round(image.Width * 0.06));
            top = Math.Max(0, row.Center - height / 2);
            bottom = Math.Min(image.Height, top + height);
        }
        return
        [
            CellRectangle(image.Width, FirstCellCenter, width, top, bottom),
            CellRectangle(image.Width, SecondCellCenter, width, top, bottom)
        ];
    }

    private static Rectangle CellRectangle(int imageWidth, double centerRatio, int width, int top, int bottom)
    {
        int center = (int)Math.Round(imageWidth * centerRatio);
        int left = center - width / 2;
        return new Rectangle(left, top, width, Math.Max(1, bottom - top));
    }

    private static YellowRow? FindTargetRow(
        Bitmap source,
        OcrEvidence evidence,
        int issue,
        IReadOnlyList<YellowRow> rows)
    {
        // The source is a fixed card and OCR may run on a top crop. Issue order
        // therefore gives a safer row identity than translating crop-local Y
        // coordinates back to the full source image.
        int[] issues = evidence.Items
            .SelectMany(item => Regex.Matches(item.Text, @"(?<!\d)(\d{3,6})\s*期")
                .Select(match => int.TryParse(match.Groups[1].Value, out int value) ? value : 0))
            .Where(value => value > 0)
            .Distinct()
            .OrderByDescending(value => value)
            .ToArray();
        int index = issues.Length == 0 ? -1 : issues[0] - issue;
        if (index >= 0 && index < rows.Count)
            return rows[index];

        // If an OCR provider emits only a bare target token, retain a geometry
        // fallback for callers that already supplied source-space coordinates.
        OcrBox[] positioned = evidence.Items
            .Where(item => item.Box is not null && IsTargetIssue(item.Text, issue))
            .Select(item => item.Box!)
            .ToArray();
        if (positioned.Length == 0)
            return null;

        double[] candidateY = positioned.Select(box => box.CenterY).ToArray();
        (YellowRow Row, double Distance) best = candidateY
            .SelectMany(y => rows.Select(row => (Row: row, Distance: Math.Abs(row.Center - y))))
            .OrderBy(item => item.Distance)
            .First();
        return best.Distance <= Math.Max(30, source.Width * 0.08) ? best.Row : null;
    }

    private static bool IsTargetIssue(string text, int issue)
    {
        if (Regex.IsMatch(text, $@"(?<!\d){issue}\s*期", RegexOptions.CultureInvariant))
            return true;
        return Regex.IsMatch(text.Trim(), $@"^第?\s*{issue}$", RegexOptions.CultureInvariant);
    }

    private static string? MatchCell(Bitmap image, Rectangle requested, IReadOnlyList<IconTemplate> templates)
    {
        Rectangle cell = Rectangle.Intersect(
            new Rectangle(0, 0, image.Width, image.Height), requested);
        if (cell.Width < 8 || cell.Height < 8)
            return null;

        byte[] pixels = Normalize(image, cell);
        var ordered = templates
            .Select(template => (Template: template, Score: Difference(pixels, template.Pixels)))
            .OrderBy(item => item.Score)
            .ToArray();
        if (ordered.Length == 0)
            return null;
        double margin = ordered.Length < 2 ? double.MaxValue : ordered[1].Score - ordered[0].Score;
        return ordered[0].Score <= 42 && margin >= 4 ? ordered[0].Template.Zodiac : null;
    }

    private static bool IsYellow(Color color) =>
        color.R >= 220 && color.G >= 175 && color.B <= 140;

    private static byte[] Normalize(Bitmap source) =>
        Normalize(source, new Rectangle(0, 0, source.Width, source.Height));

    private static byte[] Normalize(Bitmap source, Rectangle sourceRect)
    {
        using var normalized = new Bitmap(
            NormalizedWidth, NormalizedHeight, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(normalized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, NormalizedWidth, NormalizedHeight),
                sourceRect,
                GraphicsUnit.Pixel);
        }

        var pixels = new byte[NormalizedWidth * NormalizedHeight * 3];
        int index = 0;
        for (int y = 0; y < NormalizedHeight; y++)
        for (int x = 0; x < NormalizedWidth; x++)
        {
            Color color = normalized.GetPixel(x, y);
            pixels[index++] = color.R;
            pixels[index++] = color.G;
            pixels[index++] = color.B;
        }
        return pixels;
    }

    private static double Difference(byte[] left, byte[] right)
    {
        long total = 0;
        for (int index = 0; index < left.Length; index++)
            total += Math.Abs(left[index] - right[index]);
        return total / (double)left.Length;
    }
}
