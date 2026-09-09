from pathlib import Path

files = {
    name: Path(name).read_text(encoding='utf-8')
    for name in [
        'OcrLineTool.App/RuleEngine.cs',
        'OcrLineTool.App/LocalCandidatePlanner.cs',
        'OcrLineTool.App/MainForm.cs',
        'OcrLineTool.App/VisualTemplateMatcher.cs',
        'OcrLineTool.App/OcrClients.cs',
        'OcrLineTool.App/ResultDistributor.cs',
        'OcrLineTool.App/OwnedResultWriter.cs',
    ]
}


def repl(name: str, old: str, new: str) -> None:
    text = files[name]
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{name}: expected one anchor, found {count}: {old[:150]!r}')
    files[name] = text.replace(old, new, 1)

# ---------------------------------------------------------------------------
# Candidate identity: a retry subset is never evidence that a shared folder is
# globally exclusive. Callers with the complete catalog can still use the
# reviewed folder-only fallback for genuinely single-rule folders.
# ---------------------------------------------------------------------------
name = 'OcrLineTool.App/RuleEngine.cs'
repl(name,
'''    public static IReadOnlyList<OcrRule> FindMatches(
        string imagePath,
        IEnumerable<string> localLines,
        IEnumerable<OcrRule> rules)
    {
        string[] lines = localLines.ToArray();
        string text = Normalize(string.Concat(lines));
        string folder = Path.GetFileName(Path.GetDirectoryName(imagePath)) ?? "";
        return rules.Where(rule =>''',
'''    public static IReadOnlyList<OcrRule> FindMatches(
        string imagePath,
        IEnumerable<string> localLines,
        IEnumerable<OcrRule> rules,
        IEnumerable<OcrRule>? completeRules = null)
    {
        string[] lines = localLines.ToArray();
        OcrRule[] requestedRules = rules.ToArray();
        OcrRule[] identityRules = completeRules?.ToArray() ?? [];
        bool hasCompleteIdentityCatalog = completeRules is not null;
        string text = Normalize(string.Concat(lines));
        string folder = Path.GetFileName(Path.GetDirectoryName(imagePath)) ?? "";
        return requestedRules.Where(rule =>''')
repl(name,
'''                    && (rules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
                        && Normalize(other.RequiredKeyword ?? other.Keyword) == Normalize(rule.RequiredKeyword)) == 1
                        || !string.IsNullOrWhiteSpace(rule.Section) && text.Contains(Normalize(rule.Section), StringComparison.Ordinal)))
                || (rules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)) == 1
                    && expectedFolder.Equals(rule.RequiredKeyword ?? rule.Keyword, StringComparison.OrdinalIgnoreCase));''',
'''                    && (hasCompleteIdentityCatalog
                        && identityRules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
                            && Normalize(other.RequiredKeyword ?? other.Keyword) == Normalize(rule.RequiredKeyword)) == 1
                        || !string.IsNullOrWhiteSpace(rule.Section) && text.Contains(Normalize(rule.Section), StringComparison.Ordinal)))
                || (hasCompleteIdentityCatalog
                    && identityRules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)) == 1
                    && expectedFolder.Equals(rule.RequiredKeyword ?? rule.Keyword, StringComparison.OrdinalIgnoreCase));''')

# Output-file validation is a hard boundary: saved/manual text must satisfy the
# same final value shape before it can be distributed.
repl(name,
'''    private static string FormatForOutput(OcrRule rule, string value)
    {''',
'''    public static bool IsFormattedOutputValueValid(OcrRule rule, string value)
    {
        value = SimplifyOcrText(value.Trim());
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int count))
        {
            string[] numbers = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return numbers.Length == count && numbers.Distinct(StringComparer.Ordinal).Count() == count
                && numbers.All(number => number.Length == 2 && int.TryParse(number, out int parsed)
                    && parsed is >= 1 and <= 49);
        }
        if (rule.Type is "生肖" or "单生肖")
            return value.Length == 1 && Zodiac.Contains(value[0]);
        if (rule.Type == "生肖组合")
            return value.Length == 2 && value.All(Zodiac.Contains) && value.Distinct().Count() == 2;
        if (rule.Type == "九肖")
            return value.Length == 9 && value.All(Zodiac.Contains) && value.Distinct().Count() == 9;
        if (rule.Type == "统计生肖")
            return value.Length > 0 && value.Length <= Zodiac.Length && value.All(Zodiac.Contains)
                && value.Distinct().Count() == value.Length;
        if (rule.Type == "头")
            return Regex.IsMatch(value, "^[0-4]头$");
        if (rule.Type == "尾")
        {
            if (rule.Id is "亚太尾" or "战澳尾")
            {
                string[] tails = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return tails.Length == 2 && tails.Distinct(StringComparer.Ordinal).Count() == 2
                    && tails.All(item => Regex.IsMatch(item, "^[0-9]尾$"));
            }
            return Regex.IsMatch(value, "^[0-9]尾$");
        }
        if (rule.Type == "尾数组合")
        {
            string[] tails = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tails.Length == 2 && tails.Distinct(StringComparer.Ordinal).Count() == 2
                && tails.All(item => Regex.IsMatch(item, "^[0-9]尾$"));
        }
        if (rule.Type == "头数组合")
        {
            string[] heads = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return heads.Length > 0 && heads.Length <= 5 && heads.Distinct(StringComparer.Ordinal).Count() == heads.Length
                && heads.All(item => Regex.IsMatch(item, "^[0-4]头$"));
        }
        if (rule.Type == "缺尾") return Regex.IsMatch(value, "^[0-9]尾$");
        if (rule.Type == "缺头") return Regex.IsMatch(value, "^[0-4]头$");
        if (rule.Type is "五行" or "单五行") return value.Length == 1 && "金木水火土".Contains(value[0]);
        if (rule.Type == "色单双") return Regex.IsMatch(value, "^[红蓝绿][单双]$");
        if (rule.Type == "合") return Regex.IsMatch(value, "^(?:0[1-9]|1[0-3])合$");
        if (rule.Type == "段") return Regex.IsMatch(value, "^[0-7]段$");
        if (rule.Type == "半头") return Regex.IsMatch(value, "^[0-4]头[单双]$");
        return false;
    }

    private static string FormatForOutput(OcrRule rule, string value)
    {''')

# OCR horizontal-region separators must terminate generic cross-line evidence.
repl(name,
'''                if (ContainsIssueBoundary(lines[next], issue) || combined.Length + lines[next].Length >= 180)
                    break;''',
'''                if (OcrLayoutMarkers.IsBoundary(lines[next])
                    || ContainsIssueBoundary(lines[next], issue)
                    || combined.Length + lines[next].Length >= 180)
                    break;''')

# ---------------------------------------------------------------------------
# Local candidate planner + MainForm pass the complete catalog as the identity
# universe, even when only missing rules are being retried.
# ---------------------------------------------------------------------------
name = 'OcrLineTool.App/LocalCandidatePlanner.cs'
repl(name,
'''        IReadOnlyDictionary<string, IReadOnlyList<string>> localResults,
        IReadOnlyList<OcrRule> rules, int? issue = null)
    {''',
'''        IReadOnlyDictionary<string, IReadOnlyList<string>> localResults,
        IReadOnlyList<OcrRule> rules, int? issue = null,
        IReadOnlyList<OcrRule>? completeRules = null)
    {''')
repl(name,
'''            IReadOnlyList<OcrRule> matchedRules = RuleEngine.FindMatches(path, lines, rules);''',
'''            IReadOnlyList<OcrRule> matchedRules = RuleEngine.FindMatches(path, lines, rules, completeRules);''')

name = 'OcrLineTool.App/MainForm.cs'
repl(name,
'''        var localCandidates = new List<RecognitionCandidate>();
        bool isYanran = RuleCatalog.IsGroupFolder(selectedImageDirectory!, "嫣然心水");''',
'''        var localCandidates = new List<RecognitionCandidate>();
        IReadOnlyList<OcrRule> completeIdentityRules = selectedRulePath is null
            ? rules
            : RuleCatalog.Load(selectedRulePath);
        bool isYanran = RuleCatalog.IsGroupFolder(selectedImageDirectory!, "嫣然心水");''')
repl(name,
'''            foreach (LocalCandidatePlan plan in LocalCandidatePlanner.Build(imagePaths, localResults, rules, issue))''',
'''            foreach (LocalCandidatePlan plan in LocalCandidatePlanner.Build(
                imagePaths, localResults, rules, issue, completeIdentityRules))''')
repl(name,
'''            IReadOnlyList<OcrRule> matched = RuleEngine.FindMatches(path, lines, rules);''',
'''            IReadOnlyList<OcrRule> matched = RuleEngine.FindMatches(path, lines, rules, completeIdentityRules);''')

# ---------------------------------------------------------------------------
# Template assignment: independent templates cannot claim the same physical
# image. A single explicit shared template can still cover several RuleIds.
# ---------------------------------------------------------------------------
name = 'OcrLineTool.App/VisualTemplateMatcher.cs'
repl(name,
'''        if (matches.Select(match => match.Template.Id).Distinct(StringComparer.Ordinal).Count() != matches.Count)
            return false;

        var coveredRuleIds = new HashSet<string>(StringComparer.Ordinal);''',
'''        if (matches.Select(match => match.Template.Id).Distinct(StringComparer.Ordinal).Count() != matches.Count)
            return false;
        if (matches.GroupBy(match => match.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Select(match => match.Template.Id).Distinct(StringComparer.Ordinal).Count() > 1))
            return false;

        var coveredRuleIds = new HashSet<string>(StringComparer.Ordinal);''')
repl(name,
'''                if (ordered.Length > 1 &&
                    ordered[1].Distance - ordered[0].Distance < minimumDistanceMargin)
                    return [];
                return ordered.Take(1);''',
'''                if (ordered.Length > 1 &&
                    ordered[1].Distance - ordered[0].Distance < minimumDistanceMargin)
                    return [];
                return ordered;''')
repl(name,
'''        var usedTemplates = new HashSet<int>();
        var matches = new List<VisualTemplateMatch>();''',
'''        var usedTemplates = new HashSet<int>();
        var usedImages = new HashSet<int>();
        var matches = new List<VisualTemplateMatch>();''')
repl(name,
'''        {
            if (!usedTemplates.Add(score.TemplateIndex))
                continue;
            matches.Add(new VisualTemplateMatch(''',
'''        {
            if (usedTemplates.Contains(score.TemplateIndex) || usedImages.Contains(score.ImageIndex))
                continue;
            usedTemplates.Add(score.TemplateIndex);
            usedImages.Add(score.ImageIndex);
            matches.Add(new VisualTemplateMatch(''')

# ---------------------------------------------------------------------------
# Tencent spatial assembly: distant same-Y columns are separate OCR regions.
# A boundary marker prevents downstream rules from combining them again.
# ---------------------------------------------------------------------------
name = 'OcrLineTool.App/OcrClients.cs'
repl(name,
'''namespace OcrLineTool;

public sealed class OcrException''',
'''namespace OcrLineTool;

internal static class OcrLayoutMarkers
{
    internal const string RegionBoundary = "\\u001e";
    internal static bool IsBoundary(string line) => line == RegionBoundary;
}

public sealed class OcrException''')
repl(name,
'''    internal static async Task<byte[]> ReadImageAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {''',
'''    internal static async Task<byte[]> ReadImageAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {''')
# Add stable-image verifier after ReadImageAsync method.
anchor = '''        return await File.ReadAllBytesAsync(path, cancellationToken);
    }
}

public sealed class TencentOcrClient'''
replacement = '''        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    internal static void EnsureImageUnchanged(string path, byte[] sentBytes)
    {
        try
        {
            byte[] current = File.ReadAllBytes(path);
            if (!current.AsSpan().SequenceEqual(sentBytes))
                throw new OcrException("图片在云 OCR 请求期间发生变化，请重新识别。", "OCR_IMAGE_CHANGED");
        }
        catch (OcrException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("图片在云 OCR 请求期间不可用，请重新识别。", "OCR_IMAGE_CHANGED");
        }
    }
}

public sealed class TencentOcrClient'''
repl(name, anchor, replacement)
repl(name,
'''            if (!response.IsSuccessStatusCode)
                throw new OcrException($"腾讯云请求失败（HTTP {(int)response.StatusCode}）。");
            return ParseLines(json);''',
'''            if (!response.IsSuccessStatusCode)
                throw new OcrException($"腾讯云请求失败（HTTP {(int)response.StatusCode}）。");
            IReadOnlyList<string> lines = ParseLines(json);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return lines;''')
repl(name,
'''    private sealed record TextPiece(string Text, int X, int Y, int Height, int Index, bool HasPosition)
    {
        public double CenterY => Y + Height / 2d;
    }''',
'''    private sealed record TextPiece(string Text, int X, int Y, int Width, int Height, int Index, bool HasPosition)
    {
        public double CenterY => Y + Height / 2d;
        public int Right => X + Math.Max(0, Width);
    }''')
repl(name,
'''        if (!item.TryGetProperty("ItemPolygon", out JsonElement polygon))
            return new(text, 0, 0, 0, index, false);
        return new(
            text,
            polygon.GetProperty("X").GetInt32(),
            polygon.GetProperty("Y").GetInt32(),
            polygon.GetProperty("Height").GetInt32(),
            index,
            true);''',
'''        if (!item.TryGetProperty("ItemPolygon", out JsonElement polygon))
            return new(text, 0, 0, 0, 0, index, false);
        return new(
            text,
            polygon.GetProperty("X").GetInt32(),
            polygon.GetProperty("Y").GetInt32(),
            polygon.TryGetProperty("Width", out JsonElement width) ? width.GetInt32() : 0,
            polygon.GetProperty("Height").GetInt32(),
            index,
            true);''')
repl(name,
'''        return rows
            .OrderBy(row => row.Average(piece => piece.CenterY))
            .Select(row => string.Concat(row.OrderBy(piece => piece.X).Select(piece => piece.Text)))
            .ToArray();
    }
}''',
'''        return rows
            .OrderBy(row => row.Average(piece => piece.CenterY))
            .SelectMany(AssembleRowSegments)
            .ToArray();
    }

    private static IEnumerable<string> AssembleRowSegments(List<TextPiece> row)
    {
        TextPiece[] ordered = row.OrderBy(piece => piece.X).ToArray();
        var segment = new List<TextPiece>();
        int previousRight = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            TextPiece piece = ordered[index];
            if (segment.Count > 0)
            {
                TextPiece previous = segment[^1];
                int gap = piece.X - previousRight;
                int splitGap = Math.Max(48, Math.Max(previous.Height, piece.Height) * 4);
                if (gap > splitGap)
                {
                    yield return string.Concat(segment.Select(item => item.Text));
                    yield return OcrLayoutMarkers.RegionBoundary;
                    segment.Clear();
                }
            }
            segment.Add(piece);
            previousRight = Math.Max(previousRight, piece.Right);
        }
        if (segment.Count > 0)
            yield return string.Concat(segment.Select(item => item.Text));
    }
}''')
# Baidu must also reject a path changed while its request was in flight.
repl(name,
'''            if (!response.IsSuccessStatusCode)
                throw new OcrException($"百度 OCR 请求失败（HTTP {(int)response.StatusCode}）。");
            return ParseLines(json);''',
'''            if (!response.IsSuccessStatusCode)
                throw new OcrException($"百度 OCR 请求失败（HTTP {(int)response.StatusCode}）。");
            IReadOnlyList<string> lines = ParseLines(json);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return lines;''')

# ---------------------------------------------------------------------------
# Distribution boundary: validate every successful line by OCR rule type and
# explicitly revoke only this tool's previously-owned row when the new result
# is an evidence-backed same-issue conflict.
# ---------------------------------------------------------------------------
name = 'OcrLineTool.App/ResultDistributor.cs'
repl(name,
'''        var labels = new HashSet<string>(rule.Labels, StringComparer.Ordinal);
        string[] configuredLines = outputLines
            .Select(line => line.EndsWith("（已分流）", StringComparison.Ordinal) ? line[..^5] : line)
            .Where(line => IsConfiguredLine(line, labels, rule.NumberCounts))
            .ToArray();
        if (configuredLines.Length == 0)
            return EmptyResult;''',
'''        var labels = new HashSet<string>(rule.Labels, StringComparer.Ordinal);
        IReadOnlyDictionary<string, OcrRule> rulesByLabel = RuleCatalog.Load(
                RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedDirectory))
            .Where(item => labels.Contains(item.OutputLabel))
            .ToDictionary(item => item.OutputLabel, StringComparer.Ordinal);
        string[] normalizedLines = outputLines
            .Select(line => line.EndsWith("（已分流）", StringComparison.Ordinal) ? line[..^5] : line)
            .ToArray();
        string[] configuredLines = normalizedLines
            .Where(line => IsConfiguredLine(line, labels, rule.NumberCounts, rulesByLabel))
            .ToArray();
        HashSet<string> revokedLabels = normalizedLines
            .Where(line => line.StartsWith("缺失（同一期结果冲突", StringComparison.Ordinal))
            .Select(line => labels.OrderByDescending(label => label.Length)
                .FirstOrDefault(label => line.EndsWith(" " + label, StringComparison.Ordinal)))
            .Where(label => label is not null)
            .Select(label => label!)
            .ToHashSet(StringComparer.Ordinal);
        if (configuredLines.Length == 0 && revokedLabels.Count == 0)
            return EmptyResult;''')
repl(name,
'''        return await OwnedResultWriter.ApplyAsync(targetPath, sourceGroup!, configuredLines,
            marker, config.Placement?.BlankLineBeforeMarker == true);''',
'''        return await OwnedResultWriter.ApplyAsync(targetPath, sourceGroup!, configuredLines,
            revokedLabels, marker, config.Placement?.BlankLineBeforeMarker == true);''')
repl(name,
'''    private static bool IsConfiguredLine(
        string line,
        HashSet<string> labels,
        IReadOnlyDictionary<string, int>? numberCounts)
    {''',
'''    private static bool IsConfiguredLine(
        string line,
        HashSet<string> labels,
        IReadOnlyDictionary<string, int>? numberCounts,
        IReadOnlyDictionary<string, OcrRule> rulesByLabel)
    {''')
repl(name,
'''        string label = line[(separator + 1)..];
        if (!labels.Contains(label))
            return false;
        if (numberCounts?.TryGetValue(label, out int expectedCount) != true)
            return true;

        string[] numbers = line[..separator]
            .Split(',', StringSplitOptions.TrimEntries);
        return numbers.Length == expectedCount && numbers.Distinct(StringComparer.Ordinal).Count() == expectedCount
            && numbers.All(number => number.Length == 2 && number.All(c => c is >= '0' and <= '9')
                && int.TryParse(number, out int value) && value is >= 1 and <= 49);''',
'''        string label = line[(separator + 1)..];
        if (!labels.Contains(label) || !rulesByLabel.TryGetValue(label, out OcrRule? ocrRule))
            return false;
        string valueText = line[..separator].Trim();
        if (!RuleEngine.IsFormattedOutputValueValid(ocrRule, valueText))
            return false;
        if (numberCounts?.TryGetValue(label, out int expectedCount) != true)
            return true;

        string[] numbers = valueText.Split(',', StringSplitOptions.TrimEntries);
        return numbers.Length == expectedCount && numbers.Distinct(StringComparer.Ordinal).Count() == expectedCount
            && numbers.All(number => number.Length == 2 && number.All(c => c is >= '0' and <= '9')
                && int.TryParse(number, out int value) && value is >= 1 and <= 49);''')

name = 'OcrLineTool.App/OwnedResultWriter.cs'
repl(name,
'''    internal static async Task<IReadOnlySet<string>> ApplyAsync(string targetPath, string sourceGroup,
        string[] configuredLines, string? marker, bool blankLineBeforeMarker)''',
'''    internal static async Task<IReadOnlySet<string>> ApplyAsync(string targetPath, string sourceGroup,
        string[] configuredLines, IReadOnlySet<string> revokedLabels, string? marker, bool blankLineBeforeMarker)''')
repl(name,
'''            var pending = new List<string>();
            bool modified = false;
            foreach (IGrouping<string, string> group in configuredLines.GroupBy(Label, StringComparer.Ordinal))''',
'''            var pending = new List<string>();
            bool modified = false;
            foreach (string label in revokedLabels)
            {
                OwnedLine[] matchingOwners = owners
                    .Where(item => item.SourceGroup == sourceGroup && item.Label == label)
                    .ToArray();
                if (matchingOwners.Length > 1)
                    throw new OcrException("分流归属记录重复，未修改目标文件。");
                OwnedLine? owned = matchingOwners.SingleOrDefault();
                if (owned is null)
                    continue;
                int[] sameLabel = Enumerable.Range(0, rows.Count)
                    .Where(index => Label(rows[index]) == label).ToArray();
                if (sameLabel.Length == 0)
                {
                    owners.Remove(owned);
                    modified = true;
                    continue;
                }
                if (sameLabel.Length == 1 && rows[sameLabel[0]] == owned.Line
                    && !owners.Any(item => item.SourceGroup != sourceGroup && item.Label == label))
                {
                    rows.RemoveAt(sameLabel[0]);
                    owners.Remove(owned);
                    modified = true;
                    continue;
                }
                throw new OcrException($"分流冲突需撤销旧值，但目标归属已变化：{label}。保留原文件，请核对。");
            }
            foreach (IGrouping<string, string> group in configuredLines.GroupBy(Label, StringComparer.Ordinal))''')

for name, text in files.items():
    Path(name).write_text(text, encoding='utf-8')
print('Applied P2 source/downstream integrity patch')
