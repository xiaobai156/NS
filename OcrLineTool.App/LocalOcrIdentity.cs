using System.Security.Cryptography;
using System.Text;

namespace OcrLineTool;

internal static class LocalOcrIdentity
{
    internal static string Image(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // Computed once per batch, not for every image. Includes actual weights and worker source.
    internal static string Pipeline(PaddleOcrModel model)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        Add("ocr-cache-v2");
        Add(model.ToString());
        string script = Path.Combine(ResultFilePaths.RuntimeDirectory(AppContext.BaseDirectory), "paddle_local_ocr.py");
        Add(File.Exists(script) ? Image(script) : "missing-worker");
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
                Add(Path.GetRelativePath(directory, file));
                Add(Image(file));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
