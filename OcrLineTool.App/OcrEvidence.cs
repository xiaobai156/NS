using System.Text.Json.Serialization;

namespace OcrLineTool;

/// <summary>Physical OCR rectangle in the actual input view.</summary>
public sealed record OcrBox(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Math.Max(0, Width));
    public int Bottom => checked(Y + Math.Max(0, Height));
    public double CenterX => X + Width / 2d;
    public double CenterY => Y + Height / 2d;
}

/// <summary>
/// One OCR text observation. ViewId prevents tokens from independently rendered
/// views (original/crop/header/compact) being treated as one continuous field;
/// RegionId prevents separated columns/regions from being concatenated.
/// </summary>
public sealed record OcrLineEvidence(
    string Text,
    OcrBox? Box = null,
    double? Confidence = null,
    string ViewId = "default",
    string RegionId = "main");

/// <summary>Stable identity captured before an OCR request/inference starts.</summary>
public sealed record OcrEvidenceIdentity(
    string SourcePath,
    string InputPath,
    string SourceHash,
    string InputHash,
    string ViewId)
{
    public static OcrEvidenceIdentity Capture(string sourcePath, string inputPath, string viewId)
    {
        try
        {
            return new(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(inputPath),
                LocalOcrIdentity.Image(sourcePath),
                LocalOcrIdentity.Image(inputPath),
                string.IsNullOrWhiteSpace(viewId) ? "default" : viewId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new OcrException("无法固定 OCR 图片版本，请重新识别。", "OCR_IMAGE_IDENTITY_ERROR");
        }
    }

    public void EnsureCurrent()
    {
        try
        {
            if (LocalOcrIdentity.Image(SourcePath) != SourceHash ||
                LocalOcrIdentity.Image(InputPath) != InputHash)
                throw new OcrException("图片或识别视图在处理期间发生变化，请重新识别。", "OCR_IMAGE_CHANGED");
        }
        catch (OcrException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new OcrException("图片或识别视图在处理期间不可用，请重新识别。", "OCR_IMAGE_CHANGED");
        }
    }
}

/// <summary>
/// OCR evidence that reaches the business extractor without discarding source
/// version, rendered view, region, optional coordinates, or confidence.
/// </summary>
public sealed record OcrEvidence(
    string SourcePath,
    string InputPath,
    string SourceHash,
    string InputHash,
    string ViewId,
    IReadOnlyList<OcrLineEvidence> Items)
{
    [JsonIgnore]
    public IReadOnlyList<string> Lines => SafeLines(Items);

    [JsonIgnore]
    public bool HasCompleteGeometry =>
        Items.Where(item => !string.IsNullOrWhiteSpace(item.Text)).All(item => item.Box is not null);

    [JsonIgnore]
    public double? MinimumConfidence
    {
        get
        {
            double[] confidences = Items
                .Where(item => item.Confidence is not null)
                .Select(item => item.Confidence!.Value)
                .ToArray();
            return confidences.Length == 0 ? null : confidences.Min();
        }
    }

    public static IReadOnlyList<string> SafeLines(IReadOnlyList<OcrLineEvidence> items)
    {
        var output = new List<string>();
        string? previousRegion = null;
        foreach (OcrLineEvidence item in items.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            string region = item.ViewId + "\u001f" + item.RegionId;
            if (previousRegion is not null && !previousRegion.Equals(region, StringComparison.Ordinal))
                output.Add(OcrLayoutMarkers.RegionBoundary);
            output.Add(item.Text);
            previousRegion = region;
        }
        return output;
    }

    public static OcrEvidence FromLines(string inputPath, IReadOnlyList<string> lines, string viewId = "unpositioned")
    {
        string hash;
        try { hash = LocalOcrIdentity.Image(inputPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new OcrException("无法读取 OCR 输入身份，请重新识别。", "OCR_IMAGE_IDENTITY_ERROR");
        }

        var items = new List<OcrLineEvidence>();
        int explicitRegion = 0;
        foreach (string line in lines)
        {
            if (OcrLayoutMarkers.IsBoundary(line))
            {
                explicitRegion++;
                continue;
            }
            if (!string.IsNullOrWhiteSpace(line))
                items.Add(new(line, null, null, viewId, $"input-{explicitRegion}"));
        }
        // With no coordinates, line adjacency is not physical proof. Partition
        // deliberately gives each opaque line its own region; existing explicit
        // boundaries remain at least as strict as before.
        IReadOnlyList<OcrLineEvidence> partitioned = OcrEvidenceLayout.Partition(items);
        return new(inputPath, inputPath, hash, hash, viewId, partitioned);
    }

    public static OcrEvidence FromCapturedBytes(
        string inputPath,
        byte[] inputBytes,
        string viewId,
        IReadOnlyList<OcrLineEvidence> items)
    {
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(inputBytes));
        return new(inputPath, inputPath, hash, hash, viewId, items);
    }

    public OcrEvidence Bind(OcrEvidenceIdentity identity)
    {
        identity.EnsureCurrent();
        if (!string.IsNullOrWhiteSpace(InputHash) &&
            !InputHash.Equals(identity.InputHash, StringComparison.OrdinalIgnoreCase))
            throw new OcrException("OCR 响应与当前识别视图不是同一图片版本。", "OCR_IMAGE_CHANGED");

        OcrLineEvidence[] boundItems = Items.Select(item => item with
        {
            ViewId = identity.ViewId + "/" + (string.IsNullOrWhiteSpace(item.ViewId) ? "default" : item.ViewId)
        }).ToArray();
        return new(
            identity.SourcePath,
            identity.InputPath,
            identity.SourceHash,
            identity.InputHash,
            identity.ViewId,
            boundItems);
    }
}

/// <summary>
/// Turns positioned OCR tokens/lines into physical reading regions. A normal
/// single-column page remains one region. If a row proves that the page has
/// separated horizontal columns, output becomes column-major and a region
/// boundary is inserted between columns, preventing row-order cross-column joins.
/// </summary>
internal static class OcrEvidenceLayout
{
    internal static IReadOnlyList<OcrLineEvidence> Partition(IReadOnlyList<OcrLineEvidence> source)
    {
        var output = new List<OcrLineEvidence>();
        foreach (IGrouping<string, OcrLineEvidence> view in source
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId, StringComparer.Ordinal))
        {
            OcrLineEvidence[] items = view.ToArray();

            // A caller/template may already know physical regions. Never erase
            // that stronger identity by repartitioning everything as "main".
            string[] declaredRegions = items
                .Select(item => item.RegionId)
                .Where(region => !string.IsNullOrWhiteSpace(region) && region != "main"
                    && !region.StartsWith("unpositioned-", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (declaredRegions.Length > 0)
            {
                foreach (IGrouping<string, OcrLineEvidence> region in items
                    .GroupBy(item => item.RegionId, StringComparer.Ordinal))
                {
                    output.AddRange(region.OrderBy(item => item.Box?.CenterY ?? double.MaxValue)
                        .ThenBy(item => item.Box?.X ?? int.MaxValue));
                }
                continue;
            }

            if (items.Any(item => item.Box is null))
            {
                // Unknown geometry remains one opaque region. The business
                // extractor separately requires holistic + atomic agreement.
                output.AddRange(items.Select(item => item with { RegionId = "unpositioned" }));
                continue;
            }

            List<List<OcrLineEvidence>> rows = BuildRows(items);
            RowSegment[] segments = rows.SelectMany(SplitRow).ToArray();
            List<List<RowSegment>> columns = BuildHorizontalColumns(segments);
            if (columns.Count <= 1)
            {
                output.AddRange(segments.OrderBy(segment => segment.Box.CenterY)
                    .ThenBy(segment => segment.Box.X)
                    .Select(segment => segment.ToEvidence(view.Key, "main")));
                continue;
            }

            for (int column = 0; column < columns.Count; column++)
            {
                foreach (RowSegment segment in columns[column]
                    .OrderBy(segment => segment.Box.CenterY)
                    .ThenBy(segment => segment.Box.X))
                {
                    output.Add(segment.ToEvidence(view.Key, $"column-{column}"));
                }
            }
        }
        return output;
    }

    private static List<List<RowSegment>> BuildHorizontalColumns(IEnumerable<RowSegment> source)
    {
        RowSegment[] segments = source.OrderBy(item => item.Box.CenterX).ToArray();
        if (segments.Length == 0)
            return [];

        int pageLeft = segments.Min(item => item.Box.X);
        int pageRight = segments.Max(item => item.Box.Right);
        int pageWidth = Math.Max(1, pageRight - pageLeft);

        static int HorizontalGap(RowSegment segment, IReadOnlyList<RowSegment> column)
        {
            int left = column.Min(item => item.Box.X);
            int right = column.Max(item => item.Box.Right);
            return segment.Box.Right < left ? left - segment.Box.Right
                : segment.Box.X > right ? segment.Box.X - right
                : 0;
        }

        static int MergeThreshold(RowSegment segment, IReadOnlyList<RowSegment> column)
        {
            int averageHeight = (int)Math.Round(column.Average(item => Math.Max(1, item.Box.Height)));
            return Math.Max(48, Math.Max(averageHeight, Math.Max(1, segment.Box.Height)) * 4);
        }

        static List<List<RowSegment>> Cluster(IEnumerable<RowSegment> candidates)
        {
            var columns = new List<List<RowSegment>>();
            foreach (RowSegment segment in candidates.OrderBy(item => item.Box.CenterX))
            {
                int chosen = -1;
                int bestGap = int.MaxValue;
                for (int index = 0; index < columns.Count; index++)
                {
                    int gap = HorizontalGap(segment, columns[index]);
                    int threshold = MergeThreshold(segment, columns[index]);
                    if (gap <= threshold && gap < bestGap)
                    {
                        chosen = index;
                        bestGap = gap;
                    }
                }
                if (chosen < 0)
                    columns.Add([segment]);
                else
                    columns[chosen].Add(segment);
            }
            return columns;
        }

        // Establish body columns before considering any wide title/header. A
        // collection of banners must not be allowed to enlarge the body
        // envelope or merge otherwise independent columns. The envelope is
        // measured without near-full-width banners, so a 650-wide block on a
        // 1000-wide page is still a banner, not body.
        RowSegment[] nonBanners = segments
            .Where(segment => segment.Box.Width < pageWidth * 0.9)
            .ToArray();
        int bodyLeft = nonBanners.Select(item => item.Box.X).DefaultIfEmpty(pageLeft).Min();
        int bodyRight = nonBanners.Select(item => item.Box.Right).DefaultIfEmpty(pageRight).Max();
        int bodyWidth = Math.Max(1, bodyRight - bodyLeft);
        RowSegment[] bodySeeds = segments.Length >= 3
            ? segments.Where(segment => segment.Box.Width < bodyWidth * 0.70).ToArray()
            : segments;
        List<List<RowSegment>> bodyColumns = Cluster(bodySeeds);

        var spanning = new HashSet<RowSegment>();
        foreach (RowSegment candidate in segments.Except(bodySeeds))
        {
            int touched = bodyColumns.Count(column =>
                HorizontalGap(candidate, column) <= MergeThreshold(candidate, column));
            if (touched >= 2)
                spanning.Add(candidate);
        }

        List<List<RowSegment>> columns = Cluster(segments.Where(segment => !spanning.Contains(segment)));
        foreach (RowSegment banner in spanning)
            columns.Add([banner]);
        return columns.OrderBy(column => column.Average(item => item.Box.CenterX)).ToList();
    }

    private static List<List<OcrLineEvidence>> BuildRows(IEnumerable<OcrLineEvidence> items)
    {
        var rows = new List<List<OcrLineEvidence>>();
        foreach (OcrLineEvidence item in items.OrderBy(item => item.Box!.CenterY))
        {
            List<OcrLineEvidence>? row = rows.FirstOrDefault(candidate =>
            {
                double center = candidate.Average(value => value.Box!.CenterY);
                double height = candidate.Average(value => Math.Max(1, value.Box!.Height));
                return Math.Abs(center - item.Box!.CenterY) <= Math.Max(3, Math.Min(height, Math.Max(1, item.Box.Height)) * 0.55);
            });
            if (row is null) rows.Add([item]);
            else row.Add(item);
        }
        return rows;
    }

    private static List<RowSegment> SplitRow(List<OcrLineEvidence> row)
    {
        OcrLineEvidence[] ordered = row.OrderBy(item => item.Box!.X).ToArray();
        var segments = new List<RowSegment>();
        var current = new List<OcrLineEvidence>();
        int previousRight = 0;
        foreach (OcrLineEvidence item in ordered)
        {
            if (current.Count > 0)
            {
                OcrLineEvidence previous = current[^1];
                int gap = item.Box!.X - previousRight;
                int splitGap = Math.Max(48, Math.Max(previous.Box!.Height, item.Box.Height) * 4);
                if (gap > splitGap)
                {
                    segments.Add(RowSegment.From(current));
                    current.Clear();
                }
            }
            current.Add(item);
            previousRight = Math.Max(previousRight, item.Box!.Right);
        }
        if (current.Count > 0) segments.Add(RowSegment.From(current));
        return segments;
    }

    private sealed record RowSegment(string Text, OcrBox Box, double? Confidence)
    {
        internal static RowSegment From(IReadOnlyList<OcrLineEvidence> items)
        {
            int left = items.Min(item => item.Box!.X);
            int top = items.Min(item => item.Box!.Y);
            int right = items.Max(item => item.Box!.Right);
            int bottom = items.Max(item => item.Box!.Bottom);
            double[] scores = items.Where(item => item.Confidence is not null)
                .Select(item => item.Confidence!.Value).ToArray();
            return new(
                string.Concat(items.OrderBy(item => item.Box!.X).Select(item => item.Text)),
                new(left, top, right - left, bottom - top),
                scores.Length == 0 ? null : scores.Min());
        }

        internal OcrLineEvidence ToEvidence(string viewId, string regionId) =>
            new(Text, Box, Confidence, viewId, regionId);
    }
}
