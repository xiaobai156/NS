using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OcrLineTool;

public sealed record VisualTemplateDefinition(
    string Id,
    string[] RuleIds,
    string Fingerprint,
    double CropTopWidthRatio,
    double CropBottomWidthRatio,
    double? FingerprintTopWidthRatio = null,
    double? FingerprintBottomWidthRatio = null);

public sealed record VisualTemplateSet(
    int Version,
    string Folder,
    int MaxDistance,
    List<VisualTemplateDefinition> Templates,
    double? FingerprintTopWidthRatio = null,
    double? FingerprintBottomWidthRatio = null);

public sealed record VisualTemplateMatch(
    string SourcePath,
    VisualTemplateDefinition Template,
    int Distance,
    double VerticalShiftWidthRatio);

public sealed record VisualTemplateProgress(int Completed, int Total, string Path);

public static class VisualTemplateMatcher
{
    [DllImport("cuda_hash.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cuda_fingerprints")]
    private static extern int CudaFingerprints(
        byte[] bgr,
        int width,
        int height,
        double top,
        double bottom,
        double[] shifts,
        int shiftCount,
        [Out] ulong[] output);

    private const string DefaultFolder = "新澳六合彩资料";
    private const double TitleTopWidthRatio = 0.1125;
    private const double TitleBottomWidthRatio = 0.2125;
    private static readonly HashSet<string> SupportedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        DefaultFolder,
        "新澳高级会员"
    };
    // Keep the proven five-position scan: -2%, -1%, 0%, 1%, 2% of image width.
    private static readonly double[] ShiftRatios = [-0.02, -0.01, 0, 0.01, 0.02];

    public static bool Supports(string selectedFolder) =>
        TryMatchSupportedFolder(selectedFolder) is not null;

    public static string ConfigPath(string appDirectory) =>
        Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), DefaultFolder + ".templates.json");

    public static string ConfigPath(string appDirectory, string selectedFolder)
    {
        string configurationDirectory = ResultFilePaths.ConfigurationDirectory(appDirectory);
        string? supportedFolder = TryMatchSupportedFolder(selectedFolder);
        if (supportedFolder is not null)
            return Path.Combine(configurationDirectory, supportedFolder + ".templates.json");

        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedFolder));
        if (Directory.Exists(configurationDirectory))
        {
            string? matchingTemplate = Directory.EnumerateFiles(configurationDirectory, "*.templates.json")
                .Select(path => new
                {
                    Path = path,
                    Name = Path.GetFileName(path)[..^".templates.json".Length]
                })
                .Where(item => RuleCatalog.IsGroupFolder(selectedFolder, item.Name))
                .OrderByDescending(item => RuleCatalog.NormalizeGroupName(item.Name).Length)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Path)
                .FirstOrDefault();
            if (matchingTemplate is not null)
                return matchingTemplate;
        }

        return Path.Combine(configurationDirectory, folderName + ".templates.json");
    }

    private static string? TryMatchSupportedFolder(string selectedFolder)
    {
        return SupportedFolders
            .Where(folder => RuleCatalog.IsGroupFolder(selectedFolder, folder))
            .OrderByDescending(folder => RuleCatalog.NormalizeGroupName(folder).Length)
            .FirstOrDefault();
    }

    public static VisualTemplateSet Load(string path)
    {
        if (!File.Exists(path))
            throw new OcrException($"未找到 {Path.GetFileName(path)}，无法使用标题模板匹配。");

        try
        {
            VisualTemplateSet catalog = JsonSerializer.Deserialize<VisualTemplateSet>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new OcrException($"{Path.GetFileName(path)} 格式无效。");
            Validate(catalog, Path.GetFileName(path));
            return catalog;
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new OcrException($"{Path.GetFileName(path)} 格式无效。");
        }
    }

    public static string CreateFingerprint(string imagePath, double verticalShiftWidthRatio = 0)
    {
        using var image = new Bitmap(imagePath);
        return CreateCudaFingerprint(image, TitleTopWidthRatio, TitleBottomWidthRatio, verticalShiftWidthRatio);
    }

    public static string CreateFingerprint(
        string imagePath,
        double fingerprintTopWidthRatio,
        double fingerprintBottomWidthRatio,
        double verticalShiftWidthRatio = 0)
    {
        FingerprintRegion region = CreateRegion(
            fingerprintTopWidthRatio, fingerprintBottomWidthRatio);
        using var image = new Bitmap(imagePath);
        return CreateCudaFingerprint(image, region.Top, region.Bottom, verticalShiftWidthRatio);
    }

    public static IReadOnlyList<VisualTemplateMatch> Match(
        IReadOnlyList<string> imagePaths,
        IReadOnlyList<VisualTemplateDefinition> templates,
        int maxDistance,
        IProgress<VisualTemplateProgress>? progress = null) =>
        Match(
            imagePaths,
            templates,
            maxDistance,
            template => ResolveRegion(template, null, null),
            progress);

    public static IReadOnlyList<VisualTemplateMatch> Match(
        IReadOnlyList<string> imagePaths,
        VisualTemplateSet catalog,
        IProgress<VisualTemplateProgress>? progress = null) =>
        Match(
            imagePaths,
            catalog.Templates,
            catalog.MaxDistance,
            template => ResolveRegion(
                template,
                catalog.FingerprintTopWidthRatio,
                catalog.FingerprintBottomWidthRatio),
            progress);

    public static bool HasUsableMatches(
        IReadOnlyList<VisualTemplateMatch> matches,
        IReadOnlyList<VisualTemplateDefinition> templates,
        IReadOnlySet<string> requestedRuleIds)
    {
        if (matches.Count == 0 || !HasExactRuleCoverage(templates, requestedRuleIds))
            return false;

        Dictionary<string, VisualTemplateDefinition> templateById = templates
            .ToDictionary(template => template.Id, StringComparer.Ordinal);
        if (matches.Select(match => match.Template.Id).Distinct(StringComparer.Ordinal).Count() != matches.Count)
            return false;
        if (matches.GroupBy(match => match.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Select(match => match.Template.Id).Distinct(StringComparer.Ordinal).Count() > 1))
            return false;

        var coveredRuleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualTemplateMatch match in matches)
        {
            if (!templateById.TryGetValue(match.Template.Id, out VisualTemplateDefinition? configured) ||
                !configured.RuleIds.SequenceEqual(match.Template.RuleIds, StringComparer.Ordinal) ||
                match.Template.RuleIds.Any(ruleId => !requestedRuleIds.Contains(ruleId)))
                return false;

            coveredRuleIds.UnionWith(match.Template.RuleIds);
        }

        return coveredRuleIds.SetEquals(requestedRuleIds);
    }

    public static bool HasExactRuleCoverage(
        IReadOnlyList<VisualTemplateDefinition> templates,
        IReadOnlySet<string> expectedRuleIds)
    {
        if (templates.Count == 0 || expectedRuleIds.Count == 0 ||
            expectedRuleIds.Any(string.IsNullOrWhiteSpace) ||
            templates.Select(template => template.Id)
                .Distinct(StringComparer.Ordinal).Count() != templates.Count)
            return false;

        string[] configuredRuleIds = templates
            .SelectMany(template => template.RuleIds)
            .ToArray();
        return configuredRuleIds.Length > 0 &&
            !configuredRuleIds.Any(string.IsNullOrWhiteSpace) &&
            configuredRuleIds.Distinct(StringComparer.Ordinal).Count() == configuredRuleIds.Length &&
            configuredRuleIds.ToHashSet(StringComparer.Ordinal).SetEquals(expectedRuleIds);
    }

    /// <summary>
    /// Returns the templates relevant to a retry rule subset. Shared templates are
    /// cloned with only the requested rule IDs so callers cannot accidentally send
    /// already-successful rules again. An empty result means the subset is not
    /// completely represented by a non-overlapping template set.
    /// </summary>
    public static IReadOnlyList<VisualTemplateDefinition> SelectForRules(
        IReadOnlyList<VisualTemplateDefinition> templates,
        IReadOnlySet<string> requestedRuleIds)
    {
        if (templates.Count == 0 || requestedRuleIds.Count == 0 ||
            requestedRuleIds.Any(string.IsNullOrWhiteSpace))
            return [];

        if (templates.Select(template => template.Id)
                .Distinct(StringComparer.Ordinal).Count() != templates.Count)
            return [];

        var selected = new List<VisualTemplateDefinition>();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualTemplateDefinition template in templates)
        {
            string[] relevantRuleIds = template.RuleIds
                .Where(requestedRuleIds.Contains)
                .ToArray();
            if (relevantRuleIds.Length == 0)
                continue;

            if (relevantRuleIds.Any(ruleId => !covered.Add(ruleId)))
                return [];

            selected.Add(template with { RuleIds = relevantRuleIds });
        }

        return covered.SetEquals(requestedRuleIds) ? selected : [];
    }

    private static IReadOnlyList<VisualTemplateMatch> Match(
        IReadOnlyList<string> imagePaths,
        IReadOnlyList<VisualTemplateDefinition> templates,
        int maxDistance,
        Func<VisualTemplateDefinition, FingerprintRegion> regionSelector,
        IProgress<VisualTemplateProgress>? progress)
    {
        if (imagePaths.Count == 0 || templates.Count == 0)
            return [];

        ulong[][] templateHashes = templates.Select(item => ParseFingerprint(item.Fingerprint)).ToArray();
        FingerprintRegion[] templateRegions = templates.Select(regionSelector).ToArray();
        FingerprintRegion[] regions = templateRegions.Distinct().ToArray();
        var imageHashes = new (string Path, Dictionary<FingerprintRegion, ulong[][]> Hashes)[imagePaths.Count];
        int completed = 0;
        Parallel.For(0, imagePaths.Count, index =>
        {
            string path = imagePaths[index];
            try
            {
                using var image = new Bitmap(path);
                imageHashes[index] = (path, regions.ToDictionary(
                    region => region,
                    region => CreateCudaFingerprints(image, region)));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                imageHashes[index] = (path, []);
            }
            int current = Interlocked.Increment(ref completed);
            progress?.Report(new VisualTemplateProgress(current, imagePaths.Count, path));
        });

        var pairs = new List<Score>(templates.Count * imagePaths.Count);
        for (int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
        {
            for (int imageIndex = 0; imageIndex < imageHashes.Length; imageIndex++)
            {
                if (!imageHashes[imageIndex].Hashes.TryGetValue(
                    templateRegions[templateIndex], out ulong[][]? variants))
                    continue;

                int bestDistance = int.MaxValue;
                int bestShift = 0;
                for (int shiftIndex = 0; shiftIndex < variants.Length; shiftIndex++)
                {
                    int distance = HammingDistance(templateHashes[templateIndex], variants[shiftIndex]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestShift = shiftIndex;
                    }
                }
                if (bestDistance <= maxDistance)
                    pairs.Add(new Score(templateIndex, imageIndex, bestDistance, bestShift));
            }
        }

        // Do not force a deterministic choice when several images are equally close.
        // An ambiguous template must fall back to OCR instead of selecting a wrong image.
        const int minimumDistanceMargin = 4;
        pairs = pairs
            .GroupBy(item => item.TemplateIndex)
            .SelectMany(group =>
            {
                Score[] ordered = group.OrderBy(item => item.Distance)
                    .ThenBy(item => imageHashes[item.ImageIndex].Path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (ordered.Length > 1 &&
                    ordered[1].Distance - ordered[0].Distance < minimumDistanceMargin)
                    return [];
                return ordered;
            })
            .ToList();

        var usedTemplates = new HashSet<int>();
        var usedImages = new HashSet<int>();
        var matches = new List<VisualTemplateMatch>();
        foreach (Score score in pairs
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.TemplateIndex)
            .ThenBy(item => Math.Abs(ShiftRatios[item.ShiftIndex]))
            .ThenBy(item => imageHashes[item.ImageIndex].Path, StringComparer.OrdinalIgnoreCase))
        {
            if (usedTemplates.Contains(score.TemplateIndex) || usedImages.Contains(score.ImageIndex))
                continue;
            usedTemplates.Add(score.TemplateIndex);
            usedImages.Add(score.ImageIndex);
            matches.Add(new VisualTemplateMatch(
                imageHashes[score.ImageIndex].Path,
                templates[score.TemplateIndex],
                score.Distance,
                ShiftRatios[score.ShiftIndex]));
        }
        return matches.OrderBy(item => item.Template.Id, StringComparer.Ordinal).ToArray();
    }

    public static void CreateCrop(VisualTemplateMatch match, string destinationPath, bool includeRemainingRows = false, int scale = 1)
    {
        using var image = new Bitmap(match.SourcePath);
        double shift = Math.Round(match.VerticalShiftWidthRatio, 2);
        int top = (int)Math.Round(image.Width * (match.Template.CropTopWidthRatio + shift));
        int bottom = includeRemainingRows ? image.Height : (int)Math.Round(image.Width *
            (match.Template.CropBottomWidthRatio + shift));
        top = Math.Clamp(top, 0, image.Height - 1);
        bottom = Math.Clamp(bottom, top + 1, image.Height);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new OcrException("无法创建OCR裁剪目录。"));
        using Bitmap crop = image.Clone(new Rectangle(0, top, image.Width, bottom - top), PixelFormat.Format24bppRgb);
        if (scale <= 1)
        {
            crop.Save(destinationPath, ImageFormat.Png);
            return;
        }

        using var enlarged = new Bitmap(crop.Width * scale, crop.Height * scale, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(enlarged))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
        }
        enlarged.Save(destinationPath, ImageFormat.Png);
    }

    private static string CreateFingerprint(
        Bitmap image,
        double fingerprintTopWidthRatio,
        double fingerprintBottomWidthRatio,
        double verticalShiftWidthRatio)
    {
        int top = (int)Math.Round(image.Width * (fingerprintTopWidthRatio + verticalShiftWidthRatio));
        int bottom = (int)Math.Round(image.Width * (fingerprintBottomWidthRatio + verticalShiftWidthRatio));
        top = Math.Clamp(top, 0, image.Height - 1);
        bottom = Math.Clamp(bottom, top + 1, image.Height);

        using var reduced = new Bitmap(65, 16, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(reduced))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                image,
                new Rectangle(0, 0, reduced.Width, reduced.Height),
                new Rectangle(0, top, image.Width, bottom - top),
                GraphicsUnit.Pixel);
        }

        Span<ulong> hash = stackalloc ulong[16];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                if (Gray(reduced.GetPixel(x, y)) <= Gray(reduced.GetPixel(x + 1, y)))
                    hash[(y * 64 + x) / 64] |= 1UL << x;
            }
        }
        return string.Concat(hash.ToArray().Select(value => value.ToString("X16")));
    }

    private static int Gray(Color color) =>
        (color.R * 299 + color.G * 587 + color.B * 114) / 1000;

    private static ulong[][] CreateCudaFingerprints(Bitmap source, FingerprintRegion region)
    {
        using Bitmap packed = source.Clone(
            new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format24bppRgb);
        Rectangle bounds = new(0, 0, packed.Width, packed.Height);
        BitmapData data = packed.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            byte[] bgr = CopyTopDownBgr(data, packed.Width, packed.Height);
            var output = new ulong[ShiftRatios.Length * 16];
            int status = CudaFingerprints(
                bgr, packed.Width, packed.Height, region.Top, region.Bottom,
                ShiftRatios, ShiftRatios.Length, output);
            if (status != 0)
                throw new OcrException($"CUDA 标题指纹计算失败（代码 {status}）。");
            return Enumerable.Range(0, ShiftRatios.Length)
                .Select(shift => output.Skip(shift * 16).Take(16).ToArray())
                .ToArray();
        }
        finally
        {
            packed.UnlockBits(data);
        }
    }

    private static string CreateCudaFingerprint(
        Bitmap source, double top, double bottom, double shift)
    {
        using Bitmap packed = source.Clone(
            new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format24bppRgb);
        BitmapData data = packed.LockBits(
            new Rectangle(0, 0, packed.Width, packed.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            byte[] bgr = CopyTopDownBgr(data, packed.Width, packed.Height);
            var shifts = new[] { shift };
            var output = new ulong[16];
            int status = CudaFingerprints(
                bgr, packed.Width, packed.Height, top, bottom, shifts, 1, output);
            if (status != 0)
                throw new OcrException($"CUDA 标题指纹计算失败（代码 {status}）。");
            return string.Concat(output.Select(value => value.ToString("X16")));
        }
        finally
        {
            packed.UnlockBits(data);
        }
    }

    private static byte[] CopyTopDownBgr(BitmapData data, int width, int height)
    {
        int rowBytes = width * 3;
        int stride = Math.Abs(data.Stride);
        bool bottomUp = data.Stride < 0;
        byte[] source = new byte[stride * height];
        Marshal.Copy(data.Scan0, source, 0, source.Length);
        byte[] output = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
        {
            int destinationRow = bottomUp ? height - y - 1 : y;
            Buffer.BlockCopy(source, y * stride, output, destinationRow * rowBytes, rowBytes);
        }
        return output;
    }

    private static ulong[] ParseFingerprint(string fingerprint)
    {
        if (fingerprint.Length != 256)
            throw new OcrException("标题模板指纹长度无效。");
        var output = new ulong[16];
        for (int index = 0; index < output.Length; index++)
        {
            if (!ulong.TryParse(
                fingerprint.AsSpan(index * 16, 16),
                System.Globalization.NumberStyles.HexNumber,
                null,
                out output[index]))
                throw new OcrException("标题模板指纹格式无效。");
        }
        return output;
    }

    private static int HammingDistance(ulong[] left, ulong[] right)
    {
        int distance = 0;
        for (int index = 0; index < left.Length; index++)
            distance += BitOperations.PopCount(left[index] ^ right[index]);
        return distance;
    }

    private static void Validate(VisualTemplateSet catalog, string fileName)
    {
        if (catalog.Version != 1 ||
            string.IsNullOrWhiteSpace(catalog.Folder) ||
            !Supports(catalog.Folder) ||
            catalog.MaxDistance is <= 0 or > 1024 ||
            catalog.Templates is null ||
            catalog.Templates.Count == 0 ||
            !IsValidRegionPair(
                catalog.FingerprintTopWidthRatio,
                catalog.FingerprintBottomWidthRatio,
                allowBothMissing: true) ||
            catalog.Templates.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Templates.Count)
            throw new OcrException($"{fileName} 配置无效。");

        foreach (VisualTemplateDefinition template in catalog.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Id) ||
                template.RuleIds is null ||
                template.RuleIds.Length == 0 ||
                template.RuleIds.Any(string.IsNullOrWhiteSpace) ||
                template.Fingerprint is null ||
                template.Fingerprint.Length != 256 ||
                !IsValidRegion(template.CropTopWidthRatio, template.CropBottomWidthRatio) ||
                !IsValidRegionPair(
                    template.FingerprintTopWidthRatio,
                    template.FingerprintBottomWidthRatio,
                    allowBothMissing: true))
                throw new OcrException($"{fileName} 中的模板 {template.Id} 无效。");
        }
    }

    private static FingerprintRegion ResolveRegion(
        VisualTemplateDefinition template,
        double? catalogTop,
        double? catalogBottom)
    {
        double top = template.FingerprintTopWidthRatio ?? catalogTop ?? TitleTopWidthRatio;
        double bottom = template.FingerprintBottomWidthRatio ?? catalogBottom ?? TitleBottomWidthRatio;
        return CreateRegion(top, bottom);
    }

    private static FingerprintRegion CreateRegion(double top, double bottom)
    {
        if (!IsValidRegion(top, bottom))
            throw new OcrException("标题模板指纹区域无效。");
        return new FingerprintRegion(top, bottom);
    }

    private static bool IsValidRegionPair(double? top, double? bottom, bool allowBothMissing) =>
        top is null && bottom is null
            ? allowBothMissing
            : top is not null && bottom is not null && IsValidRegion(top.Value, bottom.Value);

    private static bool IsValidRegion(double top, double bottom) =>
        double.IsFinite(top) && double.IsFinite(bottom) && top >= 0 && bottom > top;

    private sealed record Score(int TemplateIndex, int ImageIndex, int Distance, int ShiftIndex);
    private readonly record struct FingerprintRegion(double Top, double Bottom);
}
