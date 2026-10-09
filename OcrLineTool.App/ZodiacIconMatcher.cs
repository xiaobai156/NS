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
    private const int IconCropHeight = 42;
    private const int ForegroundPadding = 2;
    private const double FirstCellCenter = 0.4325;
    private const double SecondCellCenter = 0.5095;
    private const double CellWidth = 0.07;
    private const double GridLineCoverage = 0.65;
    private static readonly object CacheLock = new();
    private static string? cachedConfigPath;
    private static IReadOnlyList<IconTemplate>? cachedTemplates;

    private sealed record IconTemplate(string Zodiac, byte[] Pixels);
    private sealed record IconTemplateConfig(string Zodiac, string File);
    private sealed record IconTemplateCatalog(IconTemplateConfig[] Templates);
    private sealed record CardRow(int Top, int Bottom, bool IsGridRow = false)
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
            IReadOnlyList<CardRow> rows = FindRows(image);
            if (rows.Count == 0)
                return null;

            CardRow? row = FindTargetRow(image, evidence, issue, rows);
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
                    loaded.Add(new IconTemplate(item.Zodiac, NormalizeForeground(image)));
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

    private static IReadOnlyList<CardRow> FindRows(Bitmap image)
    {
        IReadOnlyList<CardRow> gridRows = FindGridRows(image);
        return gridRows.Count >= 2 ? gridRows : FindYellowRows(image);
    }

    private static IReadOnlyList<CardRow> FindGridRows(Bitmap image)
    {
        int startY = (int)Math.Round(image.Height * 0.15);
        var active = new List<int>();
        for (int y = startY; y < image.Height; y++)
        {
            int bright = 0;
            for (int x = 0; x < image.Width; x++)
                if (IsGridLinePixel(image.GetPixel(x, y)))
                    bright++;
            if (bright >= image.Width * GridLineCoverage)
                active.Add(y);
        }

        var boundaries = new List<(int Top, int Bottom)>();
        foreach (int y in active)
        {
            if (boundaries.Count == 0 || y - boundaries[^1].Bottom > 2)
                boundaries.Add((y, y));
            else
                boundaries[^1] = (boundaries[^1].Top, y);
        }

        var rows = new List<CardRow>();
        for (int index = 0; index + 1 < boundaries.Count; index++)
        {
            int top = boundaries[index].Bottom + 1;
            int bottom = boundaries[index + 1].Top - 1;
            if (bottom - top >= IconCropHeight)
                rows.Add(new CardRow(top, bottom, true));
        }
        return rows;
    }

    private static IReadOnlyList<CardRow> FindYellowRows(Bitmap image)
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

        var rows = new List<CardRow>();
        foreach (int y in active)
        {
            if (rows.Count == 0 || y - rows[^1].Bottom > 3)
                rows.Add(new CardRow(y, y));
            else
                rows[^1] = rows[^1] with { Bottom = y };
        }
        return rows.Where(row => row.Bottom - row.Top >= 8).ToArray();
    }

    private static Rectangle[] CellRectangles(Bitmap image, CardRow row)
    {
        int width = Math.Max(12, (int)Math.Round(image.Width * CellWidth));
        if (row.IsGridRow)
        {
            int gridTop = Math.Max(0, row.Top);
            int gridBottom = Math.Min(image.Height, row.Bottom + 1);
            return
            [
                CellRectangle(image.Width, FirstCellCenter, width, gridTop, gridBottom),
                CellRectangle(image.Width, SecondCellCenter, width, gridTop, gridBottom)
            ];
        }

        int padY = Math.Max(2, (int)Math.Round(image.Width * 0.006));
        int top = Math.Max(0, row.Top - padY);
        int bottom = Math.Min(image.Height, row.Bottom + padY + 1);
        // The first row can use white icon backgrounds, leaving only a short
        // yellow edge for row detection. Keep a full icon-height crop in that
        // case instead of stretching a partial scan band.
        if (bottom - top < 24)
        {
            int height = Math.Max(IconCropHeight, (int)Math.Round(image.Width * 0.06));
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

    private static CardRow? FindTargetRow(
        Bitmap source,
        OcrEvidence evidence,
        int issue,
        IReadOnlyList<CardRow> rows)
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
        int index = issues.Length < 2 ? -1 : issues[0] - issue;
        if (issues.Length >= 2 && rows.Count >= issues.Length && index >= 0 && index < rows.Count)
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
        (CardRow Row, double Distance) best = candidateY
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

        if (cell.Height > IconCropHeight)
        {
            (string Zodiac, double Score)[] fullRow = RankCandidate(cell);
            if (IsConfident(fullRow))
                return fullRow[0].Zodiac;
        }

        var bestByZodiac = new Dictionary<string, double>(StringComparer.Ordinal);
        int maxTop = Math.Max(cell.Top, cell.Bottom - IconCropHeight);
        int step = cell.Height > IconCropHeight ? 2 : 1;
        for (int top = cell.Top; top <= maxTop; top += step)
        {
            int height = Math.Min(IconCropHeight, cell.Bottom - top);
            if (height < 8)
                continue;

            foreach ((string Zodiac, double Score) item in RankCandidate(
                         new Rectangle(cell.X, top, cell.Width, height)))
            {
                if (!bestByZodiac.TryGetValue(item.Zodiac, out double previous) || item.Score < previous)
                    bestByZodiac[item.Zodiac] = item.Score;
            }
        }

        var ordered = bestByZodiac
            .Select(item => (Zodiac: item.Key, Score: item.Value))
            .OrderBy(item => item.Score)
            .ToArray();
        if (ordered.Length == 0)
            return null;
        return IsConfident(ordered) ? ordered[0].Zodiac : null;

        (string Zodiac, double Score)[] RankCandidate(Rectangle candidate)
        {
            byte[] pixels = NormalizeForeground(image, candidate);
            return templates.GroupBy(
                         template => template.Zodiac,
                         StringComparer.Ordinal)
                .Select(group => (Zodiac: group.Key, Score: group
                    .Select(template => Difference(pixels, template.Pixels))
                    .Min()))
                .OrderBy(item => item.Score)
                .ToArray();
        }

        static bool IsConfident((string Zodiac, double Score)[] scores)
        {
            if (scores.Length == 0)
                return false;
            double margin = scores.Length < 2 ? double.MaxValue : scores[1].Score - scores[0].Score;
            // Background-independent matching still varies with screenshot scale;
            // a clear margin is required so an uncertain cell stays missing.
            return scores[0].Score <= 60 && margin >= 4.5;
        }
    }

    private static bool IsGridLinePixel(Color color) =>
        color.R >= 190 && color.G >= 190 && color.B >= 190;

    private static bool IsYellow(Color color) =>
        color.R >= 220 && color.G >= 175 && color.B <= 140;

    private static byte[] Normalize(Bitmap source) =>
        Normalize(source, new Rectangle(0, 0, source.Width, source.Height));

    private static byte[] NormalizeForeground(Bitmap source) =>
        NormalizeForeground(source, new Rectangle(0, 0, source.Width, source.Height));

    private static byte[] NormalizeForeground(Bitmap source, Rectangle sourceRect)
    {
        Rectangle bounded = Rectangle.Intersect(
            new Rectangle(0, 0, source.Width, source.Height),
            sourceRect);
        if (bounded.Width < 2 || bounded.Height < 2)
            return Normalize(source, bounded);

        int left = bounded.Right;
        int top = bounded.Bottom;
        int right = bounded.Left - 1;
        int bottom = bounded.Top - 1;
        for (int y = bounded.Top; y < bounded.Bottom; y++)
        for (int x = bounded.Left; x < bounded.Right; x++)
        {
            if (!IsForegroundPixel(source.GetPixel(x, y)))
                continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        if (right < left || bottom < top)
            return Normalize(source, bounded);

        const int padding = ForegroundPadding;
        left = Math.Max(bounded.Left, left - padding);
        top = Math.Max(bounded.Top, top - padding);
        right = Math.Min(bounded.Right - 1, right + padding);
        bottom = Math.Min(bounded.Bottom - 1, bottom + padding);

        var normalized = new Bitmap(
            NormalizedWidth,
            NormalizedHeight,
            PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(normalized))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, NormalizedWidth, NormalizedHeight),
                new Rectangle(left, top, right - left + 1, bottom - top + 1),
                GraphicsUnit.Pixel);
        }

        var pixels = new byte[NormalizedWidth * NormalizedHeight * 3];
        int index = 0;
        for (int y = 0; y < NormalizedHeight; y++)
        for (int x = 0; x < NormalizedWidth; x++)
        {
            Color color = normalized.GetPixel(x, y);
            if (!IsForegroundPixel(color))
                color = Color.White;
            pixels[index++] = color.R;
            pixels[index++] = color.G;
            pixels[index++] = color.B;
        }
        normalized.Dispose();
        return pixels;
    }

    private static bool IsForegroundPixel(Color color)
    {
        double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        return luminance < 175;
    }

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
