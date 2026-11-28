using System.Drawing;
using System.Drawing.Imaging;

namespace OcrLineTool;

internal static class TaijiNineZodiacsRecovery
{
    private const string Zodiacs = "鼠牛虎兔龙蛇马羊猴鸡狗猪";
    private static readonly (double X, double Y)[] DiscCenters =
    [
        (0.153, 0.56), (0.385, 0.56), (0.623, 0.56), (0.843, 0.56),
        (0.084, 0.85), (0.296, 0.85), (0.496, 0.85), (0.708, 0.85), (0.932, 0.85)
    ];

    internal static bool Supports(OcrRule rule) => rule.Extractor == "taiji_nine_zodiacs";

    internal static Rectangle DiscBounds(int width, int height, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, DiscCenters.Length);
        int side = (int)Math.Round(height * 0.267);
        int centerX = (int)Math.Round((width - 1) * DiscCenters[index].X);
        int centerY = (int)Math.Round((height - 1) * DiscCenters[index].Y);
        return new Rectangle(centerX - side / 2, centerY - side / 2, side, side);
    }

    internal static OcrEvidence? CombineWithSourceIssueEvidence(
        OcrEvidence sourceEvidence,
        IReadOnlyList<OcrEvidence> discEvidence,
        OcrEvidenceIdentity identity,
        int issue)
    {
        identity.EnsureCurrent();
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(sourceEvidence.SourcePath), Path.GetFullPath(identity.SourcePath))
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(sourceEvidence.InputPath), Path.GetFullPath(identity.InputPath))
            || !sourceEvidence.SourceHash.Equals(identity.SourceHash, StringComparison.OrdinalIgnoreCase)
            || !sourceEvidence.InputHash.Equals(identity.InputHash, StringComparison.OrdinalIgnoreCase)
            || discEvidence.Count != DiscCenters.Length
            || discEvidence.Any(evidence =>
                !StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetFullPath(evidence.SourcePath), Path.GetFullPath(identity.SourcePath))
                || !evidence.SourceHash.Equals(identity.SourceHash, StringComparison.OrdinalIgnoreCase)))
            return null;

        OcrLineEvidence[] issueItems = sourceEvidence.Items
            .Where(item => RuleEngine.FindIssues([item.Text]).Contains(issue))
            .Select(item => item with { ViewId = identity.ViewId + "/source-issue" })
            .ToArray();
        if (issueItems.Length == 0)
            return null;

        var zodiacItems = new List<OcrLineEvidence>(DiscCenters.Length);
        for (int index = 0; index < discEvidence.Count; index++)
        {
            string[] values = discEvidence[index].Items
                .SelectMany(item => item.Text.Where(Zodiacs.Contains))
                .Select(character => character.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (values.Length != 1)
                return null;
            zodiacItems.Add(new OcrLineEvidence(values[0], ViewId: identity.ViewId + "/taiji-disc-"
                + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        identity.EnsureCurrent();
        return new OcrEvidence(identity.SourcePath, identity.InputPath, identity.SourceHash,
            identity.InputHash, identity.ViewId, issueItems.Concat(zodiacItems).ToArray())
        {
            Reader = discEvidence[0].Reader
        };
    }

    internal static async Task<IReadOnlyList<(OcrRule Rule, OcrEvidence Evidence)>> ReadAsync(
        PaddleLocalOcrClient client,
        IReadOnlyList<(string Path, OcrRule Rule, OcrEvidenceIdentity Identity, OcrEvidence SourceEvidence)> requests,
        int issue,
        string inputFolder,
        CancellationToken cancellationToken,
        IProgress<LocalOcrProgress>? progress = null)
    {
        requests = requests
            .Where(request => Supports(request.Rule)
                && RuleEngine.MatchesRuleFolder(request.Path, request.Rule)
                && File.Exists(request.Path)
                && StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetFullPath(request.SourceEvidence.SourcePath), Path.GetFullPath(request.Identity.SourcePath))
                && StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetFullPath(request.SourceEvidence.InputPath), Path.GetFullPath(request.Identity.InputPath))
                && request.SourceEvidence.SourceHash.Equals(request.Identity.SourceHash, StringComparison.OrdinalIgnoreCase)
                && request.SourceEvidence.InputHash.Equals(request.Identity.InputHash, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(request => (request.Path, request.Rule.Id), RequestKeyComparer.Instance)
            .ToArray();
        if (requests.Count == 0)
            return [];

        Directory.CreateDirectory(inputFolder);
        var crops = new Dictionary<(string Path, string RuleId),
            IReadOnlyList<(string CropPath, OcrEvidenceIdentity Identity)>>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Identity.EnsureCurrent();
            var key = (request.Path, request.Rule.Id);
            if (crops.ContainsKey(key))
                continue;

            using var bitmap = new Bitmap(request.Path);
            var discCrops = new List<(string CropPath, OcrEvidenceIdentity Identity)>(DiscCenters.Length);
            for (int index = 0; index < DiscCenters.Length; index++)
            {
                string cropPath = Path.Combine(inputFolder, Guid.NewGuid().ToString("N") + ".png");
                Rectangle bounds = DiscBounds(bitmap.Width, bitmap.Height, index);
                using var padded = new Bitmap(bounds.Width, bounds.Height);
                using (var graphics = Graphics.FromImage(padded))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(bitmap,
                        new Rectangle(0, 0, bounds.Width, bounds.Height),
                        bounds,
                        GraphicsUnit.Pixel);
                }
                using var enlarged = new Bitmap(padded.Width * 2, padded.Height * 2);
                using (var graphics = Graphics.FromImage(enlarged))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(padded, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
                }
                enlarged.Save(cropPath, ImageFormat.Png);
                request.Identity.EnsureCurrent();
                discCrops.Add((cropPath, new(request.Path, cropPath, request.Identity.SourceHash,
                    LocalOcrIdentity.Image(cropPath), "taiji-disc-" + index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))));
            }
            crops.Add(key, discCrops);
        }

        await client.RecognizeBatchAsync(crops.Values.SelectMany(items => items)
                .Select(item => item.CropPath).ToArray(),
            progress, titleRatio: 1.0, detectionMaxSide: 1440, useCache: false,
            model: PaddleOcrModels.LocalPrimary, cancellationToken: cancellationToken);

        var output = new List<(OcrRule, OcrEvidence)>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discEvidence = new List<OcrEvidence>(DiscCenters.Length);
            foreach (var crop in crops[(request.Path, request.Rule.Id)])
            {
                crop.Identity.EnsureCurrent();
                if (!client.LastEvidence.TryGetValue(crop.CropPath, out OcrEvidence? evidence))
                {
                    discEvidence.Clear();
                    break;
                }
                discEvidence.Add(evidence.Bind(crop.Identity));
            }
            if (CombineWithSourceIssueEvidence(
                    request.SourceEvidence, discEvidence, request.Identity, issue) is { } combined)
                output.Add((request.Rule, combined));
        }
        return output;
    }

    private sealed class RequestKeyComparer : IEqualityComparer<(string Path, string RuleId)>
    {
        internal static readonly RequestKeyComparer Instance = new();

        public bool Equals((string Path, string RuleId) left, (string Path, string RuleId) right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left.Path, right.Path)
            && StringComparer.Ordinal.Equals(left.RuleId, right.RuleId);

        public int GetHashCode((string Path, string RuleId) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Path),
                StringComparer.Ordinal.GetHashCode(value.RuleId));
    }
}
