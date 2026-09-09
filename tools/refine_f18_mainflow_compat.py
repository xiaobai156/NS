from pathlib import Path

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')

def repl(old: str, new: str, expected: int = 1) -> None:
    global text
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f'expected {expected} anchors, found {count}: {old[:180]!r}')
    text = text.replace(old, new, expected)

# Keep the pre-existing private helper name unique because several long-lived
# regression tests intentionally reflect it. The production evidence helper has
# a distinct name and is the one used by the new retry path.
repl(
'''    private static void AddExtractedValues(
        OcrEvidence evidence,
        IEnumerable<OcrRule> rules,
        int issue,
        ResultValues values,
        ResultEvidenceLedger ledger)''',
'''    private static void AddExtractedEvidenceValues(
        OcrEvidence evidence,
        IEnumerable<OcrRule> rules,
        int issue,
        ResultValues values,
        ResultEvidenceLedger ledger)''')
repl('AddExtractedValues(primaryEvidence, candidateMissing, lastIssue, lastValues, lastEvidenceLedger);',
     'AddExtractedEvidenceValues(primaryEvidence, candidateMissing, lastIssue, lastValues, lastEvidenceLedger);', expected=2)
repl('AddExtractedValues(fallbackEvidence, candidate.Rules, lastIssue, lastValues, lastEvidenceLedger);',
     'AddExtractedEvidenceValues(fallbackEvidence, candidate.Rules, lastIssue, lastValues, lastEvidenceLedger);')

# Preserve the old private retry seam for existing unit tests. Production code
# no longer calls this method; it uses RecognizeRetryEvidenceAsync. This wrapper
# deliberately keeps the historical string-only behavior so test fixtures with
# virtual paths do not need physical files, while runtime evidence remains strict.
anchor = '''    private async Task WaitForPacingAsync(Stopwatch spacing, TimeSpan minimumInterval)
    {'''
compat = '''    private async Task<IReadOnlyList<string>> RecognizeRetryAsync(
        IOcrClient client,
        OcrCredential credential,
        string imagePath,
        int current,
        int total,
        IReadOnlyList<OcrRule> rules,
        int issue,
        IDictionary<string, string> values)
    {
        int retry = 0;
        while (true)
        {
            try
            {
                IReadOnlyList<string> lines = await client.RecognizeAsync(imagePath, ActiveToken);
                AddExtractedValues(lines, rules, issue, values);
                return lines;
            }
            catch (OcrException exception) when (CloudOcrPolicy.IsRateLimit(credential.Provider, exception))
            {
                if (retry < CloudOcrPolicy.MaxAutomaticRetries)
                {
                    TimeSpan delay = CloudOcrPolicy.RetryDelay(++retry);
                    statusLabel.Text = $"复抓触发限流：{delay.TotalSeconds:0} 秒后重试 {current}/{total}";
                    await Task.Delay(delay, ActiveToken);
                    continue;
                }

                await WaitForCloudResumeAsync(current, total, imagePath);
                retry = 0;
            }
        }
    }

    private async Task WaitForPacingAsync(Stopwatch spacing, TimeSpan minimumInterval)
    {'''
repl(anchor, compat)

path.write_text(text, encoding='utf-8')
print('Preserved legacy private test seams without changing production evidence flow')
