using System.Drawing;
using System.Drawing.Imaging;

namespace OcrLineTool;

internal static class RuiErTeamRecovery
{
    internal static bool Supports(OcrRule rule) => rule.Extractor == "rui_er_team_card";

    // Re-read every owned area, including successes: another source may contradict the first.
    // Keep each crop and its source identity for cache replay.
    internal static async Task<IReadOnlyList<(OcrRule Rule, OcrEvidence Evidence)>> ReadAsync(
        PaddleLocalOcrClient client, IReadOnlyList<(string Path, OcrRule Rule)> requests,
        string inputFolder, CancellationToken cancellationToken, IProgress<LocalOcrProgress>? progress = null)
    {
        var output = new List<(OcrRule, OcrEvidence)>();
        requests = requests.Where(r => Supports(r.Rule) && RuleEngine.MatchesRuleFolder(r.Path, r.Rule))
            .Distinct().ToArray();
        if (requests.Count == 0) return output;
        string[] paths = requests.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await client.RecognizeBatchAsync(paths, progress, titleRatio: 1.0, detectionMaxSide: null,
            model: PaddleOcrModels.LocalPrimary, cancellationToken: cancellationToken);
        var original = client.LastEvidence.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var pending = new List<(OcrRule Rule, string Path, OcrEvidenceIdentity Identity)>();
        var crops = new Dictionary<(string Path, OcrBox Bounds, string Author), string>();
        Directory.CreateDirectory(inputFolder);
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!original.TryGetValue(request.Path, out var evidence)) continue;
            foreach (var area in RuleEngine.RuiErAreas(evidence, request.Rule))
            {
                if (area.Summary && request.Rule.Type != "生肖") continue;
                var summary = area.Summary ? RuleEngine.RuiErSummaryCrop(area, request.Rule) : null;
                if (area.Summary && summary is null) continue;
                var source = new OcrEvidenceIdentity(request.Path, request.Path,
                    evidence.SourceHash, evidence.SourceHash, "rui-er-owned-area");
                source.EnsureCurrent();
                var key = (request.Path, area.Bounds, area.Summary ? request.Rule.Keyword : "");
                if (!crops.TryGetValue(key, out string? cropPath))
                {
                    cropPath = Path.Combine(inputFolder, Guid.NewGuid().ToString("N") + ".png");
                    using var bitmap = new Bitmap(request.Path);
                    OcrBox b = area.Bounds;
                    var rectangle = Rectangle.Intersect(new Rectangle(b.X, b.Y, b.Width, b.Height),
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;
                    using var crop = summary is { } summaryBoxes
                        ? SummaryView(bitmap, summaryBoxes.Header, summaryBoxes.Row)
                        : bitmap.Clone(rectangle, PixelFormat.Format24bppRgb);
                    using var enlarged = new Bitmap(crop.Width * 2, crop.Height * 2);
                    using (var graphics = Graphics.FromImage(enlarged))
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(crop, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
                    }
                    enlarged.Save(cropPath, ImageFormat.Png);
                    source.EnsureCurrent();
                    crops.Add(key, cropPath);
                }
                pending.Add((request.Rule, cropPath, new(request.Path, cropPath,
                    evidence.SourceHash, LocalOcrIdentity.Image(cropPath), "rui-er-owned-area")));
            }
        }
        if (pending.Count == 0) return output;
        await client.RecognizeBatchAsync(crops.Values.ToArray(), progress, titleRatio: 1.0,
            detectionMaxSide: 1440, useCache: false, model: PaddleOcrModels.LocalPrimary,
            cancellationToken: cancellationToken);
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.Identity.EnsureCurrent();
            if (client.LastEvidence.TryGetValue(item.Path, out var evidence))
                output.Add((item.Rule, evidence.Bind(item.Identity)));
        }
        return output;
    }

    private static Bitmap SummaryView(Bitmap source, OcrBox header, OcrBox row)
    {
        // Both strips come from the same verified 新澳 table. OCR rechecks the printed period
        // and author on this distinct input view; no locator text is substituted into its result.
        var view = new Bitmap(row.Width, header.Height + row.Height + 20);
        using var graphics = Graphics.FromImage(view);
        graphics.Clear(Color.White);
        graphics.DrawImage(source, new Rectangle(0, 0, header.Width, header.Height),
            new Rectangle(header.X, header.Y, header.Width, header.Height), GraphicsUnit.Pixel);
        graphics.DrawImage(source, new Rectangle(0, header.Height + 20, row.Width, row.Height),
            new Rectangle(row.X, row.Y, row.Width, row.Height), GraphicsUnit.Pixel);
        return view;
    }
}
