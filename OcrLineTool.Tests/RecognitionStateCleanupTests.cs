using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class RecognitionStateCleanupTests
{
    [Fact]
    public void ClearAllRemovesStateFilesAndEvidenceButKeepsTheDirectory()
    {
        string app = Directory.CreateTempSubdirectory("ocr-state-clear-").FullName;
        try
        {
            string root = ResultFilePaths.RecognitionStateDirectory(app);
            string evidence = Path.Combine(root, "证据", "甲群_254期");
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(root, "甲群_254期.state.json"), "{}");
            File.WriteAllText(Path.Combine(evidence, "a.png"), "x");

            RecognitionStateStore.ClearAll(app);

            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(app, recursive: true);
        }
    }

    [Fact]
    public void ClearStaleDaysKeepsTodayAndRemovesOlderFilesAndEvidence()
    {
        string app = Directory.CreateTempSubdirectory("ocr-state-stale-").FullName;
        try
        {
            string root = ResultFilePaths.RecognitionStateDirectory(app);
            Directory.CreateDirectory(root);
            string oldFile = Path.Combine(root, "旧群_253期.state.json");
            string todayFile = Path.Combine(root, "甲群_254期.state.json");
            string oldEvidence = Path.Combine(root, "证据", "旧群_253期");
            string todayEvidence = Path.Combine(root, "证据", "甲群_254期");
            Directory.CreateDirectory(oldEvidence);
            Directory.CreateDirectory(todayEvidence);
            File.WriteAllText(oldFile, "{}");
            File.WriteAllText(todayFile, "{}");
            File.WriteAllText(Path.Combine(oldEvidence, "a.png"), "x");
            File.WriteAllText(Path.Combine(todayEvidence, "b.png"), "y");
            DateTime twoDaysAgoUtc = DateTime.UtcNow.AddDays(-2);
            File.SetLastWriteTimeUtc(oldFile, twoDaysAgoUtc);
            File.SetLastWriteTimeUtc(Path.Combine(oldEvidence, "a.png"), twoDaysAgoUtc);

            RecognitionStateStore.ClearStaleDays(app);

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(todayFile));
            Assert.False(Directory.Exists(oldEvidence));
            Assert.True(Directory.Exists(todayEvidence));
            Assert.True(File.Exists(Path.Combine(todayEvidence, "b.png")));
        }
        finally
        {
            Directory.Delete(app, recursive: true);
        }
    }
}
