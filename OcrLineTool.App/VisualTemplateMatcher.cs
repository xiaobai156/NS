using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace OcrLineTool;

public sealed record VisualTemplateBackup(string Fingerprint, double TopWidthRatio, double BottomWidthRatio);

public sealed record VisualTemplateDefinition(
    string Id,
    string[] RuleIds,
    string Fingerprint,
    double CropTopWidthRatio,
    double CropBottomWidthRatio,
    double? FingerprintTopWidthRatio = null,
    double? FingerprintBottomWidthRatio = null,
    string? SourceFolder = null,
    VisualTemplateBackup? Backup = null);

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
    double VerticalShiftWidthRatio,
    string SourceHash = "",
    bool UsedBackup = false,
    bool RequiresTitleVerification = false);

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

    [DllImport("cuda_hash.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cuda_fingerprints_batch")]
    private static extern int CudaFingerprintsBatch(
        byte[] bgr, int width, int height, double[] positions, int count, [Out] ulong[] output);

    private const int MaxCudaBatchPositions = 4096;
    private const int MaxBackupDistance = 20;
    private const string DefaultFolder = "新澳六合彩资料";
    private const double TitleTopWidthRatio = 0.1125;
    private const double TitleBottomWidthRatio = 0.2125;
    private static readonly HashSet<string> SupportedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        DefaultFolder,
        "新澳高级会员",
        // 黑色六合彩、嫣然电报、嫣然六合彩大全：各自独立的模板目录，按群名加载自己的
        // *.templates.json，只在当前所选群的目录里做指纹匹配，不与其他群共用。
        "黑色六合彩",
        "嫣然电报",
        "嫣然六合彩大全"
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

    public static string CreateFingerprint(string imagePath, double verticalShiftWidthRatio = 0,
        LocalOcrDevice device = LocalOcrDevice.Gpu) =>
        CreateFingerprint(imagePath, TitleTopWidthRatio, TitleBottomWidthRatio, verticalShiftWidthRatio, device);

    public static string CreateFingerprint(
        string imagePath,
        double fingerprintTopWidthRatio,
        double fingerprintBottomWidthRatio,
        double verticalShiftWidthRatio = 0,
        LocalOcrDevice device = LocalOcrDevice.Gpu)
    {
        FingerprintRegion region = CreateRegion(
            fingerprintTopWidthRatio, fingerprintBottomWidthRatio);
        using var image = new Bitmap(imagePath);
        ulong[] hash = CreateFingerprints(image, region, [verticalShiftWidthRatio], device)[0];
        return string.Concat(hash.Select(value => value.ToString("X16")));
    }

    public static IReadOnlyList<VisualTemplateMatch> Match(
        IReadOnlyList<string> imagePaths,
        IReadOnlyList<VisualTemplateDefinition> templates,
        int maxDistance,
        IProgress<VisualTemplateProgress>? progress = null,
        LocalOcrDevice device = LocalOcrDevice.Gpu,
        CancellationToken cancellationToken = default,
        bool batchGpuRegions = false,
        bool useBackups = false) =>
        Match(
            imagePaths,
            templates,
            maxDistance,
            template => ResolveRegion(template, null, null),
            progress, device, cancellationToken, batchGpuRegions, useBackups);

    public static IReadOnlyList<VisualTemplateMatch> Match(
        IReadOnlyList<string> imagePaths,
        VisualTemplateSet catalog,
        IProgress<VisualTemplateProgress>? progress = null,
        LocalOcrDevice device = LocalOcrDevice.Gpu,
        CancellationToken cancellationToken = default) =>
        Match(
            imagePaths,
            catalog.Templates,
            catalog.MaxDistance,
            template => ResolveRegion(
                template,
                catalog.FingerprintTopWidthRatio,
                catalog.FingerprintBottomWidthRatio),
            progress, device, cancellationToken,
            RuleCatalog.IsGroupFolder(catalog.Folder, DefaultFolder),
            RuleCatalog.IsGroupFolder(catalog.Folder, DefaultFolder));

    public static bool HasUsableMatches(
        IReadOnlyList<VisualTemplateMatch> matches,
        IReadOnlyList<VisualTemplateDefinition> templates,
        IReadOnlySet<string> requestedRuleIds)
    {
        if (matches.Count == 0 ||
            matches.Any(match => match.RequiresTitleVerification) ||
            !HasExactRuleCoverage(templates, requestedRuleIds))
            return false;

        Dictionary<string, VisualTemplateDefinition> templateById = templates
            .ToDictionary(template => template.Id, StringComparer.Ordinal);
        if (matches.GroupBy(match => match.Template.Id, StringComparer.Ordinal)
            .Any(group => group.Key != MacauTitleIndex.SingleCardRule && group.Skip(1).Any()))
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
        IProgress<VisualTemplateProgress>? progress,
        LocalOcrDevice device,
        CancellationToken cancellationToken,
        bool batchGpuRegions,
        bool useBackups)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (imagePaths.Count == 0 || templates.Count == 0)
            return [];

        ulong[][] templateHashes = templates.Select(item => ParseFingerprint(item.Fingerprint)).ToArray();
        FingerprintRegion[] templateRegions = templates.Select(regionSelector).ToArray();
        var backups = templates.Select(template => useBackups ? template.Backup : null).ToArray();
        FingerprintRegion[] regions = templateRegions.Concat(backups.OfType<VisualTemplateBackup>()
            .Select(backup => CreateRegion(backup.TopWidthRatio, backup.BottomWidthRatio))).Distinct().ToArray();
        double[]? batch = batchGpuRegions && device == LocalOcrDevice.Gpu && regions.Length > 1
            && regions.Length <= MaxCudaBatchPositions / ShiftRatios.Length
            ? BatchPositions(regions, ShiftRatios) : null;
        var imageHashes = new (string Path, string SourceHash, Dictionary<FingerprintRegion, ulong[][]> Hashes)[imagePaths.Count];
        // Duplicate downloads share one content identity so matching stays deterministic.
        int completed = 0;
        try
        {
            Parallel.For(0, imagePaths.Count, new ParallelOptions { CancellationToken = cancellationToken }, index =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = imagePaths[index];
                try
                {
                    // Derive the content identity and fingerprints from one byte read.
                    // Duplicate images are removed within each template's eligible paths below.
                    byte[] bytes = File.ReadAllBytes(path);
                    string contentHash = Convert.ToHexString(SHA256.HashData(bytes));
                    using var stream = new MemoryStream(bytes, writable: false);
                    using var image = new Bitmap(stream);
                    var hashes = new Dictionary<FingerprintRegion, ulong[][]>(regions.Length);
                    ulong[]? batched = batch is null ? null : CreateBatchGpuFingerprints(image, batch);
                    for (int region = 0; region < regions.Length; region++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        hashes.Add(regions[region], batched is null
                            ? CreateFingerprints(image, regions[region], ShiftRatios, device)
                            : SplitFingerprints(batched, region * ShiftRatios.Length, ShiftRatios.Length));
                    }
                    imageHashes[index] = (path, contentHash, hashes);
                }
                catch (Exception exception) when (exception is ArgumentException or IOException)
                {
                    imageHashes[index] = (path, string.Empty, []);
                }
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new VisualTemplateProgress(current, imagePaths.Count, path));
            });
        }
        catch (AggregateException exception) when (exception.Flatten().InnerExceptions
            .OfType<OcrException>().Any(PaddleLocalOcrClient.IsCudaUnavailable))
        {
            throw exception.Flatten().InnerExceptions.OfType<OcrException>()
                .First(PaddleLocalOcrClient.IsCudaUnavailable);
        }

        List<Score> FindPairs(int templateIndex, FingerprintRegion region, ulong[] fingerprint, int limit)
        {
            var pairs = new List<Score>();
            var seenContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int imageIndex = 0; imageIndex < imageHashes.Length; imageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // An out-of-folder duplicate must not hide the authorized source copy.
                if (templates[templateIndex].SourceFolder is { Length: > 0 } folder
                    && !RuleEngine.MatchesFolder(imageHashes[imageIndex].Path, folder))
                    continue;
                if (!imageHashes[imageIndex].Hashes.TryGetValue(region, out ulong[][]? variants))
                    continue;
                if (!seenContent.Add(imageHashes[imageIndex].SourceHash))
                    continue;

                int bestDistance = int.MaxValue;
                int bestShift = 0;
                for (int shiftIndex = 0; shiftIndex < variants.Length; shiftIndex++)
                {
                    int distance = HammingDistance(fingerprint, variants[shiftIndex]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestShift = shiftIndex;
                    }
                }
                if (bestDistance <= limit)
                    pairs.Add(new Score(templateIndex, imageIndex, bestDistance, bestShift));
            }

            return pairs.OrderBy(item => item.Distance)
                .ThenBy(item => imageHashes[item.ImageIndex].Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        const int minimumDistanceMargin = 4;
        bool ClearBest(List<Score> scores) => scores.Count > 0 &&
            (scores.Count == 1 || scores[1].Distance - scores[0].Distance >= minimumDistanceMargin);

        var usedTemplates = new HashSet<int>();
        var usedImages = new HashSet<int>();
        var usedContent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<VisualTemplateMatch>();
        List<Score> BackupMatches(int index, VisualTemplateBackup backup) => FindPairs(index,
            CreateRegion(backup.TopWidthRatio, backup.BottomWidthRatio), ParseFingerprint(backup.Fingerprint),
            Math.Min(maxDistance, MaxBackupDistance));

        var primary = Enumerable.Range(0, templates.Count).Select(index =>
            FindPairs(index, templateRegions[index], templateHashes[index], maxDistance)).ToArray();
        var alternate = Enumerable.Range(0, templates.Count).Select(index =>
            backups[index] is { } backup ? BackupMatches(index, backup) : new List<Score>()).ToArray();

        // Even an ambiguous template keeps its claim to a picture; duplicate files claim by content.
        var contestedImages = (useBackups ? primary.Concat(alternate).SelectMany(scores => scores) : [])
            .GroupBy(score => imageHashes[score.ImageIndex].SourceHash, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(score => score.TemplateIndex).Distinct().Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        void Assign(IEnumerable<Score> pairs, bool usedBackup, bool singleCardAlternatives = false)
        {
            foreach (Score score in pairs
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.TemplateIndex)
            .ThenBy(item => Math.Abs(ShiftRatios[item.ShiftIndex]))
            .ThenBy(item => imageHashes[item.ImageIndex].Path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string hash = imageHashes[score.ImageIndex].SourceHash;
                if ((!singleCardAlternatives && usedTemplates.Contains(score.TemplateIndex)) ||
                    usedImages.Contains(score.ImageIndex) || usedContent.ContainsKey(hash))
                    continue;
                usedTemplates.Add(score.TemplateIndex);
                usedImages.Add(score.ImageIndex);
                usedContent.Add(hash, score.TemplateIndex);
                List<Score> scores = usedBackup ? alternate[score.TemplateIndex] : primary[score.TemplateIndex];
                bool requiresTitleVerification = useBackups &&
                    (contestedImages.Contains(hash) || !ClearBest(scores) || scores[0] != score ||
                     usedBackup && primary[score.TemplateIndex].Count > 0 &&
                     templates[score.TemplateIndex].Id != MacauTitleIndex.SingleCardRule);
                matches.Add(new VisualTemplateMatch(
                    imageHashes[score.ImageIndex].Path,
                    templates[score.TemplateIndex],
                    score.Distance,
                    ShiftRatios[score.ShiftIndex],
                    imageHashes[score.ImageIndex].SourceHash,
                    usedBackup,
                    requiresTitleVerification));
            }
        }

        Assign(useBackups
                ? primary.SelectMany(scores => scores)
                : primary.Where(ClearBest).SelectMany(scores => scores),
            usedBackup: false);
        Assign(alternate.Where((scores, index) => scores.Count > 0 && !usedTemplates.Contains(index))
            .SelectMany(scores => scores), usedBackup: true);

        if (useBackups)
        {
            int singleCard = Enumerable.Range(0, templates.Count)
                .FirstOrDefault(index => templates[index].Id == MacauTitleIndex.SingleCardRule, -1);
            if (singleCard >= 0)
            {
                // Keep one candidate per print style, then stop after the first valid value.
                Assign(primary[singleCard].Take(1),
                    usedBackup: false, singleCardAlternatives: true);
                Assign(alternate[singleCard].Take(1),
                    usedBackup: true, singleCardAlternatives: true);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return matches.OrderBy(item => item.Template.Id, StringComparer.Ordinal).ToArray();
    }

    public static void CreateCrop(VisualTemplateMatch match, string destinationPath, bool includeRemainingRows = false, int scale = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] sourceBytes;
        try
        {
            sourceBytes = File.ReadAllBytes(match.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("模板来源图片在裁剪前不可用，请重新识别。", "OCR_IMAGE_CHANGED");
        }
        string sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
        if (!string.IsNullOrWhiteSpace(match.SourceHash)
            && !sourceHash.Equals(match.SourceHash, StringComparison.OrdinalIgnoreCase))
            throw new OcrException("模板来源图片在匹配后发生变化，请重新识别。", "OCR_IMAGE_CHANGED");

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(sourceBytes, writable: false);
        using var image = new Bitmap(stream);
        double shift = Math.Round(match.VerticalShiftWidthRatio, 2);
        int top = (int)Math.Round(image.Width * (match.Template.CropTopWidthRatio + shift));
        int bottom = includeRemainingRows ? image.Height : (int)Math.Round(image.Width *
            (match.Template.CropBottomWidthRatio + shift));
        top = Math.Clamp(top, 0, image.Height - 1);
        bottom = Math.Clamp(bottom, top + 1, image.Height);

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new OcrException("无法创建OCR裁剪目录。"));
        using Bitmap crop = image.Clone(new Rectangle(0, top, image.Width, bottom - top), PixelFormat.Format24bppRgb);
        if (scale <= 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            crop.Save(destinationPath, ImageFormat.Png);
            return;
        }

        using var enlarged = new Bitmap(crop.Width * scale, crop.Height * scale, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(enlarged))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, new Rectangle(0, 0, enlarged.Width, enlarged.Height));
        }
        cancellationToken.ThrowIfCancellationRequested();
        enlarged.Save(destinationPath, ImageFormat.Png);
    }

    internal static double[] BatchPositions(FingerprintRegion[] regions, double[] shifts)
    {
        var positions = new double[regions.Length * shifts.Length * 3];
        int index = 0;
        foreach (var region in regions)
        foreach (double shift in shifts)
        {
            positions[index++] = region.Top;
            positions[index++] = region.Bottom;
            positions[index++] = shift;
        }
        return positions;
    }

    internal static ulong[] CreateBatchGpuFingerprints(Bitmap source, double[] positions)
    {
        byte[] bgr = ReadPackedBgr(source);
        int count = positions.Length / 3;
        var output = new ulong[count * 16];
        try
        {
            RequireCudaFingerprintSuccess(CudaFingerprintsBatch(bgr, source.Width, source.Height, positions, count, output));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw CudaFingerprintLoadError();
        }
        return output;
    }

    private static ulong[][] CreateFingerprints(Bitmap source, FingerprintRegion region,
        double[] shifts, LocalOcrDevice device)
    {
        byte[] bgr = ReadPackedBgr(source);
        var output = new ulong[shifts.Length * 16];
        if (device == LocalOcrDevice.Cpu)
            CpuFingerprints(bgr, source.Width, source.Height, region, shifts, output);
        else
        {
            try
            {
                RequireCudaFingerprintSuccess(CudaFingerprints(bgr, source.Width, source.Height,
                    region.Top, region.Bottom, shifts, shifts.Length, output));
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                throw CudaFingerprintLoadError();
            }
        }
        return SplitFingerprints(output, 0, shifts.Length);
    }

    private static ulong[][] SplitFingerprints(ulong[] words, int firstPosition, int count)
    {
        var result = new ulong[count][];
        for (int index = 0; index < count; index++)
            result[index] = words.AsSpan((firstPosition + index) * 16, 16).ToArray();
        return result;
    }

    private static void RequireCudaFingerprintSuccess(int status)
    {
        if (status != 0)
            throw new OcrException($"CUDA 标题指纹计算失败（代码 {status}），请在设置中手动切换 CPU。",
                PaddleLocalOcrClient.CudaUnavailableCode);
    }

    private static OcrException CudaFingerprintLoadError() =>
        new("无法加载 CUDA 标题指纹组件，请在设置中手动切换 CPU。", PaddleLocalOcrClient.CudaUnavailableCode);

    private static byte[] ReadPackedBgr(Bitmap source)
    {
        Rectangle bounds = new(0, 0, source.Width, source.Height);
        using Bitmap packed = source.Clone(bounds, PixelFormat.Format24bppRgb);
        BitmapData data = packed.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            return CopyTopDownBgr(data, packed.Width, packed.Height);
        }
        finally
        {
            packed.UnlockBits(data);
        }
    }

    // Same separable triangular resampling and rounding as tools/CudaImage/cuda_image.cu.
    // GDI+ resizing is not equivalent and would invalidate the existing template hashes.
    private static void CpuFingerprints(byte[] bgr, int width, int height,
        FingerprintRegion region, double[] shifts, ulong[] output)
    {
        var horizontal = new float[height * 65 * 3];
        double scale = width / 65.0;
        double radius = Math.Max(1, scale);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < 65; x++)
        for (int c = 0; c < 3; c++)
        {
            double center = (x + 0.5) * scale - 0.5, sum = 0, weight = 0;
            for (int sx = (int)Math.Ceiling(center - radius); sx <= (int)Math.Floor(center + radius); sx++)
            {
                double a = Math.Max(0, 1 - Math.Abs(sx - center) / radius);
                sum += a * bgr[(y * width + Math.Clamp(sx, 0, width - 1)) * 3 + c];
                weight += a;
            }
            horizontal[(y * 65 + x) * 3 + c] = (float)(sum / weight);
        }
        var row = new byte[65 * 3];
        for (int s = 0; s < shifts.Length; s++)
        {
            int top = Math.Clamp((int)Math.Round(width * (region.Top + shifts[s])), 0, height - 1);
            int bottom = Math.Clamp((int)Math.Round(width * (region.Bottom + shifts[s])), top + 1, height);
            scale = (bottom - top) / 16.0;
            radius = Math.Max(1, scale);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 65; x++)
                for (int c = 0; c < 3; c++)
                {
                    double center = top + (y + 0.5) * scale - 0.5, sum = 0, weight = 0;
                    for (int sy = (int)Math.Ceiling(center - radius); sy <= (int)Math.Floor(center + radius); sy++)
                    {
                        double a = Math.Max(0, 1 - Math.Abs(sy - center) / radius);
                        sum += a * horizontal[(Math.Clamp(sy, 0, height - 1) * 65 + x) * 3 + c];
                        weight += a;
                    }
                    row[x * 3 + c] = (byte)Math.Clamp((int)Math.Floor(sum / weight + 0.5), 0, 255);
                }
                int GrayAt(int x) => (114 * row[3 * x] + 587 * row[3 * x + 1] + 299 * row[3 * x + 2]) / 1000;
                for (int x = 0; x < 64; x++)
                    if (GrayAt(x) <= GrayAt(x + 1)) output[s * 16 + y] |= 1UL << x;
            }
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
                    allowBothMissing: true) ||
                template.Backup is { } backup &&
                    (!RuleCatalog.IsGroupFolder(catalog.Folder, DefaultFolder) ||
                     backup.Fingerprint is not { Length: 256 } ||
                     !backup.Fingerprint.All(Uri.IsHexDigit) ||
                     !IsValidRegion(backup.TopWidthRatio, backup.BottomWidthRatio)))
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
    internal readonly record struct FingerprintRegion(double Top, double Bottom);
}
