from pathlib import Path

# Refine the generated P2 source without weakening the new fail-closed rules.
rule_path = Path('OcrLineTool.App/RuleEngine.cs')
result_path = Path('OcrLineTool.App/ResultDistributor.cs')
test_path = Path('OcrLineTool.Tests/KillNumberDistributorTests.cs')

rule = rule_path.read_text(encoding='utf-8')
result = result_path.read_text(encoding='utf-8')
tests = test_path.read_text(encoding='utf-8')

# A single requested rule may still be a safe candidate when OCR explicitly
# identifies that rule (and its section, if any). This preserves genuinely
# dedicated folders without reintroducing the retry-subset folder-only bug.
old = '''            if (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                && !RuleCatalog.NormalizeGroupName(rule.RequiredKeyword)
                    .Equals(RuleCatalog.NormalizeGroupName(expectedFolder), StringComparison.OrdinalIgnoreCase)
                && !ContainsKeyword(text, Normalize(rule.RequiredKeyword)))
                return false;
            return HasValueForAnyIssue(lines, rule)'''
new = '''            if (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                && !RuleCatalog.NormalizeGroupName(rule.RequiredKeyword)
                    .Equals(RuleCatalog.NormalizeGroupName(expectedFolder), StringComparison.OrdinalIgnoreCase)
                && !ContainsKeyword(text, Normalize(rule.RequiredKeyword)))
                return false;
            bool explicitIdentity = MatchesText(text, rule)
                && (string.IsNullOrWhiteSpace(rule.Section)
                    || text.Contains(Normalize(rule.Section), StringComparison.Ordinal));
            if (explicitIdentity)
                return true;
            return HasValueForAnyIssue(lines, rule)'''
if rule.count(old) != 1:
    raise RuntimeError(f'RuleEngine explicit-identity anchor count={rule.count(old)}')
rule = rule.replace(old, new, 1)

# Replace only the generated IsConfiguredLine method. Known production labels
# are validated against their OCR rule. Explicit numberCounts remain an
# authoritative per-distribution override. Unknown custom labels fail closed
# except for a structurally safe numeric list (01-49, distinct).
start = result.index('    private static bool IsConfiguredLine(')
end = result.index('    private sealed record DistributionConfig(', start)
method = r'''    private static bool IsConfiguredLine(
        string line,
        HashSet<string> labels,
        IReadOnlyDictionary<string, int>? numberCounts,
        IReadOnlyDictionary<string, OcrRule> rulesByLabel)
    {
        int separator = line.LastIndexOf(' ');
        if (separator < 0 || line.StartsWith("缺失", StringComparison.Ordinal))
            return false;

        string label = line[(separator + 1)..];
        if (!labels.Contains(label))
            return false;
        string value = line[..separator].Trim();

        if (numberCounts?.TryGetValue(label, out int expectedCount) == true)
            return IsSafeNumberList(value, expectedCount);
        if (rulesByLabel.TryGetValue(label, out OcrRule? ocrRule))
            return RuleEngine.IsFormattedOutputValueValid(ocrRule, value);

        // Custom labels have no OCR rule to prove a semantic type. Preserve the
        // generic distributor contract only for an unambiguous numeric list;
        // arbitrary text/生肖/尾数 cannot bypass business validation this way.
        return IsSafeNumberList(value, expectedCount: null);
    }

    private static bool IsSafeNumberList(string value, int? expectedCount)
    {
        string[] numbers = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length == 0 || expectedCount is int count && numbers.Length != count)
            return false;
        return numbers.Distinct(StringComparer.Ordinal).Count() == numbers.Length
            && numbers.All(number => number.Length == 2
                && number.All(character => character is >= '0' and <= '9')
                && int.TryParse(number, out int parsed) && parsed is >= 1 and <= 49);
    }

'''
result = result[:start] + method + result[end:]

# The old production-target test used two numbers as placeholders for 36-code
# rules. That is now intentionally invalid. Keep the test's original purpose
# (each production config writes to the correct target) using a real 36-number
# value instead of weakening production validation.
method_name = 'public async Task ProductionPendingConfigsDistributeToTheirIndependentTargets()'
test_start = tests.index(method_name)
test_end = tests.index('\n    [Fact]', test_start + len(method_name))
block = tests[test_start:test_end]
valid36 = ','.join(f'{number:02d}' for number in range(1, 37))
for label in ['宝典', '心水', '内幕', '强哥', '锁妖', '赛马会', '龙王', '红人馆', '老人味', '时点半']:
    block = block.replace(f'"01,02 {label}"', f'"{valid36} {label}"')
tests = tests[:test_start] + block + tests[test_end:]

rule_path.write_text(rule, encoding='utf-8')
result_path.write_text(result, encoding='utf-8')
test_path.write_text(tests, encoding='utf-8')
print('Applied P2 compatibility refinement without weakening production validation')
