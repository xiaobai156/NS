using System.Text.Json.Serialization;

namespace OcrLineTool;

/// <summary>Physical OCR rectangle in the actual input view.</summary>
public sealed record OcrBox(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Math.Max(0, Width));
    public int Bottom => checked(Y + Math.Max(0, Height));
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
    public IReadOnlyList<string> Lines
    {
        get
        {
            var output = new List<string>();
            string? previousRegion = null;
            foreach (OcrLineEvidence item in Items.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
            {
                string region = item.ViewId + "\u001f" + item.RegionId;
                if (previousRegion is not null && !previousRegion.Equals(region, StringComparison.Ordinal))
                    output.Add(OcrLayoutMarkers.RegionBoundary);
                output.Add(item.Text);
                previousRegion = region;
            }
            return output;
        }
    }

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

    public static OcrEvidence FromLines(string inputPath, IReadOnlyList<string> lines, string viewId = "unpositioned")
    {
        string hash;
        try { hash = LocalOcrIdentity.Image(inputPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new OcrException("无法读取 OCR 输入身份，请重新识别。", "OCR_IMAGE_IDENTITY_ERROR");
        }

        int region = 0;
        var items = new List<OcrLineEvidence>();
        foreach (string line in lines)
        {
            if (OcrLayoutMarkers.IsBoundary(line))
            {
                region++;
                continue;
            }
            if (!string.IsNullOrWhiteSpace(line))
                items.Add(new(line, null, null, viewId, $"region-{region}"));
        }
        return new(inputPath, inputPath, hash, hash, viewId, items);
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
