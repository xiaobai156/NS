using System.Text.RegularExpressions;

namespace OcrLineTool;

public static class LineExtractor
{
    public static string? Extract(IEnumerable<string> lines, int issue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issue);
        var pattern = new Regex($@"(?<!\d){issue}\s*期\s*[:：]?\s*(.*)$", RegexOptions.CultureInvariant);

        foreach (string line in lines)
        {
            Match match = pattern.Match(line);
            if (match.Success)
                return $"{issue}期：{match.Groups[1].Value.Trim()}"
                    .Replace('[', '【')
                    .Replace(']', '】')
                    .Replace("南国挽心正正正杀一肖", "南国挽心㊣㊣㊣杀一肖", StringComparison.Ordinal);
        }

        return null;
    }
}
