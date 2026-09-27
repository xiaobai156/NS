using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace OcrLineTool;

internal static class LocalOcrIdentity
{
    // 分块读取以便在文件之间和块之间响应取消；摘要仍由标准库 SHA-256 计算，与整文件一次读取等价。
    internal static string Image(string path, CancellationToken cancellationToken = default)
    {
        using FileStream stream = File.OpenRead(path);
        return Image(stream, cancellationToken);
    }

    // Stream 重载是最小测试入口：用一个读到指定块才取消的流，可以确定性地验证
    // 取消检查发生在分块循环内部，而不是只在调用入口检查一次。
    internal static string Image(Stream stream, CancellationToken cancellationToken)
    {
        const int ChunkSize = 1024 * 1024;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = stream.Read(buffer, 0, ChunkSize);
                if (read == 0)
                    break;
                hash.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Computed once per batch, not for every image. Includes actual weights and worker source.
    internal static string Pipeline(PaddleOcrModel model, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        cancellationToken.ThrowIfCancellationRequested();
        Add("ocr-cache-v2");
        Add(model.ToString());
        string script = Path.Combine(ResultFilePaths.RuntimeDirectory(AppContext.BaseDirectory), "paddle_local_ocr.py");
        Add(File.Exists(script) ? Image(script, cancellationToken) : "missing-worker");
        string root = Environment.GetEnvironmentVariable("OCR_NVIDIA_MODEL_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "模型");
        string size = model == PaddleOcrModel.Medium ? "medium" : "small";
        foreach (string component in new[] { "det", "rec" })
        {
            string directory = Path.Combine(root, $"PP-OCRv6_{size}_{component}");
            Add(Path.GetFullPath(directory));
            if (!Directory.Exists(directory)) { Add("missing-model"); continue; }
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Add(Path.GetRelativePath(directory, file));
                Add(Image(file, cancellationToken));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
