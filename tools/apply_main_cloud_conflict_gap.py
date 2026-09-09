from pathlib import Path

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')


def repl(old: str, new: str, label: str, count: int = 1):
    global text
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(f'{label}: expected {count}, found {actual}')
    text = text.replace(old, new, count)

repl(
'''                foreach (OcrRule rule in activeRules)
                {
                    string? value = RuleEngine.ExtractFinalValue(cloudEvidence, issue, rule);
                    if (value is not null)
                    {
                        evidenceLedger.Observe(values, rule, value, cloudEvidence);
                        matchedValues.Add($"{value} {rule.OutputLabel}");
                    }
                    else if (!values.ContainsKey(rule.Id))
                    {
                        missingRules.Add(rule);
                    }
                }''',
'''                foreach (OcrRule rule in activeRules)
                {
                    RuleExtractionResult result = RuleEngine.ExtractFinalResult(cloudEvidence, issue, rule);
                    if (result.Status == RuleExtractionStatus.Conflict)
                    {
                        evidenceLedger.ObserveConflict(values, rule, cloudEvidence);
                        matchedValues.Add($"冲突 {rule.OutputLabel}");
                    }
                    else if (result.Status == RuleExtractionStatus.Success)
                    {
                        evidenceLedger.Observe(values, rule, result.Value!, cloudEvidence);
                        matchedValues.Add($"{result.Value} {rule.OutputLabel}");
                    }
                    else if (!values.ContainsKey(rule.Id) && !ResultValues.IsConflict(values, rule.Id))
                    {
                        missingRules.Add(rule);
                    }
                }''',
'main primary cloud conflict propagation')

repl(
'''                        foreach (OcrRule rule in activeRules)
                        {
                            string? value = RuleEngine.ExtractFinalValue(fallbackEvidence, issue, rule);
                            if (value is null)
                                continue;
                            evidenceLedger.Observe(values, rule, value, fallbackEvidence);
                            matchedValues.Add($"{value} {rule.OutputLabel}");
                        }''',
'''                        foreach (OcrRule rule in activeRules)
                        {
                            RuleExtractionResult result = RuleEngine.ExtractFinalResult(fallbackEvidence, issue, rule);
                            if (result.Status == RuleExtractionStatus.Conflict)
                            {
                                evidenceLedger.ObserveConflict(values, rule, fallbackEvidence);
                                matchedValues.Add($"冲突 {rule.OutputLabel}");
                            }
                            else if (result.Status == RuleExtractionStatus.Success)
                            {
                                evidenceLedger.Observe(values, rule, result.Value!, fallbackEvidence);
                                matchedValues.Add($"{result.Value} {rule.OutputLabel}");
                            }
                        }''',
'main fallback cloud conflict propagation')

repl(
'''        return rules.Any(rule => RuleEngine.ExtractFinalValue(evidence, issue, rule) is not null);''',
'''        return rules.Any(rule =>
            RuleEngine.ExtractFinalResult(evidence, issue, rule).Status == RuleExtractionStatus.Success);''',
'structured cache success predicate')

repl(
'''    private static bool CanReuseRetryCloudLines(
        string groupDirectory, IReadOnlyList<OcrRule> rules, IReadOnlyList<string> lines, int issue) =>
        lines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
        rules.All(rule => RuleEngine.ExtractFinalValue(lines, issue, rule) is not null);''',
'''    private static bool CanReuseRetryCloudLines(
        string groupDirectory, IReadOnlyList<OcrRule> rules, IReadOnlyList<string> lines, int issue) =>
        lines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
        rules.All(rule =>
            RuleEngine.ExtractFinalResult(lines, issue, rule).Status == RuleExtractionStatus.Success);''',
'legacy retry-cache compatibility helper stays success-only')

path.write_text(text, encoding='utf-8')
print('Applied main cloud conflict propagation patch')
