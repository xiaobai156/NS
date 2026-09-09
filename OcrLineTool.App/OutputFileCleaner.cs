using Microsoft.VisualBasic.FileIO;

namespace OcrLineTool;

public sealed record ClearFilesResult(int Deleted, int Failed);

public static class OutputFileCleaner
{
    public static ClearFilesResult Clear(
        IEnumerable<string> directories,
        Action<string>? deleteFile = null)
    {
        deleteFile ??= path => FileSystem.DeleteFile(
            path,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        int deleted = 0;
        int failed = 0;

        foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
                continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*", options);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
                continue;
            }

            foreach (string path in files)
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
        }
        return new(deleted, failed);
    }
}
