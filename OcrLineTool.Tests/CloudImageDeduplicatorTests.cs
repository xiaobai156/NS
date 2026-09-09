using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class CloudImageDeduplicatorTests
{
    [Fact]
    public async Task SameOriginalBytesInTargetGroupAreRecognizedOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "cloud-dedup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "first.jpg");
            string second = Path.Combine(root, "renamed-copy.png");
            await File.WriteAllBytesAsync(first, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(second, [1, 2, 3, 4]);
            var deduplicator = new CloudImageDeduplicator();
            int calls = 0;
            Task<IReadOnlyList<string>> Request()
            {
                calls++;
                return Task.FromResult<IReadOnlyList<string>>(["云 OCR 结果"]);
            }

            IReadOnlyList<string> firstResult = await deduplicator.RecognizeAsync(
                @"C:\图片\9.2-黄大仙新澳", first, [], Request);
            IReadOnlyList<string> secondResult = await deduplicator.RecognizeAsync(
                @"C:\图片\9.2-黄大仙新澳", second, [], Request);

            Assert.Equal(["云 OCR 结果"], firstResult);
            Assert.Equal(firstResult, secondResult);
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DifferentBytesAreNotDeduplicated()
    {
        string root = Path.Combine(Path.GetTempPath(), "cloud-dedup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "first.jpg");
            string second = Path.Combine(root, "second.jpg");
            await File.WriteAllBytesAsync(first, [1, 2, 3]);
            await File.WriteAllBytesAsync(second, [1, 2, 4]);
            var deduplicator = new CloudImageDeduplicator();
            int calls = 0;
            Task<IReadOnlyList<string>> Request() =>
                Task.FromResult<IReadOnlyList<string>>(["结果 " + ++calls]);

            await deduplicator.RecognizeAsync(@"C:\图片\9.2-嫣然心水", first, [], Request);
            await deduplicator.RecognizeAsync(@"C:\图片\9.2-嫣然心水", second, [], Request);

            Assert.Equal(2, calls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OtherGroupsAndJieshaoKillTailKeepTheirCurrentBehavior()
    {
        string root = Path.Combine(Path.GetTempPath(), "cloud-dedup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "first.jpg");
            string second = Path.Combine(root, "second.jpg");
            await File.WriteAllBytesAsync(first, [8, 8, 8]);
            await File.WriteAllBytesAsync(second, [8, 8, 8]);
            var deduplicator = new CloudImageDeduplicator();
            int otherCalls = 0;
            Task<IReadOnlyList<string>> OtherRequest() =>
                Task.FromResult<IReadOnlyList<string>>(["其他 " + ++otherCalls]);
            await deduplicator.RecognizeAsync(@"C:\图片\其他群", first, [], OtherRequest);
            await deduplicator.RecognizeAsync(@"C:\图片\其他群", second, [], OtherRequest);
            Assert.Equal(2, otherCalls);

            OcrRule tail = new("杰少", "尾", "杰少杀一尾", "加杀尾", "原创杰少", "杰少");
            int tailCalls = 0;
            Task<IReadOnlyList<string>> TailRequest() =>
                Task.FromResult<IReadOnlyList<string>>(["尾 " + ++tailCalls]);
            await deduplicator.RecognizeAsync(@"C:\图片\9.2-嫣然心水", first, [tail], TailRequest);
            await deduplicator.RecognizeAsync(@"C:\图片\9.2-嫣然心水", second, [tail], TailRequest);
            Assert.Equal(2, tailCalls);
        }
        finally { Directory.Delete(root, true); }
    }
}
