from pathlib import Path

path = Path('tools/apply_residual_accuracy_stage_a.py')
text = path.read_text(encoding='utf-8')
old = "text = replace_once(text, old_evidence, new_evidence, 'F08/F18 opaque completeness')"
new = '''text = sub_once(text,
    r\"    public static string\\? ExtractFinalValue\\(OcrEvidence evidence, int issue, OcrRule rule\\)\\n    \\{.*?\\n    \\}\\n\\n    private static IEnumerable<string> RejectCrossIssueNearbyValue\",
    new_evidence + \"\\n\\n    private static IEnumerable<string> RejectCrossIssueNearbyValue\",
    'F08/F18 opaque completeness', flags=re.S)'''
if text.count(old) != 1:
    raise RuntimeError(f'expected one evidence replacement call, found {text.count(old)}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Hardened stage A evidence method anchor')
