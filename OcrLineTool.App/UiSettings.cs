using System.Text;
using System.Text.Json;

namespace OcrLineTool;

/// <summary>界面偏好，保存在程序目录下的“界面设置.json”，升级和清除结果都不影响。</summary>
internal sealed record UiSettings(bool ShowRecognizeButton)
{
    internal static UiSettings Default { get; } = new(false);

    internal static string PathFor(string appDirectory) =>
        Path.Combine(appDirectory, "界面设置.json");

    internal static UiSettings Load(string appDirectory)
    {
        try
        {
            string path = PathFor(appDirectory);
            if (!File.Exists(path))
                return Default;
            return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path)) ?? Default;
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
