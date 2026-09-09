using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class ImageFolderCleanerTests
{
    [Fact]
    public void DeletesOnlySupportedImages()
    {
        string folder = CreateFolder();
        try
        {
            string image = Path.Combine(folder, "a.jpg");
            string note = Path.Combine(folder, "keep.txt");
            string nested = Path.Combine(folder, "nested");
            Directory.CreateDirectory(nested);
            string nestedImage = Path.Combine(nested, "b.png");
            File.WriteAllText(image, "image");
            File.WriteAllText(nestedImage, "image");
            File.WriteAllText(note, "keep");

            DeleteImagesResult result = ImageFolderCleaner.DeleteImages(folder, File.Delete);

            Assert.Equal(new DeleteImagesResult(2, 0), result);
            Assert.False(File.Exists(image));
            Assert.False(File.Exists(nestedImage));
            Assert.True(File.Exists(note));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CountsDeletionFailuresAndContinues()
    {
        string folder = CreateFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.jpg"), "a");
            File.WriteAllText(Path.Combine(folder, "b.png"), "b");
            int calls = 0;

            DeleteImagesResult result = ImageFolderCleaner.DeleteImages(folder, _ =>
            {
                if (++calls == 1)
                    throw new IOException("locked");
            });

            Assert.Equal(new DeleteImagesResult(1, 1), result);
            Assert.Equal(2, calls);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "OcrLineToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
