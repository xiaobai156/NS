from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8")


def repl(text, old, new, label, expected=1):
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f"{label}: expected {expected}, found {count}")
    return text.replace(old, new, expected)

# ---------------------------------------------------------------------------
# RuleEngine: explicit extraction conflict + peer author/section boundaries.
# ---------------------------------------------------------------------------
path = "OcrLineTool.App/RuleEngine.cs"
text = read(path)
text = repl(text,
'''    bool AllowValueWithoutKeyword = false,
    bool StrictIssueBlock = false,
    bool SingleValuePerIssue = false)''',
'''    bool AllowValueWithoutKeyword = false,
    bool StrictIssueBlock = false,
    bool SingleValuePerIssue = false,
    IReadOnlyList<string>? PeerKeywords = null)''',
"peer keyword rule property")
text = repl(text,
'''    public string OutputLabel => Label ?? Keyword;
}

public static class RuleEngine''',
'''    public string OutputLabel => Label ?? Keyword;
}

public enum RuleExtractionStatus
{
    Missing,
    Success,
    Conflict
}

public sealed record RuleExtractionResult(RuleExtractionStatus Status, string? Value)
{
    public static RuleExtractionResult Missing { get; } = new(RuleExtractionStatus.Missing, null);
    public static RuleExtractionResult Conflict { get; } = new(RuleExtractionStatus.Conflict, null);
    public static RuleExtractionResult Success(string value) => new(RuleExtractionStatus.Success, value);
}

public static class RuleEngine''',
"extraction result types")
text = repl(text,
'''    private const string Zodiac = "马蛇龙兔虎牛鼠猪狗鸡猴羊";''',
'''    private const string Zodiac = "马蛇龙兔虎牛鼠猪狗鸡猴羊";
    private const string ConflictMarker = "\u001dOCR-CONFLICT\u001d";''',
"conflict marker")

# Scope a selected author/section before building concatenated candidates.
old = '''            candidates.Add(line);
            string combined = line;
            for (int next = index + 1; next < lines.Length && next <= index + 4; next++)
            {
                if (OcrLayoutMarkers.IsBoundary(lines[next])
                    || ContainsIssueBoundary(lines[next], issue)
                    || combined.Length + lines[next].Length >= 180)
                    break;
                combined += " " + lines[next];
                candidates.Add(combined);
            }'''
new = '''            string scopedLine = ScopeCandidateToPeerBoundary(line, rule, aliases, out bool peerClosed);
            candidates.Add(scopedLine);
            if (peerClosed)
                continue;
            string combined = scopedLine;
            for (int next = index + 1; next < lines.Length && next <= index + 4; next++)
            {
                if (OcrLayoutMarkers.IsBoundary(lines[next])
                    || ContainsIssueBoundary(lines[next], issue)
                    || ContainsPeerIdentity(lines[next], rule)
                    || combined.Length + lines[next].Length >= 180)
                    break;
                combined += " " + lines[next];
                candidates.Add(combined);
            }'''
text = repl(text, old, new, "peer bounded candidates")

anchor = '''    private static IEnumerable<string> MaximalCandidates(IEnumerable<string> source)
    {
        string[] values = source.Distinct(StringComparer.Ordinal).ToArray();
        return values.Where(candidate => !values.Any(other =>
            other.Length > candidate.Length
            && other.StartsWith(candidate + " ", StringComparison.Ordinal)));
    }
'''
helpers = anchor + '''
    private static string ScopeCandidateToPeerBoundary(
        string line, OcrRule rule, IReadOnlyList<string> aliases, out bool closed)
    {
        closed = false;
        if (rule.PeerKeywords is not { Count: > 0 })
            return line;
        string normalized = Normalize(line);
        (int Index, int Length)[] own = aliases
            .Select(alias => (Index: normalized.IndexOf(alias, StringComparison.Ordinal), Length: alias.Length))
            .Where(item => item.Index >= 0)
            .ToArray();
        if (own.Length == 0)
            return line;
        int ownIndex = own.Min(item => item.Index);
        int ownEnd = own.Where(item => item.Index == ownIndex).Max(item => item.Index + item.Length);
        int peerIndex = rule.PeerKeywords
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Select(peer => normalized.IndexOf(peer, ownEnd, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (peerIndex < 0)
            return line;
        closed = true;
        return normalized[..peerIndex];
    }

    private static bool ContainsPeerIdentity(string line, OcrRule rule)
    {
        if (rule.PeerKeywords is not { Count: > 0 })
            return false;
        string normalized = Normalize(line);
        return rule.PeerKeywords
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Select(Normalize)
            .Any(peer => normalized.Contains(peer, StringComparison.Ordinal));
    }

    private static string TrimAtPeerBoundary(string normalizedTail, OcrRule rule)
    {
        if (rule.PeerKeywords is not { Count: > 0 })
            return normalizedTail;
        int peer = rule.PeerKeywords
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Select(value => normalizedTail.IndexOf(value, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        return peer >= 0 ? normalizedTail[..peer] : normalizedTail;
    }
'''
text = repl(text, anchor, helpers, "peer boundary helpers")

text = repl(text,
'''                string tail = line[(aliasIndex + aliasText.Length)..];
                int forbiddenIndex = tail.IndexOf('禁');''',
'''                string tail = TrimAtPeerBoundary(line[(aliasIndex + aliasText.Length)..], rule);
                int forbiddenIndex = tail.IndexOf('禁');''',
"summary peer boundary")

# Explicit conflict markers in all current multi-value aggregators.
text = text.replace(
    '''return nineValues.Count == 1 ? nineValues.Single() : null;''',
    '''return nineValues.Count == 0 ? null : nineValues.Count == 1 ? nineValues.Single() : ConflictMarker;''')
text = text.replace(
    '''return bracketValues.Count == 1 ? bracketValues.Single() : null;''',
    '''return bracketValues.Count == 0 ? null : bracketValues.Count == 1 ? bracketValues.Single() : ConflictMarker;''')
text = text.replace(
    '''return splitObserved.Count == 1 ? splitObserved.Single() : null;''',
    '''return splitObserved.Count == 0 ? null : splitObserved.Count == 1 ? splitObserved.Single() : ConflictMarker;''')
text = text.replace(
    '''return reviewed.Count == 1 ? reviewed.Single() : null;''',
    '''return reviewed.Count == 0 ? null : reviewed.Count == 1 ? reviewed.Single() : ConflictMarker;''')
text = text.replace(
    '''return standaloneValues.Count == 1 ? standaloneValues.Single() : null;''',
    '''return standaloneValues.Count == 0 ? null : standaloneValues.Count == 1 ? standaloneValues.Single() : ConflictMarker;''')
text = text.replace(
    '''return foundIssue && heads.Count == 1 ? $"{heads.Single()}头" : null;''',
    '''return !foundIssue || heads.Count == 0 ? null : heads.Count == 1 ? $"{heads.Single()}头" : ConflictMarker;''')
# HashSet<string> aggregators: strict blocks, summaries, nearby and number rows.
text = text.replace(
    '''return values.Count == 1 ? values.Single() : null;''',
    '''return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;''')
text = text.replace(
    '''return observed.Count == 1 ? observed.Single() : null;''',
    '''return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;''')
text = repl(text,
'''        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : null;''',
'''        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : ConflictMarker;''',
"generic observed conflict")
text = repl(text,
'''        return values.Count == 1 ? values[0].ToString() : null;''',
'''        return values.Count == 0 ? null : values.Count == 1 ? values[0].ToString() : ConflictMarker;''',
"vertical conflict")

# Public extraction result keeps Conflict distinct while string API remains compatible.
text = repl(text,
'''    public static string? ExtractValue(IEnumerable<string> cloudLines, int issue, OcrRule rule)
        => ExtractValueCore(cloudLines, issue, rule, requireCloudKeyword: true);''',
'''    public static string? ExtractValue(IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        string? raw = ExtractValueCore(cloudLines, issue, rule, requireCloudKeyword: true);
        return raw == ConflictMarker ? null : raw;
    }''',
"public value strips conflict marker")

start = text.index('    public static string? ExtractFinalValue(IEnumerable<string> cloudLines, int issue, OcrRule rule) =>')
end = text.index('    private static IEnumerable<string> RejectCrossIssueNearbyValue', start)
replacement = '''    public static RuleExtractionResult ExtractFinalResult(
        IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        string? raw = ExtractValueCore(
            RejectCrossIssueNearbyValue(cloudLines, issue, rule), issue, rule, requireCloudKeyword: false);
        return ToExtractionResult(raw);
    }

    public static string? ExtractFinalValue(IEnumerable<string> cloudLines, int issue, OcrRule rule) =>
        ExtractFinalResult(cloudLines, issue, rule).Value;

    public static RuleExtractionResult ExtractFinalResult(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        bool conflict = false;
        foreach (IGrouping<string, OcrLineEvidence> region in evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId + "\\u001f" + item.RegionId, StringComparer.Ordinal))
        {
            OcrLineEvidence[] items = region.ToArray();
            RuleExtractionResult regional;
            if (items.All(item => item.Box is not null))
            {
                regional = ExtractFinalResult(items.Select(item => item.Text), issue, rule);
            }
            else
            {
                RuleExtractionResult holistic = ExtractFinalResult(items.Select(item => item.Text), issue, rule);
                if (holistic.Status == RuleExtractionStatus.Conflict)
                {
                    conflict = true;
                    continue;
                }
                var atomic = new HashSet<string>(StringComparer.Ordinal);
                foreach (OcrLineEvidence item in items)
                {
                    RuleExtractionResult itemResult = ExtractFinalResult(new[] { item.Text }, issue, rule);
                    if (itemResult.Status == RuleExtractionStatus.Conflict)
                        conflict = true;
                    else if (itemResult.Status == RuleExtractionStatus.Success)
                        atomic.Add(itemResult.Value!);
                }
                if (conflict || atomic.Count > 1)
                {
                    conflict = true;
                    continue;
                }
                regional = holistic.Status == RuleExtractionStatus.Success
                    && atomic.Count == 1 && atomic.Contains(holistic.Value!)
                    ? holistic
                    : RuleExtractionResult.Missing;
            }
            if (regional.Status == RuleExtractionStatus.Conflict)
                conflict = true;
            else if (regional.Status == RuleExtractionStatus.Success)
                observed.Add(regional.Value!);
        }
        if (conflict || observed.Count > 1)
            return RuleExtractionResult.Conflict;
        return observed.Count == 1
            ? RuleExtractionResult.Success(observed.Single())
            : RuleExtractionResult.Missing;
    }

    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule) =>
        ExtractFinalResult(evidence, issue, rule).Value;

    private static RuleExtractionResult ToExtractionResult(string? raw) =>
        raw == ConflictMarker
            ? RuleExtractionResult.Conflict
            : raw is null
                ? RuleExtractionResult.Missing
                : RuleExtractionResult.Success(raw);

'''
text = text[:start] + replacement + text[end:]
write(path, text)

# ---------------------------------------------------------------------------
# RuleCatalog: invalid booleans fail closed + inject same-folder peer identities.
# ---------------------------------------------------------------------------
path = "OcrLineTool.App/RuleCatalog.cs"
text = read(path)
text = repl(text,
'''            var output = new List<OcrRule>();
            bool strictIssueBlock = document.RootElement.TryGetProperty("strictIssueBlock", out JsonElement strictElement)
                && strictElement.ValueKind == JsonValueKind.True;''',
'''            var output = new List<OcrRule>();
            bool strictIssueBlock = ReadOptionalBoolean(
                document.RootElement, "strictIssueBlock", false, fileName);''',
"root strict boolean")
text = repl(text,
'''                bool ignoreIssue = item.TryGetProperty("ignoreIssue", out JsonElement ignoreIssueElement)
                    && ignoreIssueElement.ValueKind == JsonValueKind.True;
                bool allowNearbyValue = item.TryGetProperty("allowNearbyValue", out JsonElement nearbyElement)
                    && nearbyElement.ValueKind == JsonValueKind.True;
                bool allowValueWithoutKeyword = item.TryGetProperty("allowValueWithoutKeyword", out JsonElement withoutKeywordElement)
                    && withoutKeywordElement.ValueKind == JsonValueKind.True;
                bool singleValuePerIssue = item.TryGetProperty("singleValuePerIssue", out JsonElement singleValueElement)
                    && singleValueElement.ValueKind == JsonValueKind.True;
                bool itemStrictIssueBlock = item.TryGetProperty("strictIssueBlock", out JsonElement itemStrictElement)
                    ? itemStrictElement.ValueKind == JsonValueKind.True
                    : strictIssueBlock;''',
'''                bool ignoreIssue = ReadOptionalBoolean(item, "ignoreIssue", false, fileName);
                bool allowNearbyValue = ReadOptionalBoolean(item, "allowNearbyValue", false, fileName);
                bool allowValueWithoutKeyword = ReadOptionalBoolean(item, "allowValueWithoutKeyword", false, fileName);
                bool singleValuePerIssue = ReadOptionalBoolean(item, "singleValuePerIssue", false, fileName);
                bool itemStrictIssueBlock = ReadOptionalBoolean(item, "strictIssueBlock", strictIssueBlock, fileName);''',
"item boolean validation")
text = repl(text,
'''            if (output.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != output.Count)
                throw new OcrException($"{fileName} 中存在重复的输出名称。");
            return output;''',
'''            if (output.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != output.Count)
                throw new OcrException($"{fileName} 中存在重复的输出名称。");

            OcrRule[] enriched = output.Select(rule =>
            {
                if (string.IsNullOrWhiteSpace(rule.Folder))
                    return rule;
                HashSet<string> own = new[] { rule.Keyword, rule.Label ?? string.Empty }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.Ordinal);
                string[] peers = output
                    .Where(other => other.Id != rule.Id
                        && string.Equals(other.Folder, rule.Folder, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(other => new[] { other.Keyword, other.Label ?? string.Empty })
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !own.Contains(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return rule with { PeerKeywords = peers };
            }).ToArray();
            return enriched;''',
"peer identity enrichment")
text = repl(text,
'''    }
}''',
'''    }

    private static bool ReadOptionalBoolean(
        JsonElement element, string propertyName, bool fallback, string fileName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new OcrException($"{fileName} 的 {propertyName} 必须是 true 或 false。")
        };
    }
}''',
"boolean helper")
write(path, text)

# ---------------------------------------------------------------------------
# ResultValues + ledger + MainForm: make explicit conflicts sticky end-to-end.
# ---------------------------------------------------------------------------
path = "OcrLineTool.App/ResultValues.cs"
text = read(path)
text = repl(text,
'''    internal static bool IsConflict(IReadOnlyDictionary<string, string> values, string id) =>
        values is ResultValues guarded && guarded.Conflicts.Contains(id);''',
'''    internal static void MarkConflict(IDictionary<string, string> values, string id)
    {
        values.Remove(id);
        if (values is ResultValues guarded)
            guarded.Conflicts.Add(id);
    }

    internal static bool IsConflict(IReadOnlyDictionary<string, string> values, string id) =>
        values is ResultValues guarded && guarded.Conflicts.Contains(id);''',
"explicit result conflict")
write(path, text)

path = "OcrLineTool.App/RecognitionStateStore.cs"
text = read(path)
text = repl(text,
'''    internal void Seed(ResultEvidenceRecord record) => records[record.RuleId] = record;''',
'''    internal void ObserveConflict(ResultValues values, OcrRule rule, OcrEvidence evidence)
    {
        ResultValues.MarkConflict(values, rule.Id);
        records[rule.Id] = BuildRecord(rule, string.Empty, "conflict", evidence);
    }

    internal void Seed(ResultEvidenceRecord record) => records[record.RuleId] = record;''',
"ledger explicit conflict")
write(path, text)

path = "OcrLineTool.App/MainForm.cs"
text = read(path)
text = repl(text,
'''    private static void AddExtractedValues(
        IReadOnlyList<string> lines,
        IEnumerable<OcrRule> rules,
        int issue,
        IDictionary<string, string> values)
    {
        foreach (OcrRule rule in rules)
        {
            string? value = RuleEngine.ExtractFinalValue(lines, issue, rule);
            if (value is not null)
                ResultValues.AddTo(values, rule.Id, value);
        }
    }

    private static void AddExtractedEvidenceValues(
        OcrEvidence evidence,
        IEnumerable<OcrRule> rules,
        int issue,
        ResultValues values,
        ResultEvidenceLedger ledger)
    {
        foreach (OcrRule rule in rules)
        {
            string? value = RuleEngine.ExtractFinalValue(evidence, issue, rule);
            if (value is not null)
                ledger.Observe(values, rule, value, evidence);
        }
    }''',
'''    private static void AddExtractedValues(
        IReadOnlyList<string> lines,
        IEnumerable<OcrRule> rules,
        int issue,
        IDictionary<string, string> values)
    {
        foreach (OcrRule rule in rules)
        {
            RuleExtractionResult result = RuleEngine.ExtractFinalResult(lines, issue, rule);
            if (result.Status == RuleExtractionStatus.Conflict)
                ResultValues.MarkConflict(values, rule.Id);
            else if (result.Status == RuleExtractionStatus.Success)
                ResultValues.AddTo(values, rule.Id, result.Value!);
        }
    }

    internal static void AddExtractedEvidenceValues(
        OcrEvidence evidence,
        IEnumerable<OcrRule> rules,
        int issue,
        ResultValues values,
        ResultEvidenceLedger ledger)
    {
        foreach (OcrRule rule in rules)
        {
            RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, issue, rule);
            if (result.Status == RuleExtractionStatus.Conflict)
                ledger.ObserveConflict(values, rule, evidence);
            else if (result.Status == RuleExtractionStatus.Success)
                ledger.Observe(values, rule, result.Value!, evidence);
        }
    }''',
"propagate extraction conflicts")
write(path, text)

print("Applied residual contract stage C")
