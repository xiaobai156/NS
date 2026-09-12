using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OcrLineTool;

public sealed record NativeOcrMirrorOutcome(
    int Written,
    int SkippedMissingDirectory,
    int SkippedExisting,
    int Failed,
    IReadOnlyList<string> Errors)
{
    public static readonly NativeOcrMirrorOutcome Empty = new(0, 0, 0, 0, []);

    public bool HasAny => Written > 0
        || SkippedMissingDirectory > 0
        || SkippedExisting > 0
        || Failed > 0
        || Errors.Count > 0;

    public NativeOcrMirrorOutcome Merge(NativeOcrMirrorOutcome other) => new(
        Written + other.Written,
        SkippedMissingDirectory + other.SkippedMissingDirectory,
        SkippedExisting + other.SkippedExisting,
        Failed + other.Failed,
        [.. Errors, .. other.Errors]);
}

/// <summary>
/// Mirrors distributed rows into the OCR自动分流 data buckets
/// (%APPDATA%\NativeOcrApp\data\directory-data.json). Only directories that
/// already exist with the exact same name are filled, and only while their
/// value is still empty; everything else is skipped without adding entries.
/// </summary>
public static class NativeOcrDirectoryMirror
{
    private const string Zodiacs = "鼠牛虎兔龙蛇马羊猴鸡狗猪";

    public static string DefaultStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NativeOcrApp",
        "data",
        "directory-data.json");

    public static NativeOcrMirrorOutcome Apply(
        string? storePath, string? kind, IEnumerable<(string Label, string Value)> rows)
    {
        (string Label, string Value)[] entries = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Label) && !string.IsNullOrWhiteSpace(row.Value))
            .ToArray();
        if (entries.Length == 0 || string.IsNullOrWhiteSpace(kind))
            return NativeOcrMirrorOutcome.Empty;

        string path = string.IsNullOrWhiteSpace(storePath) ? DefaultStorePath : storePath;
        if (!File.Exists(path))
            return Fail(entries.Length, $"外部软件数据文件不存在：{path}");

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            if (root is not JsonObject document || document[kind] is not JsonObject bucket)
                return Fail(entries.Length, $"外部软件数据缺少 {kind} 数据段。");

            JsonObject values = bucket["Values"] as JsonObject ?? new JsonObject();
            Dictionary<string, string> directories = ReadDirectoryIds(bucket);

            int written = 0;
            int missing = 0;
            int existing = 0;
            bool changed = false;
            foreach ((string label, string value) in entries)
            {
                if (!directories.TryGetValue(Normalize(label), out string? id))
                {
                    missing++;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(values[id]?.GetValue<string>()))
                {
                    existing++;
                    continue;
                }
                string formatted = Format(kind, value);
                if (formatted.Length == 0)
                    continue;
                values[id] = formatted;
                written++;
                changed = true;
            }

            if (changed)
            {
                bucket["Values"] = values;
                WriteAtomic(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            return new NativeOcrMirrorOutcome(written, missing, existing, 0, []);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException
            or FormatException)
        {
            return Fail(entries.Length, $"外部软件同步失败：{Path.GetFileName(path)} 不可写或格式无效。");
        }
    }

    private static Dictionary<string, string> ReadDirectoryIds(JsonObject bucket)
    {
        var directories = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bucket["Directories"] is not JsonArray list)
            return directories;
        foreach (JsonNode? node in list)
        {
            if (node is not JsonObject directory)
                continue;
            string? id = directory["Id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
                continue;
            string? label = directory["Label"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(label))
                directories.TryAdd(Normalize(label), id);
            directories.TryAdd(Normalize(id), id);
        }
        return directories;
    }

    private static NativeOcrMirrorOutcome Fail(int count, string message) =>
        new(0, 0, 0, count, [message]);

    private static string Normalize(string value) =>
        string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));

    private static string Format(string kind, string value) => kind switch
    {
        "Shengxiao" => FormatShengxiao(value),
        "Dawei" => FormatDawei(value),
        _ => FormatTwoDigitPairs(value)
    };

    // Same semantics as the other app: keep zodiac characters only, deduplicate,
    // and turn a complete nine-zodiac list into its single missing zodiac.
    private static string FormatShengxiao(string value)
    {
        var seen = new HashSet<char>();
        var result = new List<char>();
        foreach (char character in value)
        {
            if (Zodiacs.Contains(character) && seen.Add(character))
                result.Add(character);
        }
        return result.Count == 9
            ? new string(Zodiacs.Where(character => !seen.Contains(character)).ToArray())
            : new string(result.ToArray());
    }

    // 杀号/杀五码: strip everything non-numeric, then group into pairs.
    private static string FormatTwoDigitPairs(string value)
    {
        var pure = new StringBuilder();
        foreach (char character in value)
        {
            if (char.IsAsciiDigit(character))
                pure.Append(character);
        }
        var formatted = new List<char>(pure.Length + pure.Length / 2);
        for (int index = 0; index < pure.Length; index++)
        {
            if (index > 0 && index % 2 == 0)
                formatted.Add(',');
            formatted.Add(pure[index]);
        }
        return new string(formatted.ToArray());
    }

    // 大围: split on non-digits, zero-pad one-digit runs, cap at 100 entries.
    private static string FormatDawei(string value)
    {
        var parts = new List<string>();
        var digits = new StringBuilder();
        foreach (char character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                continue;
            }
            Flush(digits, parts);
        }
        Flush(digits, parts);

        var formatted = new List<string>();
        foreach (string part in parts)
        {
            if (formatted.Count >= 100)
                break;
            formatted.Add(part.Length == 1 ? "0" + part : part[..2]);
        }
        return string.Join(",", formatted);
    }

    private static void Flush(StringBuilder digits, List<string> parts)
    {
        if (digits.Length == 0)
            return;
        parts.Add(digits.ToString());
        digits.Clear();
    }

    private static void WriteAtomic(string path, string text)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
