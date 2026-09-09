using Microsoft.VisualBasic.FileIO;

namespace OcrLineTool;

public sealed record DeleteImagesResult(int Deleted, int Failed);

public static class ImageFolderCleaner
{
    public static DeleteImagesResult DeleteImages(string directory, Action<string>? deleteFile = null)
    {
        deleteFile ??= path => FileSystem.DeleteFile(
            path,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);

        int deleted = 0;
        int failed = 0;
        foreach (string path in ImageFolderScanner.Scan(directory))
        {
            try
            {
                deleteFile(path);
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }
        return new(deleted, failed);
    }
}
