using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OcrLineTool;

internal sealed record SummaryRowRecoveryResult(string Value, IReadOnlyList<string> StripLines);

/// <summary>
/// Recovers a single author row from a shared summary sheet by cropping the
/// band around that author's own line and re-running local OCR on the strip.
/// The strip contains only that row, so no neighbouring author can supply it.
/// </summary>
internal static class SummaryRowRecovery
{
    private const int StripDetectionMaxSide = 1440;
    private const int StripScale = 2;
    private const double BandHalfPitch = 0.4;

    internal static async Task<SummaryRowRecoveryResult?> TryRecoverAsync(
        PaddleLocalOcrClient client,
        string imagePath,
        OcrRule rule,
        IReadOnlyList<OcrRule> catalog,
        double titleRatio,
        int? detectionMaxSide,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.RecognizeBatchAsync(
                [imagePath],
                progress: null,
                titleRatio: titleRatio,
                detectionMaxSide: detectionMaxSide,
                useCache: true,
                cancellationToken: cancellationToken,
                model: PaddleOcrModels.LocalPrimary);
        }
        catch (OcrException)
        {
            return null;
        }

        if (!client.LastEvidence.TryGetValue(imagePath, out OcrEvidence? located) || located is null)
            return null;

        string own = RuleCatalog.NormalizeGroupName(rule.Keyword);
        string label = string.IsNullOrWhiteSpace(rule.Label)
            ? string.Empty
            : RuleCatalog.NormalizeGroupName(rule.Label);
        OcrLineEvidence[] rows = located.Items
            .Where(item => item.Box is not null
                && item.Text.Contains('正', StringComparison.Ordinal))
            .ToArray();
        if (rows.Length < 2)
            return null;

        int[] centers = rows.Select(item => (int)Math.Round(item.Box!.CenterY)).Order().ToArray();
        OcrLineEvidence[] ownRows = rows
            .Where(item => ContainsName(RuleCatalog.NormalizeGroupName(item.Text), own, label))
            .ToArray();
        if (ownRows.Length != 1)
            return null;

        int targetCenter = (int)Math.Round(ownRows[0].Box!.CenterY);
        if (ComputeBand(centers, targetCenter) is not { } band)
            return null;

        return await CropAndReadStripAsync(
            client, imagePath, band, cancellationToken,
            stripLines => RuleEngine.ExtractSummaryRowZodiacFromStrip(stripLines, rule, catalog));
    }

    // Recovers a single period row of a dedicated card: the full-image OCR
    // missed the target row's value while the candidate scan still carries
    // that row, so crop the row band and re-read it locally.
    internal static Task<SummaryRowRecoveryResult?> TryRecoverIssueRowAsync(
        PaddleLocalOcrClient client,
        string imagePath,
        int issue,
        double titleRatio,
        int? detectionMaxSide,
        CancellationToken cancellationToken) =>
        TryRecoverIssueRowCoreAsync(
            client, imagePath, issue, titleRatio, detectionMaxSide, cancellationToken,
            stripLines => RuleEngine.ExtractIssueRowZodiacFromStrip(stripLines, issue));

    internal static Task<SummaryRowRecoveryResult?> TryRecoverIssueRowNumbersAsync(
        PaddleLocalOcrClient client,
        string imagePath,
        int issue,
        OcrRule rule,
        double titleRatio,
        int? detectionMaxSide,
        CancellationToken cancellationToken) =>
        TryRecoverIssueRowCoreAsync(
            client, imagePath, issue, titleRatio, detectionMaxSide, cancellationToken,
            stripLines => RuleEngine.ExtractFinalValue(stripLines, issue, rule));

    private static async Task<SummaryRowRecoveryResult?> TryRecoverIssueRowCoreAsync(
        PaddleLocalOcrClient client,
        string imagePath,
        int issue,
        double titleRatio,
        int? detectionMaxSide,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<string>, string?> parse)
    {
        try
        {
            await client.RecognizeBatchAsync(
                [imagePath],
                progress: null,
                titleRatio: titleRatio,
                detectionMaxSide: detectionMaxSide,
                useCache: true,
                cancellationToken: cancellationToken,
                model: PaddleOcrModels.LocalPrimary);
        }
        catch (OcrException)
        {
            return null;
        }

        if (!client.LastEvidence.TryGetValue(imagePath, out OcrEvidence? located) || located is null)
            return null;
        if (ComputeIssueRowBand(located.Items, issue) is not { } band)
            return null;

        return await CropAndReadStripAsync(client, imagePath, band, cancellationToken, parse);
    }

    // Period rows are the anchors: the value may sit on the next line
    // ("257期" / "禁" / "一肖羊"), so the band spans from the target row down
    // to just before the next period row.
    internal static (int Top, int Height)? ComputeIssueRowBand(
        IReadOnlyList<OcrLineEvidence> items,
        int issue)
    {
        OcrLineEvidence[] issueRows = items
            .Where(item => item.Box is not null
                && item.Text.Contains('期', StringComparison.Ordinal))
            .OrderBy(item => item.Box!.Y)
            .ToArray();
        if (issueRows.Length < 2)
            return null;
        OcrLineEvidence[] targets = issueRows
            .Where(item => RuleEngine.LineContainsIssue(item.Text, issue))
            .ToArray();
        if (targets.Length != 1)
            return null;

        OcrLineEvidence target = targets[0];
        OcrLineEvidence? next = issueRows
            .Where(item => item.Box!.Y > target.Box!.Y)
            .OrderBy(item => item.Box!.Y)
            .FirstOrDefault();
        int[] centers = issueRows
            .Select(item => (int)Math.Round(item.Box!.CenterY))
            .ToArray();
        if (MedianPitch(centers) is not { } pitch)
            return null;
        int top = Math.Max(0, target.Box!.Y - 4);
        int bottom = next is not null
            ? next.Box!.Y - 4
            : target.Box.Bottom + (int)Math.Round(pitch * 0.8);
        return bottom - top < 12 ? null : (top, bottom - top);
    }

    private static async Task<SummaryRowRecoveryResult?> CropAndReadStripAsync(
        PaddleLocalOcrClient client,
        string imagePath,
        (int Top, int Height) band,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<string>, string?> parse)
    {
        string stripPath = Path.Combine(
            Path.GetTempPath(), "OcrLineTool-NVIDIA-CUDA", "strips",
            Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stripPath)!);
            if (!TryCrop(imagePath, band.Top, band.Height, stripPath))
                return null;

            IReadOnlyDictionary<string, IReadOnlyList<string>> stripResults;
            try
            {
                stripResults = await client.RecognizeBatchAsync(
                    [stripPath],
                    progress: null,
                    titleRatio: 1.0,
                    detectionMaxSide: StripDetectionMaxSide,
                    useCache: false,
                    cancellationToken: cancellationToken,
                    model: PaddleOcrModels.LocalPrimary);
            }
            catch (OcrException)
            {
                return null;
            }

            if (!stripResults.TryGetValue(stripPath, out IReadOnlyList<string>? stripLines))
                return null;
            string? value = parse(stripLines);
            return value is null ? null : new SummaryRowRecoveryResult(value, stripLines);
        }
        finally
        {
            try { File.Delete(stripPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static int? MedianPitch(IReadOnlyList<int> rowCenters)
    {
        if (rowCenters.Count < 2)
            return null;
        var gaps = new List<int>();
        for (int index = 1; index < rowCenters.Count; index++)
        {
            int gap = rowCenters[index] - rowCenters[index - 1];
            if (gap is >= 10 and <= 200)
                gaps.Add(gap);
        }
        if (gaps.Count == 0)
            return null;
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    // The band is centred on the author row and never reaches the neighbouring
    // rows: its height stays below 80% of the measured row pitch.
    internal static (int Top, int Height)? ComputeBand(IReadOnlyList<int> rowCenters, int targetCenter)
    {
        if (MedianPitch(rowCenters) is not { } pitch)
            return null;
        int half = Math.Max(6, (int)Math.Round(pitch * BandHalfPitch));
        int top = Math.Max(0, targetCenter - half);
        return half * 2 >= 8 ? (top, half * 2) : null;
    }

    private static bool ContainsName(string normalizedText, string own, string label) =>
        own.Length > 0 && normalizedText.Contains(own, StringComparison.Ordinal)
        || label.Length > 0 && normalizedText.Contains(label, StringComparison.Ordinal);

    private static bool TryCrop(string imagePath, int top, int height, string destination)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = new Bitmap(stream);
            int currentTop = Math.Min(top, Math.Max(0, image.Height - 1));
            int currentHeight = Math.Min(height, image.Height - currentTop);
            if (currentHeight <= 0)
                return false;
            using var crop = image.Clone(
                new Rectangle(0, currentTop, image.Width, currentHeight), PixelFormat.Format24bppRgb);
            using var enlarged = new Bitmap(crop.Width * StripScale, crop.Height * StripScale, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(enlarged))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(crop, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
            }
            enlarged.Save(destination, ImageFormat.Png);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or ExternalException)
        {
            return false;
        }
    }
}
