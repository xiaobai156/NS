using System.Security.Cryptography;

namespace OcrLineTool;

/// <summary>Reuses cloud text only when the actual original image bytes are identical.</summary>
public sealed class CloudImageDeduplicator
{
    private readonly Dictionary<string, IReadOnlyList<string>> responses =
        new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<string>> RecognizeAsync(
        string groupDirectory,
        string originalImagePath,
        IReadOnlyList<OcrRule> rules,
        Func<Task<IReadOnlyList<string>>> request)
    {
        if (!ShouldDeduplicate(groupDirectory, rules))
            return await request();

        string? fingerprint = TryFingerprint(originalImagePath);
        if (fingerprint is null)
            return await request();
        if (responses.TryGetValue(fingerprint, out IReadOnlyList<string>? cached))
            return cached;

        IReadOnlyList<string> lines = await request();
        responses[fingerprint] = lines;
        return lines;
    }

    private static bool ShouldDeduplicate(string groupDirectory, IReadOnlyList<OcrRule> rules) =>
        (RuleCatalog.IsGroupFolder(groupDirectory, "黄大仙新澳") ||
         RuleCatalog.IsGroupFolder(groupDirectory, "嫣然心水")) &&
        !rules.Any(rule => rule.Id == "杰少杀一尾");

    private static string? TryFingerprint(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
