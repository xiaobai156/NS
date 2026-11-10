namespace OcrLineTool;

public sealed record StatisticsCheck(int Version, string Reader, string SourceHash, string InputHash,
    IReadOnlyList<OcrLineEvidence> Items);

internal static class StatisticsVerification
{
    internal const int Version = 1;

    internal static OcrEvidence Attach(OcrEvidence primary, OcrEvidence independent) => primary with
    {
        StatisticsCheck = new(Version, independent.Reader, independent.SourceHash, independent.InputHash,
            independent.TokenItems ?? independent.Items)
    };

    internal static async Task<IReadOnlyList<OcrEvidence>> VerifyBatchAsync(
        IReadOnlyList<OcrEvidence> primary, IReadOnlyList<OcrRule> rules, int issue,
        PaddleLocalOcrClient client, CancellationToken cancellationToken)
    {
        var pending = primary.Where(e => rules.Any(r => RuleEngine.NeedsStatisticsVerification(e, issue, r))).ToArray();
        if (pending.Length == 0) return primary;
        var identities = pending.Select(e => new OcrEvidenceIdentity(e.SourcePath, e.InputPath,
            e.SourceHash, e.InputHash, "statistics-check")).ToArray();
        foreach (var identity in identities) identity.EnsureCurrent();
        try
        {
            await client.RecognizeBatchAsync(pending.Select(e => e.InputPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                titleRatio: 1, model: PaddleOcrModel.Small, cancellationToken: cancellationToken);
        }
        catch (OcrException exception) when (!PaddleLocalOcrClient.IsCudaUnavailable(exception)
            && exception.Code != "OCR_IMAGE_CHANGED")
        {
            return primary;
        }
        foreach (var identity in identities) identity.EnsureCurrent();
        return primary.Select(e => pending.Contains(e) && client.LastEvidence.TryGetValue(e.InputPath, out var check)
            ? Attach(e, check.Bind(new(e.SourcePath, e.InputPath, e.SourceHash, e.InputHash, "statistics-check"))) : e).ToArray();
    }
}
