using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class ImageFolderScannerTests
{
    [Fact]
    public void ScansSupportedImagesRecursively()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ocr-line-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "b.jpg"), "x");
            File.WriteAllText(Path.Combine(folder, "a.PNG"), "x");
            File.WriteAllText(Path.Combine(folder, "skip.txt"), "x");
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "nested", "nested.jpg"), "x");

            string[] files = ImageFolderScanner.Scan(folder);

            Assert.Equal(["a.PNG", "b.jpg", "nested.jpg"], files.Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ReportsMissingDirectory()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var error = Assert.Throws<OcrException>(() => ImageFolderScanner.Scan(folder));

        Assert.Contains("固定图片目录不存在", error.Message);
    }

    [Fact]
    public void ListsOnlyImmediateSubfoldersInNameOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "ocr-folder-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string second = Directory.CreateDirectory(Path.Combine(root, "B组")).FullName;
            string first = Directory.CreateDirectory(Path.Combine(root, "A组")).FullName;
            Directory.CreateDirectory(Path.Combine(first, "不显示的下级目录"));
            File.WriteAllText(Path.Combine(root, "不显示的文件.txt"), "x");

            string[] folders = ImageFolderScanner.ListSubfolders(root);

            Assert.Equal([first, second], folders);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AcceptsOnlyFoldersBelowTheFixedRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ocr-root");

        Assert.True(ImageFolderScanner.IsSubfolder(root, Path.Combine(root, "人物一")));
        Assert.True(ImageFolderScanner.IsSubfolder(root, Path.Combine(root, "人物一", "更多图片")));
        Assert.False(ImageFolderScanner.IsSubfolder(root, root));
        Assert.False(ImageFolderScanner.IsSubfolder(root, root + "-other"));
        Assert.False(ImageFolderScanner.IsSubfolder(root, Path.GetTempPath()));
    }
}
