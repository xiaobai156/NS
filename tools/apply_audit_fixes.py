"""One-shot reviewed source migration, executed only on the isolated repair branch.
No production images, OCR services, deployment or secrets are accessed.
Every replacement requires the audited source anchor; changed paths are explicit.
"""
from pathlib import Path
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / '.audit-changed.json'
changed = []

def write(path, text):
    destination = ROOT / path
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(text, encoding='utf-8', newline='\n')
    if path not in changed:
        changed.append(path)

def replace(text, old, new, count=1):
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(f'Expected {count} audited anchors, found {actual}: {old[:100]!r}')
    return text.replace(old, new)

def phase1():
    if (ROOT / 'docs/audit-phase1-applied.md').exists():
        return
    path = 'OcrLineTool.App/RuleEngine.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '        lines = RecoverTruncatedDoomsdayIssue(lines, issue, rule);',
        '        // A requested issue is a query, never evidence for repairing OCR.\n        if (!rule.StrictIssueBlock)\n            lines = SplitInlineIssueRows(lines);')
    start = text.index('    private static string[] RecoverTruncatedDoomsdayIssue(')
    end = text.index('    private static string? ExtractZodiacSummaryValue(', start)
    text = text[:start] + '''    private static string[] SplitInlineIssueRows(IEnumerable<string> source)
    {
        var output = new List<string>();
        foreach (string line in source)
        {
            MatchCollection periods = IssueRegex.Matches(line);
            int start = 0;
            for (int index = 1; index < periods.Count; index++)
            {
                output.Add(line[start..periods[index].Index]);
                start = periods[index].Index;
            }
            output.Add(line[start..]);
        }
        return output.ToArray();
    }

    private static string[] SummaryRowsForIssue(IEnumerable<string> source, int issue)
    {
        var rows = new List<string>();
        bool inTarget = false;
        foreach (string line in SplitInlineIssueRows(source))
        {
            if (ContainsAnyIssue(line))
                inTarget = ContainsIssue(line, issue);
            if (inTarget)
                rows.Add(line);
        }
        return rows.ToArray();
    }

''' + text[end:]
    old = '''          if (digitCount == 8 && !tails.Contains(0))
              tails = tails.Append(0).ToArray();
          return (digitCount == 9 && tails.Length == 9) || (digitCount == 8 && tails.Length == 9)'''
    text = replace(text, old, '          return digitCount == 9 && tails.Length == 9')
    old = '''        foreach (string line in candidates.OrderByDescending(line => Normalize(line).Contains(keyword, StringComparison.Ordinal)))
        {
            string? value = ExtractTyped(line, rule.Type);
            if (value is not null)
                return value;
        }
'''
    text = replace(text, old, '''        var observed = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> relevant = keywordInTarget
            ? candidates.Where(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)))
            : candidates;
        foreach (string line in relevant)
        {
            string? value = ExtractTyped(line, rule.Type);
            if (value is not null)
                observed.Add(value);
        }
        // Never let an earlier copy of the selected issue silently win.
        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : null;
''')
    start = text.index('    private static string? ExtractZodiacSummaryValue(')
    end = text.index('    private static string? ExtractMostFrequentZodiac(', start)
    block = text[start:end]
    block = replace(block, '        string[] aliases = [rule.Keyword, rule.Label ?? string.Empty];',
        '        lines = SummaryRowsForIssue(lines, issue);\n        var observed = new HashSet<string>(StringComparer.Ordinal);\n        string[] aliases = [rule.Keyword, rule.Label ?? string.Empty];')
    block = replace(block, '                        return value;', '                        observed.Add(value);', count=2)
    # The guard retains its null return; only the final result is replaced.
    position = block.rfind('        return null;')
    block = block[:position] + block[position:].replace('        return null;',
        '        return observed.Count == 1 ? observed.Single() : null;', 1)
    text = text[:start] + block + text[end:]
    write(path, text)

    write('OcrLineTool.Tests/AuditSafetyRegressionTests.cs', '''using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class AuditSafetyRegressionTests
{
    private static readonly OcrRule Doomsday = new("末日降临", "段", RequiredKeyword: "末日降临", Folder: "天机阁杀料");
    private static readonly OcrRule Zodiac = new("南国挽心", "生肖", RequiredKeyword: "南国挽心", Folder: "天机阁杀料");
    private static readonly OcrRule Tail = new("公子送尾数", "缺尾", "翩翩公子尾", StrictIssueBlock: true);

    [Fact]
    public void PreviousSuffixCannotInventRequestedIssue() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["249期 末日降临 1段", "50期 末日降临 2段", "50期 末日降临 3段"], 251, Doomsday));

    [Fact]
    public void EvenMatchingShortSuffixNeedsExplicitIssueEvidence() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["249期 末日降临 1段", "51期 末日降临 3段"], 251, Doomsday));

    [Fact]
    public void EightTailsCannotInventZero() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["251期", "公子送尾数：1 2 3 4 5 6 7 8 准！"], 251, Tail));

    [Fact]
    public void InlineIssuesCannotBorrowPreviousValue() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["250期 南国挽心 鸡 251期 南国挽心 待更新"], 251, Zodiac));

    [Fact]
    public void ConflictingTargetRowsCannotChooseFirst() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["251期 南国挽心 鸡", "251期 南国挽心 狗"], 251, Zodiac));

    [Fact]
    public void SummaryIsScopedToRequestedIssue() => Assert.Equal("狗", RuleEngine.ExtractFinalValue(
        ["统计表", "250期", "南国挽心 禁 鸡", "251期", "南国挽心 禁 狗"], 251, Zodiac));

    [Fact]
    public void ConflictingSummaryCopiesCannotChooseFirst() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["统计表", "251期", "南国挽心 禁 鸡", "251期", "南国挽心 禁 狗"], 251, Zodiac));

    [Fact]
    public void ExplicitIssueRetainsCorrectValue() => Assert.Equal("3段", RuleEngine.ExtractFinalValue(
        ["250期 末日降临 2段", "251期 末日降临 3段"], 251, Doomsday));

    [Fact]
    public void CompleteDistinctTailSetStillWorks() => Assert.Equal("8尾", RuleEngine.ExtractFinalValue(
        ["251期", "公子送尾数：0 1 2 3 4 5 6 7 9 准！"], 251, Tail));

    [Fact]
    public void OtherIssueCannotProduceResult() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["250期 南国挽心 鸡"], 251, Zodiac));

    [Fact]
    public void IdenticalTargetCopiesRemainValid() => Assert.Equal("狗", RuleEngine.ExtractFinalValue(
        ["251期 南国挽心 狗", "251期 南国挽心 狗"], 251, Zodiac));
}
''')
    # Tag hardware tests explicitly; no CPU OCR fallback and no silent test skip.
    # These classes construct CUDA fingerprints and cannot run on hosted Windows VMs.
    hardware = []
    for test in (ROOT / 'OcrLineTool.Tests').glob('*Tests.cs'):
        source = test.read_text(encoding='utf-8-sig')
        if any(token in source for token in ('VisualTemplateMatcher.CreateFingerprint(',
                                             'VisualTemplateMatcher.Match(',
                                             'VisualTemplateMatcher.CreateCrop(')):
            source, count = re.subn(r'(?m)^public sealed class ', '[Trait("Category", "CUDA")]\npublic sealed class ', source, count=1)
            if count != 1:
                raise RuntimeError(f'Cannot tag CUDA test class {test}')
            write(test.relative_to(ROOT).as_posix(), source)
            hardware.append(test.name)
    write('docs/audit-phase1-applied.md', '# Audit repair phase 1\n\n'
        'Removed inferred issue repair and inferred zero digits. Legacy inline periods are split before extraction; '
        'summary rows are bounded by the selected issue, and conflicting observed values are rejected.\n\n'
        'CUDA-only test classes are explicitly tagged, not claimed as executed on a hosted VM: '
        + ', '.join(hardware) + '.\nNo production OCR or deployment is performed.\n')

if __name__ == '__main__':
    phase1()
    MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
    print('Reviewed changed paths: ' + ', '.join(changed))
