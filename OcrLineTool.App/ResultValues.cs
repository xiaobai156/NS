using System.Text.RegularExpressions;

namespace OcrLineTool;

/// <summary>Successful values and sticky conflicts for one recognition run.</summary>
internal sealed class ResultValues : Dictionary<string, string>
{
    internal HashSet<string> Conflicts { get; } = new(StringComparer.Ordinal);
    internal ResultValues(IEqualityComparer<string> comparer) : base(comparer) { }
    internal ResultValues(IDictionary<string, string> source, IEqualityComparer<string> comparer) : base(source, comparer)
    {
        if (source is ResultValues previous) Conflicts.UnionWith(previous.Conflicts);
    }

    internal static void AddTo(IDictionary<string, string> values, string id, string value)
    {
        if (values is not ResultValues guarded) { values.TryAdd(id, value); return; }
        if (guarded.Conflicts.Contains(id)) return;
        if (guarded.TryGetValue(id, out string? existing) && Canonical(existing) != Canonical(value))
        {
            guarded.Remove(id);
            guarded.Conflicts.Add(id);
            return;
        }
        guarded.TryAdd(id, value);
    }

    internal static bool IsConflict(IReadOnlyDictionary<string, string> values, string id) =>
        values is ResultValues guarded && guarded.Conflicts.Contains(id);

    private static string Canonical(string value)
    {
        value = value.Trim();
        if (value.Length > 0 && (value.All("马蛇龙兔虎牛鼠猪狗鸡猴羊".Contains) || value.All("金木水火土".Contains)))
            return string.Concat(value.Order());
        if (Regex.IsMatch(value, @"^\d{2}(?:[ ,]+\d{2})+$"))
            return string.Join(",", Regex.Split(value, "[ ,]+").Order(StringComparer.Ordinal));
        if (Regex.IsMatch(value, @"^\d尾(?:[+ ]+\d尾)+$"))
            return string.Join("+", Regex.Split(value, "[+ ]+").Order(StringComparer.Ordinal));
        return value;
    }
}
