from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)

path = Path('OcrLineTool.App/VisualTemplateMatcher.cs')
text = path.read_text(encoding='utf-8')

text = replace_once(text,
'''using System.Runtime.InteropServices;
using System.Text.Json;''',
'''using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;''',
'crypto using')

text = replace_once(text,
'''public sealed record VisualTemplateMatch(
    string SourcePath,
    VisualTemplateDefinition Template,
    int Distance,
    double VerticalShiftWidthRatio);''',
'''public sealed record VisualTemplateMatch(
    string SourcePath,
    VisualTemplateDefinition Template,
    int Distance,
    double VerticalShiftWidthRatio,
    string SourceHash = "");''',
'template match source hash')

text = replace_once(text,
'''        var imageHashes = new (string Path, Dictionary<FingerprintRegion, ulong[][]> Hashes)[imagePaths.Count];
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
            }''',
'''        var imageHashes = new (string Path, string SourceHash, Dictionary<FingerprintRegion, ulong[][]> Hashes)[imagePaths.Count];
        int completed = 0;
        Parallel.For(0, imagePaths.Count, index =>
        {
            string path = imagePaths[index];
            try
            {
                // Fingerprint and source identity must be derived from the exact
                // same bytes. Otherwise a same-path replacement between matching
                // and cropping could bind template A's location to image B.
                byte[] bytes = File.ReadAllBytes(path);
                string sourceHash = Convert.ToHexString(SHA256.HashData(bytes));
                using var stream = new MemoryStream(bytes, writable: false);
                using var image = new Bitmap(stream);
                imageHashes[index] = (path, sourceHash, regions.ToDictionary(
                    region => region,
                    region => CreateCudaFingerprints(image, region)));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                imageHashes[index] = (path, string.Empty, []);
            }''',
'match same bytes identity')

text = replace_once(text,
'''            matches.Add(new VisualTemplateMatch(
                imageHashes[score.ImageIndex].Path,
                templates[score.TemplateIndex],
                score.Distance,
                ShiftRatios[score.ShiftIndex]));''',
'''            matches.Add(new VisualTemplateMatch(
                imageHashes[score.ImageIndex].Path,
                templates[score.TemplateIndex],
                score.Distance,
                ShiftRatios[score.ShiftIndex],
                imageHashes[score.ImageIndex].SourceHash));''',
'match carries source hash')

text = replace_once(text,
'''    public static void CreateCrop(VisualTemplateMatch match, string destinationPath, bool includeRemainingRows = false, int scale = 1)
    {
        using var image = new Bitmap(match.SourcePath);
        double shift = Math.Round(match.VerticalShiftWidthRatio, 2);''',
'''    public static void CreateCrop(VisualTemplateMatch match, string destinationPath, bool includeRemainingRows = false, int scale = 1)
    {
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

        using var stream = new MemoryStream(sourceBytes, writable: false);
        using var image = new Bitmap(stream);
        double shift = Math.Round(match.VerticalShiftWidthRatio, 2);''',
'template crop source verification')

path.write_text(text, encoding='utf-8')
print('Applied template source-version pin')
