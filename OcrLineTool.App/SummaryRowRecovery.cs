using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OcrLineTool;

internal enum SummaryRowRecoveryKind { Summary, IssueZodiac, IssueNumbers, RightBlock }

internal sealed record SummaryRowRecoveryRequest(string ImagePath, OcrRule Rule, SummaryRowRecoveryKind Kind)
{
    internal string ViewId => Kind switch
    {
        SummaryRowRecoveryKind.Summary => "summary-row-strip",
        SummaryRowRecoveryKind.RightBlock => "right-block-strip",
        _ => "issue-row-strip"
    };
}

internal sealed record SummaryRowRecoveryResult(string Value, IReadOnlyList<string> StripLines, string SourceHash);

/// <summary>
/// 给补读进度加上阶段前缀（定位/裁剪重读），便于界面区分；同步转发（不用 Progress&lt;T&gt; 的异步投递），
/// 保证同一批回调的顺序与产生顺序一致。
/// </summary>
internal sealed class PrefixedProgress : IProgress<LocalOcrProgress>
{
    private readonly IProgress<LocalOcrProgress> inner;
    private readonly string prefix;

    internal PrefixedProgress(IProgress<LocalOcrProgress> inner, string prefix)
    {
        this.inner = inner;
        this.prefix = prefix;
    }

    public void Report(LocalOcrProgress value) => inner.Report(value with { Stage = prefix + value.Stage });
}

/// <summary>Batch row recovery without combining authors, periods or image versions.</summary>
internal static class SummaryRowRecovery
{
    private const int StripDetectionMaxSide = 1440;
    private const int StripScale = 2;
    private const double BandHalfPitch = 0.4;
    private static readonly HashSet<string> RightBlockRuleIds = new(StringComparer.Ordinal) { "简单爱" };

    internal static bool SupportsRightBlockRecovery(OcrRule rule) => RightBlockRuleIds.Contains(rule.Id);

    internal static async Task<IReadOnlyDictionary<SummaryRowRecoveryRequest, SummaryRowRecoveryResult>> TryRecoverBatchAsync(
        PaddleLocalOcrClient client,
        IReadOnlyList<SummaryRowRecoveryRequest> requests,
        IReadOnlyList<OcrRule> catalog,
        int issue,
        double titleRatio,
        int? detectionMaxSide,
        CancellationToken cancellationToken,
        IProgress<LocalOcrProgress>? progress = null)
    {
        var results = await RecoverOnceAsync(client, requests, catalog, issue, titleRatio, detectionMaxSide, cancellationToken,
            progress: progress);
        if (detectionMaxSide is not null)
        {
            // 定位读带本组的 det-max（嫣然心水 960 = 0.55 横向压缩视图）；密集表
            // 在该视图里可能连目标期号锚点都读不到（杰少杀一肖），于是补读连行都
            // 裁不出来。对仍未解决的请求用原图坐标系（标准 medium 检测）再定位一次，
            // 行条取值仍由标准 medium（1440）产出，不换取值来源。
            var remaining = requests.Where(request => !results.ContainsKey(request)).ToArray();
            if (remaining.Length > 0)
                foreach (var result in await RecoverOnceAsync(client, remaining, catalog, issue, titleRatio, null, cancellationToken,
                    progress: progress, locatePrefix: "定位（原图坐标）·"))
                    results[result.Key] = result.Value;
        }
        var compactRequests = requests.Where(request => !results.ContainsKey(request)
            && request.Kind == SummaryRowRecoveryKind.IssueZodiac
            && request.Rule.Id == "杰少杀一肖" && request.Rule.Folder == "杰少").ToArray();
        if (compactRequests.Length > 0)
            foreach (var result in await RecoverOnceAsync(client, compactRequests, catalog, issue,
                1.0, 960, cancellationToken, PaddleOcrModel.Small, progress, "定位（小模型）·"))
                results[result.Key] = result.Value;
        return results;
    }

    private static async Task<Dictionary<SummaryRowRecoveryRequest, SummaryRowRecoveryResult>> RecoverOnceAsync(
        PaddleLocalOcrClient client,
        IReadOnlyList<SummaryRowRecoveryRequest> requests,
        IReadOnlyList<OcrRule> catalog,
        int issue,
        double titleRatio,
        int? detectionMaxSide,
        CancellationToken cancellationToken,
        PaddleOcrModel locatorModel = PaddleOcrModels.LocalPrimary,
        IProgress<LocalOcrProgress>? progress = null,
        string locatePrefix = "定位·")
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new Dictionary<SummaryRowRecoveryRequest, SummaryRowRecoveryResult>();
        string[] sources = requests.Select(request => request.ImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
        if (sources.Length == 0)
            return results;
        var located = await ReadBatchAsync(client, sources, titleRatio, detectionMaxSide, true, cancellationToken, locatorModel,
            progress is null ? null : new PrefixedProgress(progress, locatePrefix));
        var pending = new List<(SummaryRowRecoveryRequest Request, string Path, OcrEvidenceIdentity Source,
            Func<IReadOnlyList<string>, string?> Parse)>();
        string folder = Path.Combine(Path.GetTempPath(), "OcrLineTool-NVIDIA-CUDA", "strips", Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() =>
            {
                foreach (SummaryRowRecoveryRequest request in requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!located.TryGetValue(request.ImagePath, out var original))
                        continue;
                    if (locatorModel == PaddleOcrModel.Small
                        && !RuleEngine.FindMatches(request.ImagePath, original.Lines, [request.Rule], catalog)
                            .Any(rule => rule.Id == request.Rule.Id))
                        continue;
                    var source = new OcrEvidenceIdentity(request.ImagePath, request.ImagePath,
                        original.Evidence.SourceHash, original.Evidence.SourceHash, request.ViewId);
                    try
                    {
                        source.EnsureCurrent();
                        (int X, int Y, int Width, int Height)? rect;
                        Func<IReadOnlyList<string>, string?> parse;
                        if (request.Kind == SummaryRowRecoveryKind.Summary)
                        {
                            var band = ComputeSummaryBand(original.Evidence.Items, request.Rule);
                            rect = band is null ? null : (0, band.Value.Top, 0, band.Value.Height);
                            parse = lines => RuleEngine.ExtractSummaryRowZodiacFromStrip(lines, request.Rule, catalog);
                        }
                        else if (request.Kind == SummaryRowRecoveryKind.RightBlock)
                        {
                            var items = original.Evidence.TokenItems is { Count: > 0 } tokens ? tokens : original.Evidence.Items;
                            rect = ComputeRightBlockRect(items, issue);
                            parse = lines => RuleEngine.ExtractRightBlockZodiacFromStrip(lines, issue);
                        }
                        else
                        {
                            // Compact preprocessing changes only X. Its Y coordinates still belong
                            // to the original image; never mix its rows with the separate header view.
                            var items = locatorModel == PaddleOcrModel.Small
                                ? original.Evidence.Items.Where(item => item.ViewId == "paddle/compact").ToArray()
                                : original.Evidence.Items;
                            var band = ComputeIssueRowBand(items, issue);
                            rect = band is null ? null : (0, band.Value.Top, 0, band.Value.Height);
                            parse = request.Kind == SummaryRowRecoveryKind.IssueNumbers
                                ? lines => RuleEngine.ExtractFinalValue(lines, issue, request.Rule)
                                : lines => RuleEngine.ExtractIssueRowZodiacFromStrip(lines, issue);
                            if (request.Rule.Id == "杰少杀一肖" && request.Rule.Folder == "杰少")
                                // This dense table prints the opening zodiac on the same row.
                                // The rule parser separates that column; a second row is not safe.
                                parse = lines => lines.Count(line => !string.IsNullOrWhiteSpace(line)) == 1
                                    ? RuleEngine.ExtractFinalValue(lines, issue, request.Rule) : null;
                        }
                        if (rect is not { } bounds)
                            continue;
                        Directory.CreateDirectory(folder);
                        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
                        if (!TryCropRect(request.ImagePath, bounds.X, bounds.Y, bounds.Width, bounds.Height, path))
                            continue;
                        source.EnsureCurrent();
                        pending.Add((request, path, source, parse));
                    }
                    catch (Exception exception) when (IsRecoverableError(exception)) { }
                }
            }, cancellationToken);

            if (pending.Count == 0)
                return results;
            var strips = await ReadBatchAsync(client, pending.Select(item => item.Path).ToArray(),
                1.0, StripDetectionMaxSide, false, cancellationToken,
                progress: progress is null ? null : new PrefixedProgress(progress, "裁剪重读·"));
            foreach (var item in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    item.Source.EnsureCurrent();
                    if (strips.TryGetValue(item.Path, out var strip) && item.Parse(strip.Lines) is { } value)
                        results[item.Request] = new SummaryRowRecoveryResult(value, strip.Lines, item.Source.SourceHash);
                }
                catch (Exception exception) when (IsRecoverableError(exception)) { }
            }
            return results;
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<Dictionary<string, (IReadOnlyList<string> Lines, OcrEvidence Evidence)>> ReadBatchAsync(
        PaddleLocalOcrClient client, IReadOnlyList<string> paths, double titleRatio, int? detectionMaxSide,
        bool useCache, CancellationToken cancellationToken,
        PaddleOcrModel model = PaddleOcrModels.LocalPrimary,
        IProgress<LocalOcrProgress>? progress = null)
    {
        try
        {
            var texts = await client.RecognizeBatchAsync(paths, progress, titleRatio: titleRatio,
                detectionMaxSide: detectionMaxSide, useCache: useCache,
                cancellationToken: cancellationToken, model: model);
            return texts.Where(item => client.LastEvidence.ContainsKey(item.Key)).ToDictionary(
                item => item.Key, item => (item.Value, client.LastEvidence[item.Key]), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (IsRecoverableError(exception))
        {
            // A bad image must not discard its batch peers. Device failures and cancellation remain fatal.
            var results = new Dictionary<string, (IReadOnlyList<string>, OcrEvidence)>(StringComparer.OrdinalIgnoreCase);
            if (paths.Count > 1)
                foreach (string path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var item in await ReadBatchAsync(client, [path], titleRatio, detectionMaxSide, useCache, cancellationToken, model, progress))
                        results[item.Key] = item.Value;
                }
            return results;
        }
    }

    private static bool IsRecoverableError(Exception exception) => exception is OcrException ocr
        ? !PaddleLocalOcrClient.IsCudaUnavailable(ocr)
        : exception is IOException or UnauthorizedAccessException;

    internal static (int Top, int Height)? ComputeSummaryBand(IReadOnlyList<OcrLineEvidence> items, OcrRule rule)
    {
        string own = RuleCatalog.NormalizeGroupName(rule.Keyword);
        string label = string.IsNullOrWhiteSpace(rule.Label) ? string.Empty : RuleCatalog.NormalizeGroupName(rule.Label);
        // Circled 正 marks are optional OCR observations, not author identity.
        // Locate the unique horizontal name and measure aligned author rows;
        // a merged vertical zodiac column must never influence the row pitch.
        OcrLineEvidence[] ownRows = items.Where(item => item.Box is { Height: > 0 } box
            && box.Width > box.Height
            && ContainsName(RuleCatalog.NormalizeGroupName(item.Text), own, label)).ToArray();
        if (ownRows.Length != 1)
            return null;
        OcrLineEvidence anchor = ownRows[0];
        OcrBox ownBox = anchor.Box!;
        int targetCenter = (int)Math.Round(ownBox.CenterY);
        int[] centers = items.Where(item => item.Box is { Height: > 0 } box
            && item.ViewId == anchor.ViewId && box.Width > box.Height
            && box.Height >= ownBox.Height / 2d && box.Height <= ownBox.Height * 2d
            && Math.Abs((long)box.X - ownBox.X) <= ownBox.Height
            && item.Text.Count(char.IsLetter) >= 2)
            .Select(item => (int)Math.Round(item.Box!.CenterY)).Distinct().Order().ToArray();
        if (MedianPitch(centers) is not { } pitch)
            return null;
        int nearestGap = centers.Where(center => center != targetCenter)
            .Select(center => Math.Abs(center - targetCenter)).Min();
        int half = (int)Math.Floor(Math.Min(pitch, nearestGap) * BandHalfPitch);
        return half >= 4 ? (Math.Max(0, targetCenter - half), half * 2) : null;
    }

    internal static (int X, int Y, int Width, int Height)? ComputeRightBlockRect(IReadOnlyList<OcrLineEvidence> items, int issue)
    {
        OcrLineEvidence[] targets = items.Where(item => item.Box is not null && RuleEngine.LineContainsIssue(item.Text, issue)).ToArray();
        if (targets.Length != 1)
            return null;
        OcrBox box = targets[0].Box!;
        int top = Math.Max(0, box.Y - 2);
        int x = Math.Max(0, box.X - 4);
        return (x, top, 0, Math.Max(24, box.Height) + 4);
    }

    // Period rows are the anchors; values may be below the period but never reach the next period.
    internal static (int Top, int Height)? ComputeIssueRowBand(IReadOnlyList<OcrLineEvidence> items, int issue)
    {
        OcrLineEvidence[] issueRows = items
            .Where(item => item.Box is not null && item.Text.Contains('期', StringComparison.Ordinal))
            .OrderBy(item => item.Box!.Y).ToArray();
        if (issueRows.Length < 2)
            return null;
        OcrLineEvidence[] targets = issueRows.Where(item => RuleEngine.LineContainsIssue(item.Text, issue)).ToArray();
        if (targets.Length != 1)
            return null;
        OcrLineEvidence target = targets[0];
        OcrLineEvidence? next = issueRows.Where(item => item.Box!.Y > target.Box!.Y).OrderBy(item => item.Box!.Y).FirstOrDefault();
        int[] centers = issueRows.Select(item => (int)Math.Round(item.Box!.CenterY)).ToArray();
        if (MedianPitch(centers) is not { } pitch)
            return null;
        int top = Math.Max(0, target.Box!.Y - 4);
        int bottom = next is not null ? next.Box!.Y - 4 : target.Box.Bottom + (int)Math.Round(pitch * 0.8);
        return bottom - top < 12 ? null : (top, bottom - top);
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

    // The author band stays below 80% of the measured row pitch.
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

    // width <= 0 means crop to the right image edge.
    private static bool TryCropRect(string imagePath, int x, int y, int width, int height, string destination)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = new Bitmap(stream);
            int left = Math.Clamp(x, 0, Math.Max(0, image.Width - 1));
            int top = Math.Clamp(y, 0, Math.Max(0, image.Height - 1));
            int cropWidth = width <= 0 ? image.Width - left : Math.Min(width, image.Width - left);
            int cropHeight = Math.Min(height, image.Height - top);
            if (cropWidth <= 0 || cropHeight <= 0)
                return false;
            using var crop = image.Clone(new Rectangle(left, top, cropWidth, cropHeight), PixelFormat.Format24bppRgb);
            using var enlarged = new Bitmap(crop.Width * StripScale, crop.Height * StripScale, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(enlarged))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(crop, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
            }
            enlarged.Save(destination, ImageFormat.Png);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        {
            return false;
        }
    }
}
