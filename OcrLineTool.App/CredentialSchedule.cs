namespace OcrLineTool;

public enum OcrProvider
{
    Tencent,
    Baidu
}

public sealed record OcrCredential(
    OcrProvider Provider,
    string Slot,
    string Id,
    string Secret)
{
    public string DisplayName => $"{(Provider == OcrProvider.Tencent ? "腾讯云" : "百度云")} {Slot}";
}

public static class CredentialSchedule
{
    private sealed record CredentialSlot(OcrProvider Provider, string Slot);

    private static readonly CredentialSlot[] Credentials =
    [
        new(OcrProvider.Tencent, "A"),
        new(OcrProvider.Tencent, "B"),
        new(OcrProvider.Tencent, "C"),
        new(OcrProvider.Baidu, "A"),
        new(OcrProvider.Baidu, "B"),
        new(OcrProvider.Baidu, "C"),
        new(OcrProvider.Baidu, "D"),
        new(OcrProvider.Tencent, "D")
    ];

    // Do not permanently cache a failed configuration load. Reload at request boundaries.
    private static OcrSecrets ProductionSecrets => OcrSecretsLoader.Load(validateAllConfigured: false);

    private static readonly AsyncLocal<OcrSecrets?> TestSecrets = new();

    private static readonly DateOnly Anchor = new(2026, 8, 29);
    private const int AnchorIssue = 241;
    private static readonly TimeZoneInfo Beijing = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    public static DateOnly TodayInBeijing() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beijing));

    public static OcrCredential Today() => ForDate(TodayInBeijing());

    public static OcrCredential DescribeSlot(int slot)
    {
        if ((uint)slot >= Credentials.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        CredentialSlot selected = Credentials[slot];
        return new(selected.Provider, selected.Slot, string.Empty, string.Empty);
    }

    public static OcrCredential DescribeDate(DateOnly date)
    {
        int slot = (date.DayNumber - Anchor.DayNumber) % Credentials.Length;
        return DescribeSlot(slot < 0 ? slot + Credentials.Length : slot);
    }

    public static OcrCredential Resolve(OcrCredential descriptor)
    {
        int slot = Array.FindIndex(Credentials, item => item.Provider == descriptor.Provider && item.Slot == descriptor.Slot);
        return ForSlot(slot);
    }

    public static OcrCredential DescribeFallback(OcrCredential credential)
    {
        int slot = Array.FindIndex(Credentials, item => item.Provider != credential.Provider && item.Slot == credential.Slot);
        if (slot < 0) slot = Array.FindIndex(Credentials, item => item.Provider != credential.Provider);
        return DescribeSlot(slot);
    }


    public static int IssueForDate(DateOnly date) => AnchorIssue + date.DayNumber - Anchor.DayNumber;

    public static OcrCredential ForSlot(int slot)
    {
        if ((uint)slot >= Credentials.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));

        CredentialSlot selected = Credentials[slot];
        OcrSecret secret = (TestSecrets.Value ?? ProductionSecrets).Get(selected.Provider, selected.Slot);
        return new(selected.Provider, selected.Slot, secret.Id, secret.Secret);
    }

    public static OcrCredential FallbackFor(OcrCredential credential)
    {
        int index = Array.FindIndex(Credentials, item =>
            item.Provider != credential.Provider && item.Slot == credential.Slot);
        if (index < 0)
            index = Array.FindIndex(Credentials, item => item.Provider != credential.Provider);
        return ForSlot(index);
    }

    public static IReadOnlyList<OcrCredential> RotationFrom(OcrCredential credential)
    {
        int start = Array.FindIndex(Credentials, item =>
            item.Provider == credential.Provider && item.Slot == credential.Slot);
        if (start < 0)
            start = 0;

        var available = new List<OcrCredential>();
        OcrSecrets secrets;
        try { secrets = TestSecrets.Value ?? ProductionSecrets; }
        catch (OcrException) { return available; }
        for (int offset = 0; offset < Credentials.Length; offset++)
        {
            CredentialSlot selected = Credentials[(start + offset) % Credentials.Length];
            try
            {
                OcrSecret secret = secrets.Get(selected.Provider, selected.Slot);
                available.Add(new(selected.Provider, selected.Slot, secret.Id, secret.Secret));
            }
            catch (OcrException) { /* An unconfigured slot is not an unavailable application. */ }
        }
        return available;
    }

    public static OcrCredential ForDate(DateOnly date)
    {
        int index = (date.DayNumber - Anchor.DayNumber) % Credentials.Length;
        if (index < 0)
            index += Credentials.Length;
        return ForSlot(index);
    }

    internal static IDisposable UseSecretsForTests(OcrSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        OcrSecrets? previous = TestSecrets.Value;
        TestSecrets.Value = secrets;
        return new SecretsScope(previous);
    }

    private sealed class SecretsScope(OcrSecrets? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            TestSecrets.Value = previous;
            disposed = true;
        }
    }
}
