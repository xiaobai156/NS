from pathlib import Path

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')

def repl(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'expected exactly one anchor, found {count}: {old[:160]!r}')
    text = text.replace(old, new, 1)

# F13: local-primary cloud fallback candidates were planned while a rule was
# missing, but the loop dynamically removed a rule after the first candidate
# succeeded. Keep the planned rule set stable so competing already-planned
# candidates are actually compared and ResultValues can surface conflicts.
repl(
'''            foreach (RecognitionCandidate candidate in cloudCandidates)
            {
                OcrRule[] pendingRules = candidate.Rules
                    .Where(rule => !values.ContainsKey(rule.Id))
                    .ToArray();
                if (pendingRules.Length == 0)
                    continue;
''',
'''            foreach (RecognitionCandidate candidate in cloudCandidates)
            {
                // The candidate list was frozen while these rules were missing. Do not
                // drop a rule merely because an earlier competing candidate succeeded;
                // compare every already-planned observation so conflicts stay visible.
                OcrRule[] evidenceRules = candidate.Rules.ToArray();
                if (evidenceRules.Length == 0)
                    continue;
''')
for old, new in [
    ('CanReuseRetryCloudLines(selectedImageDirectory!, pendingRules, cached, issue)',
     'CanReuseRetryCloudLines(selectedImageDirectory!, evidenceRules, cached, issue)'),
    ('foreach (OcrRule rule in pendingRules)\n                {',
     'foreach (OcrRule rule in evidenceRules)\n                {'),
    ('rules = pendingRules.Select(rule => rule.Id),',
     'rules = evidenceRules.Select(rule => rule.Id),')
]:
    repl(old, new)

# F13: when a fallback provider response has already been paid for/received,
# compare it against every rule applicable to that candidate, not only fields
# that were missing in the primary response.
repl(
'''                    foreach (OcrRule rule in missingRules.Where(_ => fallbackLines is not null))
                    {''',
'''                    foreach (OcrRule rule in activeRules.Where(_ => fallbackLines is not null))
                    {''')

# F13 retry: fallback is still requested only when something remains missing,
# but once that response is received it must be compared against the full
# candidate rule set so it can contradict a primary value.
repl(
'''                candidateMissing = candidateMissing.Where(rule => !lastValues.ContainsKey(rule.Id)).ToArray();
                IReadOnlyList<string> fallbackLines = [];
                if (candidateMissing.Length > 0)
                {''',
'''                OcrRule[] fallbackMissing = candidateMissing
                    .Where(rule => !lastValues.ContainsKey(rule.Id))
                    .ToArray();
                IReadOnlyList<string> fallbackLines = [];
                if (fallbackMissing.Length > 0)
                {''')
repl(
'''                            selectedImageDirectory!, candidate.SourcePath, candidateMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryAsync(
                                    fallbackClient, fallbackCredential, candidate.SourcePath, completed + 1, selection.Candidates.Count,
                                    candidateMissing, lastIssue, lastValues);
                            });
                        AddExtractedValues(fallbackLines, candidateMissing, lastIssue, lastValues);''',
'''                            selectedImageDirectory!, candidate.SourcePath, fallbackMissing,
                            async () =>
                            {
                                cloudRequests++;
                                return await RecognizeRetryAsync(
                                    fallbackClient, fallbackCredential, candidate.SourcePath, completed + 1, selection.Candidates.Count,
                                    fallbackMissing, lastIssue, lastValues);
                            });
                        AddExtractedValues(fallbackLines, candidate.Rules, lastIssue, lastValues);''')

# F16: group TXT is presentation text, not trusted typed state. At minimum a
# recovered success must still satisfy the CURRENT rule's exact output shape.
repl(
'''            if (value.Length > 0)
            {
                ResultValues.AddTo(lastValues, rule.Id, value);
                lastTextRecognizedRuleIds.Add(rule.Id);
            }
''',
'''            if (value.Length > 0 && RuleEngine.IsFormattedOutputValueValid(rule, value))
            {
                ResultValues.AddTo(lastValues, rule.Id, value);
                lastTextRecognizedRuleIds.Add(rule.Id);
            }
            else if (value.Length > 0)
            {
                lastMissingReasons[rule.Id] = "保存结果未通过当前规则校验";
            }
''')

path.write_text(text, encoding='utf-8')
print('Applied F13/F16 closure patch')
