using System.Text;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class DistributionStateCleanupTests
{
    private static string CreateTempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"distribution-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void ClearsOnlyPreviousDayOwnershipStateFromConfiguredTargets()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "target");
            Directory.CreateDirectory(target);
            string config = Path.Combine(folder, "config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "杀数字分发规则.json"), JsonSerializer.Serialize(new
            {
                targetFile = "{issue}期-杀数字.txt",
                targetDirectory = target,
                sources = Array.Empty<object>()
            }), new UTF8Encoding(false));

            string state = Path.Combine(target, ".ocr-state");
            string backups = Path.Combine(state, ".ocr-backups");
            Directory.CreateDirectory(backups);
            string oldOwner = Path.Combine(state, "255期-杀数字.txt.owners.json");
            string todayOwner = Path.Combine(state, "255期-头.txt.owners.json");
            string oldBackup = Path.Combine(backups, "255期-杀数字.txt.owners.json.bak");
            File.WriteAllText(oldOwner, "[]", new UTF8Encoding(false));
            File.WriteAllText(todayOwner, "[]", new UTF8Encoding(false));
            File.WriteAllText(oldBackup, "[]", new UTF8Encoding(false));
            DateTime yesterday = DateTime.UtcNow.AddDays(-1);
            File.SetLastWriteTimeUtc(oldOwner, yesterday);
            File.SetLastWriteTimeUtc(oldBackup, yesterday);

            DistributionStateCleanup.ClearStaleBefore(config);

            Assert.False(File.Exists(oldOwner));
            Assert.False(File.Exists(oldBackup));
            Assert.False(Directory.Exists(backups));
            Assert.True(File.Exists(todayOwner));
            Assert.True(Directory.Exists(state));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void BrokenConfigsAreSkippedAndMissingTargetsFallBackToTheDefault()
    {
        string folder = CreateTempFolder();
        try
        {
            string config = Path.Combine(folder, "config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "坏分发规则.json"), "{ not json", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(config, "空分发规则.json"), "{}", new UTF8Encoding(false));

            string[] targets = DistributionStateCleanup.EnumerateTargetDirectories(config).ToArray();

            Assert.Equal([ResultDistributor.TargetDirectory], targets);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
