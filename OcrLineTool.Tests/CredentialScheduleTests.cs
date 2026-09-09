using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class CredentialScheduleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"ocr-schedule-{Guid.NewGuid():N}");
    private readonly IDisposable secretsScope;

    public CredentialScheduleTests()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "secrets.json");
        File.WriteAllText(path, TestJson);
        secretsScope = CredentialSchedule.UseSecretsForTests(OcrSecretsLoader.Load(path));
    }

    [Theory]
    [InlineData(2026, 8, 29, 241)]
    [InlineData(2026, 8, 30, 242)]
    [InlineData(2026, 8, 31, 243)]
    [InlineData(2026, 9, 1, 244)]
    public void CalculatesIssueFromBeijingCalendarDate(int year, int month, int day, int expected)
    {
        Assert.Equal(expected, CredentialSchedule.IssueForDate(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(2026, 8, 29, OcrProvider.Tencent, "A")]
    [InlineData(2026, 8, 30, OcrProvider.Tencent, "B")]
    [InlineData(2026, 8, 31, OcrProvider.Tencent, "C")]
    [InlineData(2026, 9, 1, OcrProvider.Baidu, "A")]
    [InlineData(2026, 9, 2, OcrProvider.Baidu, "B")]
    [InlineData(2026, 9, 3, OcrProvider.Baidu, "C")]
    [InlineData(2026, 9, 4, OcrProvider.Baidu, "D")]
    [InlineData(2026, 9, 5, OcrProvider.Tencent, "D")]
    [InlineData(2026, 9, 6, OcrProvider.Tencent, "A")]
    public void SelectsOneCredentialPerBeijingCalendarDay(
        int year, int month, int day, OcrProvider provider, string slot)
    {
        OcrCredential actual = CredentialSchedule.ForDate(new DateOnly(year, month, day));

        Assert.Equal(provider, actual.Provider);
        Assert.Equal(slot, actual.Slot);
    }

    [Fact]
    public void DateBeforeAnchorWrapsBackward()
    {
        OcrCredential actual = CredentialSchedule.ForDate(new DateOnly(2026, 8, 28));

        Assert.Equal(OcrProvider.Tencent, actual.Provider);
        Assert.Equal("D", actual.Slot);
    }

    [Fact]
    public void TodayUsesTheBeijingCalendarDateAndReadableProviderName()
    {
        DateOnly beijingToday = CredentialSchedule.TodayInBeijing();
        OcrCredential actual = CredentialSchedule.Today();

        Assert.Equal(CredentialSchedule.ForDate(beijingToday), actual);
        Assert.Matches("^(腾讯云|百度云) [A-D]$", actual.DisplayName);
        Assert.Equal("腾讯云 A", CredentialSchedule.ForDate(new DateOnly(2026, 8, 29)).DisplayName);
        Assert.Equal("百度云 A", CredentialSchedule.ForDate(new DateOnly(2026, 9, 1)).DisplayName);
    }

    [Theory]
    [InlineData(0, OcrProvider.Tencent, "A")]
    [InlineData(1, OcrProvider.Tencent, "B")]
    [InlineData(2, OcrProvider.Tencent, "C")]
    [InlineData(3, OcrProvider.Baidu, "A")]
    [InlineData(4, OcrProvider.Baidu, "B")]
    [InlineData(5, OcrProvider.Baidu, "C")]
    [InlineData(6, OcrProvider.Baidu, "D")]
    [InlineData(7, OcrProvider.Tencent, "D")]
    public void SupportsTemporaryCredentialSelection(int slot, OcrProvider provider, string name)
    {
        OcrCredential actual = CredentialSchedule.ForSlot(slot);

        Assert.Equal(provider, actual.Provider);
        Assert.Equal(name, actual.Slot);
    }

    [Fact]
    public void LoadsTheJsonValueForEveryMappedSlot()
    {
        string[] expectedIds =
        [
            "tencent-a-id", "tencent-b-id", "tencent-c-id", "baidu-a-key",
            "baidu-b-key", "baidu-c-key", "baidu-d-key", "tencent-d-id"
        ];

        for (int i = 0; i < expectedIds.Length; i++)
            Assert.Equal(expectedIds[i], CredentialSchedule.ForSlot(i).Id);
    }

    [Fact]
    public void RotatesThroughEveryCredentialStartingAtTheSelectedOne()
    {
        IReadOnlyList<OcrCredential> credentials = CredentialSchedule.RotationFrom(CredentialSchedule.ForSlot(2));

        Assert.Equal(8, credentials.Count);
        Assert.Equal("腾讯云 C", credentials[0].DisplayName);
        Assert.Equal("腾讯云 B", credentials[^1].DisplayName);
        Assert.Equal(credentials.Select(item => item.DisplayName).Distinct().Count(), credentials.Count);
    }

    [Fact]
    public void RejectsInvalidTemporaryCredentialSelection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CredentialSchedule.ForSlot(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CredentialSchedule.ForSlot(8));
    }

    [Theory]
    [InlineData(0, OcrProvider.Baidu, "A")]
    [InlineData(1, OcrProvider.Baidu, "B")]
    [InlineData(2, OcrProvider.Baidu, "C")]
    [InlineData(3, OcrProvider.Tencent, "A")]
    [InlineData(4, OcrProvider.Tencent, "B")]
    [InlineData(5, OcrProvider.Tencent, "C")]
    [InlineData(6, OcrProvider.Tencent, "D")]
    [InlineData(7, OcrProvider.Baidu, "D")]
    public void SelectsTheOtherProviderWithTheSameSlotAsFallback(
        int slot, OcrProvider expectedProvider, string expectedName)
    {
        OcrCredential actual = CredentialSchedule.FallbackFor(CredentialSchedule.ForSlot(slot));

        Assert.Equal(expectedProvider, actual.Provider);
        Assert.Equal(expectedName, actual.Slot);
    }

    public void Dispose()
    {
        secretsScope.Dispose();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private const string TestJson = """
    {
      "tencent": {
        "A": { "secretId": "tencent-a-id", "secretKey": "tencent-a-secret" },
        "B": { "secretId": "tencent-b-id", "secretKey": "tencent-b-secret" },
        "C": { "secretId": "tencent-c-id", "secretKey": "tencent-c-secret" },
        "D": { "secretId": "tencent-d-id", "secretKey": "tencent-d-secret" }
      },
      "baidu": {
        "A": { "apiKey": "baidu-a-key", "secretKey": "baidu-a-secret" },
        "B": { "apiKey": "baidu-b-key", "secretKey": "baidu-b-secret" },
        "C": { "apiKey": "baidu-c-key", "secretKey": "baidu-c-secret" },
        "D": { "apiKey": "baidu-d-key", "secretKey": "baidu-d-secret" }
      }
    }
    """;
}
