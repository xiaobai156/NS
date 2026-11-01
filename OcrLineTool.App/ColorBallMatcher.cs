using System.Text.RegularExpressions;

namespace OcrLineTool;

/// <summary>Reads the two coloured balls in the fixed 大赢家 table, never OCR colour words.</summary>
public static class ColorBallMatcher
{
    private sealed record Row(int Top, int Bottom)
    {
        public double Center => (Top + Bottom) / 2d;
        public int Height => Bottom - Top;
    }

    public static RuleExtractionResult ExtractMissingWave(OcrEvidence evidence, int issue)
    {
        if (issue <= 0 || string.IsNullOrWhiteSpace(evidence.SourceHash) || string.IsNullOrWhiteSpace(evidence.InputHash))
            return RuleExtractionResult.Missing;
        var identity = new OcrEvidenceIdentity(evidence.SourcePath, evidence.InputPath,
            evidence.SourceHash, evidence.InputHash, evidence.ViewId);
        identity.EnsureCurrent();
        try
        {
            // Coordinates and pixels come from the same OCR input. A template crop or
            // enlargement therefore needs no guessed offset back into the original.
            using var image = new Bitmap(evidence.InputPath);
            IReadOnlyList<Row> rows = FindRows(image);
            var targets = new HashSet<int>();
            foreach (var view in (evidence.TokenItems ?? evidence.Items).GroupBy(item => item.ViewId))
            {
                // Compact views change X coordinates; this card only uses uncompressed views.
                if (view.Key.Split('/').Contains("compact", StringComparer.OrdinalIgnoreCase))
                    continue;
                var located = new HashSet<(int Issue, int Row)>();
                foreach (OcrLineEvidence item in view)
                {
                    // Tencent retains percentage scores, including in cached/bound evidence.
                    double maximumConfidence = item.ViewId.Split('/').Contains("tencent", StringComparer.OrdinalIgnoreCase) ? 100 : 1;
                    if (item.Box is not { Width: > 0, Height: > 0 } box
                        || (item.Confidence is double confidence && (!double.IsFinite(confidence)
                            || confidence < maximumConfidence * .85 || confidence > maximumConfidence))
                        || box.X < 0 || box.Right > image.Width * .19)
                        continue;
                    Match token = Regex.Match(item.Text, @"^\s*(\d{1,6})\s*期\s*$");
                    if (!token.Success || !int.TryParse(token.Groups[1].Value, out int period))
                        continue;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        Row row = rows[i];
                        if (box.Y >= row.Top - 2 && box.Bottom <= row.Bottom + 2
                            && box.Height <= row.Height * .9 && Math.Abs(box.CenterY - row.Center) <= row.Height * .25)
                            located.Add((period, i));
                    }
                }
                // An explicit target label is mandatory. Other located labels corroborate
                // the descending sequence; they never supply a missing target period.
                if (located.Select(item => item.Row).Distinct().Count() < 2)
                    continue;
                if (located.Select(item => item.Issue + item.Row).Distinct().Count() != 1)
                    return RuleExtractionResult.Missing;
                foreach (var target in located.Where(item => item.Issue == issue)) targets.Add(target.Row);
            }

            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (int target in targets)
            {
                string? pair = ReadBalls(image, rows[target]);
                if (pair is null)
                    return RuleExtractionResult.Missing;
                values.Add("红蓝绿".Single(color => !pair.Contains(color)) + "波");
            }
            identity.EnsureCurrent();
            return values.Count > 1 ? RuleExtractionResult.Conflict
                : values.Count == 1 && targets.Count == 1 ? RuleExtractionResult.Success(values.Single())
                : RuleExtractionResult.Missing;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return RuleExtractionResult.Missing;
        }
    }

    private static IReadOnlyList<Row> FindRows(Bitmap image)
    {
        var borders = new List<(int Top, int Bottom)>();
        int step = Math.Max(1, image.Width / 250);
        for (int y = 0; y < image.Height; y++)
        {
            int white = 0, total = 0;
            for (int x = (int)(image.Width * .025); x < image.Width * .975; x += step)
            {
                Color c = image.GetPixel(x, y);
                int min = Math.Min(c.R, Math.Min(c.G, c.B)), max = Math.Max(c.R, Math.Max(c.G, c.B));
                // JPEG chroma subsampling can tint thin white borders yellow.
                // The gold cell background still fails both lightness and neutrality.
                if (min >= 185 && max - min <= 65) white++;
                total++;
            }
            if (total == 0 || white < total * .82) continue;
            if (borders.Count == 0 || y - borders[^1].Bottom > 2) borders.Add((y, y));
            else borders[^1] = (borders[^1].Top, y);
        }
        var rows = new List<Row>();
        for (int i = 1; i < borders.Count; i++)
        {
            var row = new Row(borders[i - 1].Bottom + 1, borders[i].Top);
            if (row.Height >= image.Width * .055 && row.Height <= image.Width * .13) rows.Add(row);
        }
        // The partial last row has no lower border and cannot be accepted.
        return rows;
    }

    private static string? ReadBalls(Bitmap image, Row row)
    {
        int left = (int)(image.Width * .43), right = (int)(image.Width * .64);
        int top = row.Top + (int)(row.Height * .12), bottom = row.Bottom - (int)(row.Height * .12);
        int width = right - left, height = bottom - top;
        var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) pixels[y * width + x] = Classify(image.GetPixel(left + x, top + y));
        var balls = new List<(double X, char Color)>();
        for (int seed = 0; seed < pixels.Length; seed++)
        {
            if (pixels[seed] < 0) continue;
            var pending = new Queue<int>();
            var counts = new int[3];
            int minX = width, minY = height, maxX = 0, maxY = 0, count = 0;
            Visit(seed);
            while (pending.TryDequeue(out int p))
            {
                int x = p % width, y = p / width;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                if (x > 0) Visit(p - 1);
                if (x + 1 < width) Visit(p + 1);
                if (y > 0) Visit(p - width);
                if (y + 1 < height) Visit(p + width);
            }
            // Ignore JPEG edge speckles; substantial malformed components are not balls.
            if (count < Math.Max(12, image.Width * image.Width * .0001)) continue;
            int w = maxX - minX + 1, h = maxY - minY + 1;
            int dominant = Array.IndexOf(counts, counts.Max());
            if (w < image.Width * .025 || w > image.Width * .08
                || h < image.Width * .025 || h > image.Width * .08
                || w < h * .7 || w > h * 1.4 || count < w * h * .55 || counts[dominant] < count * .9
                || Math.Abs(top + (minY + maxY) / 2d - row.Center) > row.Height * .22)
                return null;
            balls.Add((left + (minX + maxX) / 2d, "红蓝绿"[dominant]));

            void Visit(int p)
            {
                if (pixels[p] < 0) return;
                counts[pixels[p]]++; count++;
                pixels[p] = -1;
                pending.Enqueue(p);
            }
        }
        balls.Sort((a, b) => a.X.CompareTo(b.X));
        return balls.Count == 2 && balls[0].Color != balls[1].Color
            && Math.Abs(balls[0].X / image.Width - .49375) <= .02
            && Math.Abs(balls[1].X / image.Width - .56875) <= .02
            ? string.Concat(balls[0].Color, balls[1].Color) : null;
    }

    private static int Classify(Color color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B)), min = Math.Min(color.R, Math.Min(color.G, color.B));
        if (max < 102 || (max - min) / (double)max < .6) return -1;
        float hue = color.GetHue();
        return hue <= 20 || hue >= 340 ? 0 : hue is >= 185 and <= 255 ? 1 : hue is >= 85 and <= 155 ? 2 : -1;
    }
}
