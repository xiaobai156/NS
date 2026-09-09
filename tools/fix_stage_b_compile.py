from pathlib import Path


def replace_once(path, old, new, label):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1, found {count}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

replace_once(
    "OcrLineTool.App/RecognitionStateStore.cs",
    '''        bool conflict = ResultValues.IsConflict(values, rule.Id);\n        if (!conflict && !values.TryGetValue(rule.Id, out string? accepted))\n        {\n            records.Remove(rule.Id);\n            return;\n        }\n\n        records[rule.Id] = BuildRecord(\n            rule,\n            conflict ? string.Empty : accepted!,''',
    '''        bool conflict = ResultValues.IsConflict(values, rule.Id);\n        string? accepted = null;\n        if (!conflict && !values.TryGetValue(rule.Id, out accepted))\n        {\n            records.Remove(rule.Id);\n            return;\n        }\n\n        records[rule.Id] = BuildRecord(\n            rule,\n            conflict ? string.Empty : accepted!,''',
    "definite assignment",
)

replace_once(
    "OcrLineTool.App/MainForm.cs",
    '''                    primaryEvidence = cachedEvidence;\n                    AddExtractedEvidenceValues(primaryEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);''',
    '''                    primaryEvidence = cachedEvidence!;\n                    AddExtractedEvidenceValues(primaryEvidence, evidenceRules, lastIssue, lastValues, lastEvidenceLedger);''',
    "cached evidence nullability",
)

print("Fixed stage B compile guards")
