from pathlib import Path

main_path = Path('OcrLineTool.App/MainForm.cs')
state_path = Path('OcrLineTool.App/RecognitionStateStore.cs')
rule_path = Path('OcrLineTool.App/RuleEngine.cs')
main = main_path.read_text(encoding='utf-8')
state = state_path.read_text(encoding='utf-8')
rule = rule_path.read_text(encoding='utf-8')

def repl(text: str, old: str, new: str, label: str, expected: int = 1) -> str:
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f'{label}: expected {expected} anchors, found {count}: {old[:180]!r}')
    return text.replace(old, new, expected)

# ---------------------------------------------------------------------------
# F13 final closure: if a cloud request is needed for any rule on a candidate,
# compare that already-paid response against EVERY rule known to belong to the
# candidate image. This catches a cloud contradiction to a local success without
# adding any extra cloud request.
# ---------------------------------------------------------------------------
main = repl(main,
'''            foreach (RecognitionCandidate candidate in candidates)
            {
                foreach (OcrRule rule in candidate.Rules.Where(rule => !values.ContainsKey(rule.Id)))
                    AddCloudRule(candidate.SourcePath, candidate.OcrPath, rule);
            }
''',
'''            foreach (RecognitionCandidate candidate in candidates)
            {
                foreach (OcrRule rule in RulesForAlreadyRequestedCloudFallback(candidate.Rules, values))
                    AddCloudRule(candidate.SourcePath, candidate.OcrPath, rule);
            }
''', 'F13 candidate planning')

helper_anchor = '''    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        RecognitionCandidate candidate,
        string stage) =>'''
helper = '''    internal static IReadOnlyList<OcrRule> RulesForAlreadyRequestedCloudFallback(
        IReadOnlyList<OcrRule> candidateRules,
        IReadOnlyDictionary<string, string> acceptedValues) =>
        candidateRules.Any(rule => !acceptedValues.ContainsKey(rule.Id))
            ? candidateRules
            : Array.Empty<OcrRule>();

    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        RecognitionCandidate candidate,
        string stage) =>'''
main = repl(main, helper_anchor, helper, 'F13 helper')

# ---------------------------------------------------------------------------
# F16 final closure: manual distribution is not allowed to promote arbitrary
# display TXT. Button requires evidence state; actual lines are rebuilt from
# trusted structured state and current rules.
# ---------------------------------------------------------------------------
main = repl(main,
'''    private bool CanManualDistribute() =>
        selectedImageDirectory is not null
        && File.Exists(ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value)));''',
'''    private bool CanManualDistribute() =>
        selectedImageDirectory is not null
        && File.Exists(ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value)))
        && File.Exists(ResultFilePaths.ForRecognitionState(AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value)));''',
'F16 manual button')

main = repl(main,
'''        int issue = Decimal.ToInt32(issueInput.Value);
        string summaryPath = ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory!, issue);
        SetBusy(true);
        try
        {
            string[] outputLines = (await File.ReadAllLinesAsync(summaryPath))
                .Select(GroupResultFormatter.RemoveLegacySourceSuffix).ToArray();
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, issue, outputLines);
            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
                ?? RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedImageDirectory!));''',
'''        int issue = Decimal.ToInt32(issueInput.Value);
        SetBusy(true);
        try
        {
            IReadOnlyList<OcrRule> rules = RuleCatalog.Load(selectedRulePath
                ?? RuleCatalog.PathForFolder(AppContext.BaseDirectory, selectedImageDirectory!));
            string[] outputLines = RecognitionStateStore.BuildTrustedOutputLines(
                AppContext.BaseDirectory, selectedImageDirectory!, issue, rules);
            DistributionResult distribution = await ResultDistributor.DistributeAllAsync(selectedImageDirectory!, issue, outputLines);''',
'F16 manual source')

# ---------------------------------------------------------------------------
# F18 final closure: the business extractor never sees a synthetic sequence that
# spans physical OCR regions/views. Each (ViewId, RegionId) is extracted on its
# own; multiple different valid values are a conflict, identical observations
# collapse to one value. This makes the boundary independent of every special
# parser branch inside RuleEngine.
# ---------------------------------------------------------------------------
rule = repl(rule,
'''    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        string? value = ExtractFinalValue(evidence.Lines, issue, rule);
        if (value is null)
            return null;

        OcrLineEvidence[] nonEmpty = evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToArray();
        if (nonEmpty.Length == 0)
            return null;

        // Positioned evidence has already been partitioned into physical
        // regions/columns. For an engine that cannot provide geometry, never
        // accept a value that only becomes valid after joining multiple opaque
        // lines: one returned physical OCR item must independently prove it.
        if (evidence.HasCompleteGeometry)
            return value;
        return nonEmpty.Any(item =>
            string.Equals(ExtractFinalValue(new[] { item.Text }, issue, rule), value, StringComparison.Ordinal))
            ? value
            : null;
    }
''',
'''    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, OcrLineEvidence> region in evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId + "\u001f" + item.RegionId, StringComparer.Ordinal))
        {
            string? value = ExtractFinalValue(region.Select(item => item.Text), issue, rule);
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 1 ? observed.Single() : null;
    }
''', 'F18 region extraction')

# ---------------------------------------------------------------------------
# Structured state also binds success to the exact current rule contract, not
# only RuleId/Type/Label. A rule/config change invalidates old trusted state.
# ---------------------------------------------------------------------------
state = repl(state,
'''    string RuleType,
    string OutputLabel,
    string Value,''',
'''    string RuleType,
    string OutputLabel,
    string RuleSignature,
    string Value,''', 'state signature field')

state = repl(state,
'''            rule.Type,
            rule.OutputLabel,
            accepted,''',
'''            rule.Type,
            rule.OutputLabel,
            RecognitionStateStore.RuleSignature(rule),
            accepted,''', 'state signature observe')

state = repl(state,
'''                    record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                    !RuleEngine.IsFormattedOutputValueValid(rule, record.Value ?? string.Empty) ||''',
'''                    record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                    record.RuleSignature != RuleSignature(rule) ||
                    !RuleEngine.IsFormattedOutputValueValid(rule, record.Value ?? string.Empty) ||''', 'state signature load')

state = repl(state,
'''                record.Status != "success" || record.Value != value ||
                record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                !RuleEngine.IsFormattedOutputValueValid(rule, value))''',
'''                record.Status != "success" || record.Value != value ||
                record.RuleType != rule.Type || record.OutputLabel != rule.OutputLabel ||
                record.RuleSignature != RuleSignature(rule) ||
                !RuleEngine.IsFormattedOutputValueValid(rule, value))''', 'state signature save')

insert_anchor = '''    internal static RecognitionStateLoad Load(
        string appDirectory,'''
insert = '''    internal static string RuleSignature(OcrRule rule)
    {
        string json = JsonSerializer.Serialize(rule);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    internal static string[] BuildTrustedOutputLines(
        string appDirectory,
        string selectedDirectory,
        int issue,
        IReadOnlyList<OcrRule> rules)
    {
        RecognitionStateLoad restored = Load(appDirectory, selectedDirectory, issue, rules);
        if (restored.Values.Count == 0)
            throw new OcrException("当前群结果没有可验证的来源状态，请重新识别后再手动分流。", "OCR_STATE_REQUIRED");
        var missingReasons = rules
            .Where(rule => !restored.Values.ContainsKey(rule.Id))
            .ToDictionary(rule => rule.Id, _ => "未找到可信来源状态", StringComparer.Ordinal);
        return RuleEngine.FormatOutput(rules, restored.Values, missingReasons);
    }

    internal static RecognitionStateLoad Load(
        string appDirectory,'''
state = repl(state, insert_anchor, insert, 'state helpers')

state = repl(state,
'''                ResultValues.AddTo(values, rule.Id, record.Value);
                if (!ResultValues.IsConflict(values, rule.Id))''',
'''                ResultValues.AddTo(values, rule.Id, record.Value!);
                if (!ResultValues.IsConflict(values, rule.Id))''', 'state nullable')

main_path.write_text(main, encoding='utf-8')
state_path.write_text(state, encoding='utf-8')
rule_path.write_text(rule, encoding='utf-8')
print('Applied final F13/F16/F18 closure')
