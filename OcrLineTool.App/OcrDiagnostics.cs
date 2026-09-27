using System.Text;
using System.Text.Json;

namespace OcrLineTool;

/// <summary>
/// 诊断 JSON 的统一写入口：按当前群/当前期原子替换同一个文件（不新建缓存、不留历史日志），
/// 写失败只把原因交回调用方——识别/复抓结果照旧写盘，状态栏说明「本轮诊断未更新」，旧诊断保留。
/// </summary>
internal static class OcrDiagnostics
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>写入诊断并返回错误原因；成功返回 null。取消不算诊断失败（调用方已按取消路径收尾）。</summary>
    internal static async Task<string?> TryWriteAsync(string path, object payload)
    {
        try
        {
            await AtomicFile.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, Options), new UTF8Encoding(true));
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception.Message;
        }
    }

    /// <summary>状态栏用的诊断后缀；写失败时明确说明本轮诊断未更新，不覆盖任务本身的结果说明。</summary>
    internal static string StatusSuffix(string path, string? error) => error is null
        ? $"诊断：{path}"
        : $"诊断：{path}（本轮诊断未更新：{error}）";
}
