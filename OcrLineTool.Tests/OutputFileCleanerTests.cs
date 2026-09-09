using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class OutputFileCleanerTests
{
    [Fact]
    public void DeletesEveryFileTypeRecursivelyAndKeepsTheFolders()
    {
        string root = CreateFolder();
        string temporary = Path.Combine(root, "临时文件");
        string important = Path.Combine(root, "重要结果");
        string diagnostic = Path.Combine(temporary, "诊断");
        string group = Path.Combine(important, "群结果");
        Directory.CreateDirectory(diagnostic);
        Directory.CreateDirectory(group);
        File.WriteAllText(Path.Combine(temporary, "a.tmp"), "a");
        File.WriteAllText(Path.Combine(diagnostic, "b.json"), "b");
        File.WriteAllText(Path.Combine(important, "c.bin"), "c");
        File.WriteAllText(Path.Combine(group, "d.txt"), "d");

        try
        {
            ClearFilesResult result = OutputFileCleaner.Clear([temporary, important], File.Delete);

            Assert.Equal(new ClearFilesResult(4, 0), result);
            Assert.Empty(Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(important, "*", SearchOption.AllDirectories));
            Assert.True(Directory.Exists(diagnostic));
            Assert.True(Directory.Exists(group));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IgnoresMissingFoldersAndContinuesAfterAFileFails()
    {
        string root = CreateFolder();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.dat");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        int calls = 0;

        try
        {
            ClearFilesResult result = OutputFileCleaner.Clear(
                [root, Path.Combine(root, "missing")],
                path =>
                {
                    if (++calls == 1)
                        throw new IOException("locked");
                    File.Delete(path);
                });

            Assert.Equal(new ClearFilesResult(1, 1), result);
            Assert.Equal(2, calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "OcrLineToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
