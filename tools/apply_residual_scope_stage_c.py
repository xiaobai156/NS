from pathlib import Path


def patch(path: str, old: str, new: str, label: str, count: int = 1):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(f"{label}: expected {count}, found {actual}")
    p.write_text(text.replace(old, new, count), encoding="utf-8")


# ---------------------------------------------------------------------------
# RuleEngine: configured sibling author/section markers close the active cell.
# ---------------------------------------------------------------------------
patch(
    "OcrLineTool.App/RuleEngine.cs",
    '''    bool AllowValueWithoutKeyword = false,\n    bool StrictIssueBlock = false,\n    bool SingleValuePerIssue = false)''',
    '''    bool AllowValueWithoutKeyword = false,\n    bool StrictIssueBlock = false,\n    bool SingleValuePerIssue = false,\n    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<string>? SiblingBoundaries = null)''',
    "OcrRule sibling boundary metadata",
)

patch(
    "OcrLineTool.App/RuleEngine.cs",
    '''        foreach (string line in relevant)\n        {\n            string? value = ExtractTypedForRule(line, rule);\n            if (value is not null)\n                observed.Add(value);\n        }''',
    '''        foreach (string line in relevant)\n        {\n            string scoped = ScopeCandidateToRuleCell(line, rule, aliases);\n            string? value = ExtractTypedForRule(scoped, rule);\n            if (value is not null)\n                observed.Add(value);\n        }''',
    "scope typed extraction to configured cell",
)

patch(
    "OcrLineTool.App/RuleEngine.cs",
    '''    private static IEnumerable<string> MaximalCandidates(IEnumerable<string> source)\n    {''',
    '''    private static string ScopeCandidateToRuleCell(string candidate, OcrRule rule, IReadOnlyList<string> aliases)\n    {\n        if (rule.SiblingBoundaries is not { Count: > 0 })\n            return candidate;\n\n        string normalized = Normalize(candidate);\n        string[] preferredAnchors = new[]\n        {\n            rule.Section ?? string.Empty,\n            rule.RequiredKeyword ?? string.Empty,\n            rule.Keyword,\n            rule.Label ?? string.Empty\n        }\n            .Where(anchor => !string.IsNullOrWhiteSpace(anchor))\n            .Select(Normalize)\n            .Concat(aliases)\n            .Where(anchor => anchor.Length > 0)\n            .Distinct(StringComparer.Ordinal)\n            .ToArray();\n\n        int start = -1;\n        int anchorLength = 0;\n        foreach (string anchor in preferredAnchors)\n        {\n            int index = normalized.IndexOf(anchor, StringComparison.Ordinal);\n            if (index < 0)\n                continue;\n            start = index;\n            anchorLength = anchor.Length;\n            break;\n        }\n        if (start < 0)\n            return candidate;\n\n        int searchStart = start + anchorLength;\n        int end = normalized.Length;\n        foreach (string sibling in rule.SiblingBoundaries\n            .Where(boundary => !string.IsNullOrWhiteSpace(boundary))\n            .Select(Normalize)\n            .Where(boundary => boundary.Length > 0)\n            .Distinct(StringComparer.Ordinal))\n        {\n            int index = normalized.IndexOf(sibling, searchStart, StringComparison.Ordinal);\n            if (index >= 0 && index < end)\n                end = index;\n        }\n\n        return end < normalized.Length ? normalized[start..end] : candidate;\n    }\n\n    private static IEnumerable<string> MaximalCandidates(IEnumerable<string> source)\n    {''',
    "add configured cell scope helper",
)

# ---------------------------------------------------------------------------
# RuleCatalog: reject invalid bool types and derive sibling boundaries from the
# current JSON catalog instead of hardcoding author names in RuleEngine.
# ---------------------------------------------------------------------------
patch(
    "OcrLineTool.App/RuleCatalog.cs",
    '''    public static IReadOnlyList<OcrRule> Load(string path)\n    {''',
    '''    private static bool ReadBooleanOption(\n        JsonElement owner, string propertyName, bool defaultValue, string fileName)\n    {\n        if (!owner.TryGetProperty(propertyName, out JsonElement value))\n            return defaultValue;\n        return value.ValueKind switch\n        {\n            JsonValueKind.True => true,\n            JsonValueKind.False => false,\n            _ => throw new OcrException($"{fileName} 中 {propertyName} 必须是布尔值 true/false。")\n        };\n    }\n\n    public static IReadOnlyList<OcrRule> Load(string path)\n    {''',
    "add strict boolean reader",
)

patch(
    "OcrLineTool.App/RuleCatalog.cs",
    '''            bool strictIssueBlock = document.RootElement.TryGetProperty("strictIssueBlock", out JsonElement strictElement)\n                && strictElement.ValueKind == JsonValueKind.True;''',
    '''            bool strictIssueBlock = ReadBooleanOption(\n                document.RootElement, "strictIssueBlock", false, fileName);''',
    "root strictIssueBlock validation",
)

patch(
    "OcrLineTool.App/RuleCatalog.cs",
    '''                bool ignoreIssue = item.TryGetProperty("ignoreIssue", out JsonElement ignoreIssueElement)\n                    && ignoreIssueElement.ValueKind == JsonValueKind.True;\n                bool allowNearbyValue = item.TryGetProperty("allowNearbyValue", out JsonElement nearbyElement)\n                    && nearbyElement.ValueKind == JsonValueKind.True;\n                bool allowValueWithoutKeyword = item.TryGetProperty("allowValueWithoutKeyword", out JsonElement withoutKeywordElement)\n                    && withoutKeywordElement.ValueKind == JsonValueKind.True;\n                bool singleValuePerIssue = item.TryGetProperty("singleValuePerIssue", out JsonElement singleValueElement)\n                    && singleValueElement.ValueKind == JsonValueKind.True;\n                bool itemStrictIssueBlock = item.TryGetProperty("strictIssueBlock", out JsonElement itemStrictElement)\n                    ? itemStrictElement.ValueKind == JsonValueKind.True\n                    : strictIssueBlock;''',
    '''                bool ignoreIssue = ReadBooleanOption(item, "ignoreIssue", false, fileName);\n                bool allowNearbyValue = ReadBooleanOption(item, "allowNearbyValue", false, fileName);\n                bool allowValueWithoutKeyword = ReadBooleanOption(item, "allowValueWithoutKeyword", false, fileName);\n                bool singleValuePerIssue = ReadBooleanOption(item, "singleValuePerIssue", false, fileName);\n                bool itemStrictIssueBlock = ReadBooleanOption(item, "strictIssueBlock", strictIssueBlock, fileName);''',
    "per-rule boolean validation",
)

patch(
    "OcrLineTool.App/RuleCatalog.cs",
    '''            if (output.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != output.Count)\n                throw new OcrException($"{fileName} 中存在重复的输出名称。");\n            return output;''',
    '''            if (output.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != output.Count)\n                throw new OcrException($"{fileName} 中存在重复的输出名称。");\n\n            Dictionary<string, OcrRule[]> byFolder = output\n                .Where(rule => !string.IsNullOrWhiteSpace(rule.Folder))\n                .GroupBy(rule => rule.Folder!, StringComparer.OrdinalIgnoreCase)\n                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);\n            return output.Select(rule =>\n            {\n                if (string.IsNullOrWhiteSpace(rule.Folder)\n                    || !byFolder.TryGetValue(rule.Folder, out OcrRule[]? siblings)\n                    || siblings.Length <= 1)\n                    return rule;\n\n                string[] ownMarkers = new[]\n                {\n                    rule.Keyword, rule.Label ?? string.Empty, rule.Section ?? string.Empty,\n                    rule.RequiredKeyword ?? string.Empty\n                }\n                    .Where(marker => !string.IsNullOrWhiteSpace(marker))\n                    .ToArray();\n                string[] boundaries = siblings\n                    .Where(other => !string.Equals(other.Id, rule.Id, StringComparison.Ordinal))\n                    .SelectMany(other => new[]\n                    {\n                        other.Keyword, other.Label ?? string.Empty, other.Section ?? string.Empty,\n                        other.RequiredKeyword ?? string.Empty\n                    })\n                    .Where(marker => !string.IsNullOrWhiteSpace(marker))\n                    .Where(marker => !ownMarkers.Contains(marker, StringComparer.Ordinal))\n                    .Distinct(StringComparer.Ordinal)\n                    .ToArray();\n                return boundaries.Length == 0 ? rule : rule with { SiblingBoundaries = boundaries };\n            }).ToArray();''',
    "derive sibling cell boundaries",
)

print("Applied residual scope/config stage C")
