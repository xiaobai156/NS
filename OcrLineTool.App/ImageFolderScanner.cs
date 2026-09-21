namespace OcrLineTool;

public static class ImageFolderScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly EnumerationOptions SafeTopLevelEnumeration = new()
    {
        // A missing child must surface so MainForm can retain the last complete list and retry.
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static string[] Scan(string folder)
    {
        if (!Directory.Exists(folder))
            throw new OcrException("固定图片目录不存在：" + folder);

        try
        {
            return Directory.EnumerateFiles(folder, "*", SafeEnumeration)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("扫描图片目录失败：" + folder);
        }
    }

    public static string[] ListSubfolders(string root)
    {
        if (!Directory.Exists(root))
            throw new OcrException("固定图片目录不存在：" + root);

        try
        {
            return Directory.EnumerateDirectories(root, "*", SafeTopLevelEnumeration)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("扫描图片目录失败：" + root);
        }
    }

    public static bool IsSubfolder(string root, string folder)
    {
        string rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            + Path.DirectorySeparatorChar;
        string selectedPath = Path.GetFullPath(folder);
        return selectedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
