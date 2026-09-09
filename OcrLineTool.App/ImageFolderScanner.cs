namespace OcrLineTool;

public static class ImageFolderScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    public static string[] Scan(string folder)
    {
        if (!Directory.Exists(folder))
            throw new OcrException("固定图片目录不存在：" + folder);

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string[] ListSubfolders(string root)
    {
        if (!Directory.Exists(root))
            throw new OcrException("固定图片目录不存在：" + root);

        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsSubfolder(string root, string folder)
    {
        string rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            + Path.DirectorySeparatorChar;
        string selectedPath = Path.GetFullPath(folder);
        return selectedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
