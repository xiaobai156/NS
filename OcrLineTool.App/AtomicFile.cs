using System.Text;

namespace OcrLineTool;

/// <summary>Same-directory replacement; an incomplete write never truncates the last good file.</summary>
internal static class AtomicFile
{
    public static async Task WriteAllTextAsync(string path, string text, Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, text, encoding ?? new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
            {
                string backups = Path.Combine(Path.GetDirectoryName(fullPath)!, ".ocr-backups");
                Directory.CreateDirectory(backups);
                File.Replace(temporary, fullPath, Path.Combine(backups, Path.GetFileName(fullPath) + ".bak"));
            }
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static Task WriteAllLinesAsync(string path, IEnumerable<string> lines, Encoding encoding,
        CancellationToken cancellationToken = default) =>
        WriteAllTextAsync(path, string.Concat(lines.Select(line => line + Environment.NewLine)), encoding, cancellationToken);

    public static void WriteAllText(string path, string text, Encoding? encoding = null) =>
        WriteAllTextAsync(path, text, encoding).GetAwaiter().GetResult();

    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding) =>
        WriteAllLinesAsync(path, lines, encoding).GetAwaiter().GetResult();
}
