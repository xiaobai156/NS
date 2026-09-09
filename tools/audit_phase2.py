from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase2-applied.md').exists():
    write('OcrLineTool.App/AtomicFile.cs', '''using System.Text;

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
                File.Replace(temporary, fullPath, fullPath + ".bak");
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
''')
    write('OcrLineTool.App/LocalOcrIdentity.cs', '''using System.Security.Cryptography;
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
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\\n"));
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
''')
    path = 'OcrLineTool.App/PaddleLocalOcrClient.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    old = '''        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask);'''
    text = replace(text, old, '''        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
        }
        catch
        {
            // Own only the process started here. Never kill unrelated Python processes.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            try { await process.WaitForExitAsync(CancellationToken.None); }
            catch (InvalidOperationException) { }
            try { await Task.WhenAll(standardOutputTask, standardErrorTask); } catch { }
            throw;
        }''')
    text = replace(text, '    private readonly IProcessRunner processRunner;', '''    private readonly IProcessRunner processRunner;
    private readonly Dictionary<string, string> imageErrors = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> LastImageErrors => imageErrors;''')
    text = replace(text, '        progress?.Report(new LocalOcrProgress(0, imagePaths.Count, string.Empty, "正在检查本地 OCR 缓存……"));', '''        imageErrors.Clear();
        cancellationToken.ThrowIfCancellationRequested();
        string pipeline = LocalOcrIdentity.Pipeline(model);
        var initialKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        progress?.Report(new LocalOcrProgress(0, imagePaths.Count, string.Empty, "正在检查本地 OCR 缓存……"));''')
    text = replace(text, '            string key = CacheKeyForModel(path, titleRatio, detectionMaxSide, model);', '''            string key = CacheKeyCore(path, titleRatio, detectionMaxSide, model, pipeline);
            initialKeys[path] = key;''')
    text = replace(text, '''                        if (!string.IsNullOrWhiteSpace(result.Error))
                            continue;
                        string[] texts = result.Texts ?? [];
                        results[result.Path] = texts;
                        string key = CacheKeyForModel(result.Path, titleRatio, detectionMaxSide, model);
                        cache[key] = new CacheEntry(key, texts);''', '''                        if (!initialKeys.TryGetValue(result.Path, out string? initialKey))
                            throw new OcrException("PaddleOCR 返回了未请求的图片路径。", "OCR_PROTOCOL_ERROR");
                        if (!string.IsNullOrWhiteSpace(result.Error))
                        {
                            imageErrors[result.Path] = result.Error;
                            if (ErrorCodeFor(result.Error) == CudaUnavailableCode)
                                throw new OcrException(result.Error, CudaUnavailableCode);
                            continue;
                        }
                        string key = CacheKeyCore(result.Path, titleRatio, detectionMaxSide, model, pipeline);
                        if (key != initialKey)
                        {
                            imageErrors[result.Path] = "图片在识别过程中发生变化，请重新识别。";
                            continue;
                        }
                        string[] texts = result.Texts ?? [];
                        results[result.Path] = texts;
                        // Empty results are not durable success-cache entries.
                        if (texts.Any(line => !string.IsNullOrWhiteSpace(line)))
                            cache[key] = new CacheEntry(key, texts);''')
    text = replace(text, '''        if (result.ExitCode != 0)
            throw new OcrException($"PaddleOCR 执行失败（代码 {result.ExitCode}）。");''', '''        if (result.ExitCode != 0)
        {
            // The worker writes its useful error before exiting non-zero.
            // Do not accept partial success on non-zero exit, or expose raw stderr/secrets.
            if (File.Exists(outputPath))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath, cancellationToken));
                    if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                        error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
                    {
                        string message = error.GetString()!;
                        throw new OcrException(message, ErrorCodeFor(message));
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
            throw new OcrException($"PaddleOCR 执行失败（代码 {result.ExitCode}）。");
        }''')
    text = replace(text, '            return entries.ToDictionary(entry => entry.Key, StringComparer.OrdinalIgnoreCase);', '''            return entries.Where(entry => entry.Texts is not null && entry.Texts.Any(line => !string.IsNullOrWhiteSpace(line)))
                .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);''')
    text = replace(text, '''            await using FileStream stream = File.Create(cachePath);
            await JsonSerializer.SerializeAsync(stream, cache.Values.ToArray(), cancellationToken: cancellationToken);''', '''            await AtomicFile.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache.Values.ToArray()),
                new UTF8Encoding(false), cancellationToken);''')
    text = replace(text, '        catch (IOException)\n        {\n            // 缓存不可写不影响本次识别。',
        '        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)\n        {\n            // 缓存不可写不影响本次识别。')
    marker = '''    private static string CacheKeyForModel(string path, double titleRatio, int? detectionMaxSide, PaddleOcrModel model)
    {'''
    text = replace(text, marker, '''    private static string CacheKeyForModel(string path, double titleRatio, int? detectionMaxSide, PaddleOcrModel model) =>
        CacheKeyCore(path, titleRatio, detectionMaxSide, model, LocalOcrIdentity.Pipeline(model));

    private static string CacheKeyCore(string path, double titleRatio, int? detectionMaxSide, PaddleOcrModel model, string pipeline)
    {''')
    text = replace(text, '        FileInfo file = new(path);', '        FileInfo file = new(path);')
    text = replace(text, '        if (model != PaddleOcrModel.Small)',
        '        key += $"|sha256={LocalOcrIdentity.Image(path)}|pipeline={pipeline}";\n        if (model != PaddleOcrModel.Small)')
    text = replace(text, 'compact=0.75-header=0.28', 'compact=0.55-header=0.28')
    write(path, text)

    path = 'OcrLineTool.App/paddle_local_ocr.py'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, 'import argparse\n', 'import argparse\nimport hashlib\nimport uuid\n')
    start = text.index('def _stage_model(')
    end = text.index('\ndef _prepare_model_root(', start)
    text = text[:start] + '''def _model_digest(path: Path) -> str:
    digest = hashlib.sha256()
    for file in sorted(item for item in path.rglob("*") if item.is_file()):
        digest.update(file.relative_to(path).as_posix().encode("utf-8") + b"\\0")
        with file.open("rb") as source:
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(chunk)
    return digest.hexdigest()


def _stage_model(source: Path, target: Path) -> None:
    expected = _model_digest(source)
    if _model_files_present(target) and _model_digest(target) == expected:
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    staging = target.parent / f".{target.name}.staging-{uuid.uuid4().hex}"
    retired = target.parent / f".{target.name}.retired-{uuid.uuid4().hex}"
    try:
        shutil.copytree(source, staging)
        if _model_digest(staging) != expected:
            raise RuntimeError("模型在复制时发生变化，请重试。")
        if target.exists():
            os.replace(target, retired)
        try:
            os.replace(staging, target)
        except OSError:
            if retired.exists() and not target.exists():
                os.replace(retired, target)
            raise
    finally:
        shutil.rmtree(staging, ignore_errors=True)
        shutil.rmtree(retired, ignore_errors=True)

''' + text[end:]
    text = replace(text, '    cache_root = _model_cache_root()', '''    # Immutable versioned roots prevent an updated model from reusing old weights.
    identity = hashlib.sha256("|".join(
        name + ":" + _model_digest(source_root / name) for name in model_names
    ).encode("utf-8")).hexdigest()
    cache_root = _model_cache_root() / identity''')
    write(path, text)
    write('docs/audit-phase2-applied.md', '# Audit repair phase 2\n\n'
        'Non-zero worker exits preserve structured errors. Per-image errors remain available separately; '
        'GPU errors stop the task. Cancellation terminates only the owned process tree. '
        'Local cache keys include image content, worker source and model-weight hashes; '
        'empty results are not persisted. ASCII model copies use content-versioned roots. '
        'Atomic file replacement preserves the previous successful file.\n')

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
