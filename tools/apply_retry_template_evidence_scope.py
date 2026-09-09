from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')

# This runs after lifecycle + crop-creation pin patches.
old_record = '''    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null,
        OcrEvidence? LocalEvidence = null,
        OcrEvidenceIdentity? CreationIdentity = null)
    {
        internal OcrEvidenceIdentity PinnedIdentity { get; } =
            CreationIdentity ?? OcrEvidenceIdentity.Capture(SourcePath, OcrPath, "candidate");
    }'''
new_record = '''    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null,
        OcrEvidence? LocalEvidence = null,
        OcrEvidenceIdentity? CreationIdentity = null,
        IReadOnlyList<string>? DeclaredRuleIds = null)
    {
        internal OcrEvidenceIdentity PinnedIdentity { get; } =
            CreationIdentity ?? OcrEvidenceIdentity.Capture(SourcePath, OcrPath, "candidate");
    }'''
text = replace_once(text, old_record, new_record, 'candidate declared rules')

# Template candidate: preserve the full configured RuleIds for this physical
# template, even when SelectForRules cloned it down to a retry subset.
old = '''                            templateCandidates.Add(new RecognitionCandidate(
                                match.SourcePath,
                                ocrPath,
                                match.Template.RuleIds.Select(id => ruleMap[id]).ToArray(),
                                true,
                                "标题模板",
                                match.Distance,
                                CreationIdentity: creationIdentity));'''
new = '''                            string[] declaredRuleIds = catalog.Templates
                                .Single(template => template.Id.Equals(match.Template.Id, StringComparison.Ordinal))
                                .RuleIds;
                            templateCandidates.Add(new RecognitionCandidate(
                                match.SourcePath,
                                ocrPath,
                                match.Template.RuleIds.Select(id => ruleMap[id]).ToArray(),
                                true,
                                "标题模板",
                                match.Distance,
                                CreationIdentity: creationIdentity,
                                DeclaredRuleIds: declaredRuleIds));'''
text = replace_once(text, old, new, 'template declared rule ids')

old_retry = '''    private static OcrRule[] RetryEvidenceRules(RecognitionCandidate candidate, IReadOnlyList<OcrRule> allRules)
    {
        HashSet<string> selectedIds = candidate.Rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> folders = candidate.Rules
            .Select(rule => rule.Folder)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return allRules.Where(rule => selectedIds.Contains(rule.Id)
            || !string.IsNullOrWhiteSpace(rule.Folder) && folders.Contains(rule.Folder!)).ToArray();
    }'''
new_retry = '''    internal static IReadOnlyList<OcrRule> ExpandDeclaredEvidenceRules(
        IReadOnlyList<OcrRule> selectedRules,
        IReadOnlyList<OcrRule> allRules,
        IReadOnlyList<string>? declaredRuleIds)
    {
        if (declaredRuleIds is not { Count: > 0 })
            return selectedRules;
        string[] ids = declaredRuleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var byId = allRules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        // A malformed declaration must never broaden evidence to an approximate
        // set. Fall back to the already-selected rules instead.
        if (ids.Length == 0 || ids.Any(id => !byId.ContainsKey(id)))
            return selectedRules;
        return ids.Select(id => byId[id]).ToArray();
    }

    private static OcrRule[] RetryEvidenceRules(RecognitionCandidate candidate, IReadOnlyList<OcrRule> allRules)
    {
        if (candidate.DeclaredRuleIds is { Count: > 0 })
            return ExpandDeclaredEvidenceRules(
                candidate.Rules, allRules, candidate.DeclaredRuleIds).ToArray();

        HashSet<string> selectedIds = candidate.Rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> folders = candidate.Rules
            .Select(rule => rule.Folder)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return allRules.Where(rule => selectedIds.Contains(rule.Id)
            || !string.IsNullOrWhiteSpace(rule.Folder) && folders.Contains(rule.Folder!)).ToArray();
    }'''
text = replace_once(text, old_retry, new_retry, 'retry declared evidence rules')

path.write_text(text, encoding='utf-8')
print('Applied shared-template retry evidence scope')
