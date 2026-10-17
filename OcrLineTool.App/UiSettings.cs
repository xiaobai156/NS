using System.Text;
using System.Text.Json;

namespace OcrLineTool;

public enum LocalOcrDevice
{
    Gpu,
    Cpu
}

/// <summary>界面偏好，保存在程序目录下的“界面设置.json”，升级和清除结果都不影响。</summary>
internal sealed record UiSettings(
    bool ShowRecognizeButton,
    bool RetryUsesLocalOcr = true,
    LocalOcrDevice OcrDevice = LocalOcrDevice.Gpu,
    string? ImageRootDirectory = null)
{
    /// <summary>默认抓取路径（每日图片根目录）；用户未自定义时使用。</summary>
    internal const string DefaultImageRootDirectory =
        @"C:\Users\Administrator\Desktop\每天工具\飞机抓图\结果";

    internal static UiSettings Default { get; } = new(false);

    internal static string PathFor(string appDirectory) =>
        Path.Combine(appDirectory, "界面设置.json");

    /// <summary>
    /// 有效抓取路径：未自定义时用默认值；已自定义时原样返回。目录临时不可用交给界面按
    /// “群目录暂不可用”提示，不静默切回默认目录，避免读到错误来源的图片。
    /// </summary>
    internal string EffectiveImageRootDirectory() =>
        string.IsNullOrWhiteSpace(ImageRootDirectory) ? DefaultImageRootDirectory : ImageRootDirectory;

    /// <summary>按程序目录里的设置解析有效抓取路径。</summary>
    internal static string ResolveImageRootDirectory(string appDirectory) =>
        Load(appDirectory).EffectiveImageRootDirectory();

    internal static UiSettings Load(string appDirectory)
    {
        try
        {
            string path = PathFor(appDirectory);
            if (!File.Exists(path))
                return Default;
            UiSettings settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path)) ?? Default;
            return Enum.IsDefined(settings.OcrDevice) ? settings : settings with { OcrDevice = LocalOcrDevice.Gpu };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    internal static void Save(string appDirectory, UiSettings settings) =>
        AtomicFile.WriteAllText(
            PathFor(appDirectory),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(true));
}
