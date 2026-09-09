from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase6-applied.md').exists():
    path = 'OcrLineTool.App/MainForm.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''            activeCancellation?.Cancel();
            activeCancellation?.Dispose();
            dateTimer.Dispose();''', '''            activeCancellation?.Cancel();
            activeCancellation?.Dispose();
            activeCancellation = null;
            dateTimer.Dispose();''')
    write(path, text)

    path = 'OcrLineTool.Tests/TemplateSelectionTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '[Trait("Category", "CUDA")]\npublic sealed class TemplateSelectionCollection', 'public sealed class TemplateSelectionCollection')
    text = replace(text, 'public sealed class TemplateSelectionTests', '[Trait("Category", "CUDA")]\npublic sealed class TemplateSelectionTests')
    write(path, text)

    path = 'OcrLineTool.App/AtomicFile.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, fullPath + ".bak");
            else''', '''            if (File.Exists(fullPath))
            {
                string backups = Path.Combine(Path.GetDirectoryName(fullPath)!, ".ocr-backups");
                Directory.CreateDirectory(backups);
                File.Replace(temporary, fullPath, Path.Combine(backups, Path.GetFileName(fullPath) + ".bak"));
            }
            else''')
    write(path, text)
    path = 'OcrLineTool.App/OwnedResultWriter.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '        string ownerPath = targetPath + ".ocr-owners.json";', '''        string stateDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(targetPath))!, ".ocr-state");
        string ownerPath = Path.Combine(stateDirectory, Path.GetFileName(targetPath) + ".owners.json");''')
    text = replace(text, '            using FileStream gate = new(targetPath + ".ocr-write.lock", FileMode.OpenOrCreate,', '''            Directory.CreateDirectory(stateDirectory);
            using FileStream gate = new(Path.Combine(stateDirectory, Path.GetFileName(targetPath) + ".lock"), FileMode.OpenOrCreate,''')
    write(path, text)

    path = 'OcrLineTool.App/RuleEngine.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''            return HasValueForAnyIssue(lines, rule)
                || (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                    && ContainsKeyword(text, Normalize(rule.RequiredKeyword)))
                || expectedFolder.Equals(rule.RequiredKeyword ?? rule.Keyword, StringComparison.OrdinalIgnoreCase);''', '''            return HasValueForAnyIssue(lines, rule)
                || (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                    && !RuleCatalog.NormalizeGroupName(rule.RequiredKeyword).Equals(
                        RuleCatalog.NormalizeGroupName(expectedFolder), StringComparison.OrdinalIgnoreCase)
                    && ContainsKeyword(text, Normalize(rule.RequiredKeyword)))
                || (rules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)) == 1
                    && expectedFolder.Equals(rule.RequiredKeyword ?? rule.Keyword, StringComparison.OrdinalIgnoreCase));''')
    text = replace(text, '        if (rule.StrictIssueBlock)\n            return ExtractStrictIssueBlock(lines, issue, rule);', '''        if (rule.StrictIssueBlock || rule.Folder == "公式杀料")
            return ExtractStrictIssueBlock(lines, issue, rule);''')
    write(path, text)

    # Obsolete expectations intentionally change only where the user authorized safer semantics.
    path = 'OcrLineTool.Tests/YanranConfirmedHardeningTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, 'RestoresTheCurrentDoomsdayRowFromItsAnchoredConsecutiveHistory', 'DoesNotInventTheCurrentDoomsdayRowFromConsecutiveHistory')
    text = replace(text, '        Assert.Equal("1段", RuleEngine.ExtractFinalValue(lines, 246, rule));', '        Assert.Null(RuleEngine.ExtractFinalValue(lines, 246, rule));')
    write(path, text)

    path = 'OcrLineTool.Tests/OcrClientHttpTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, 'TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure', 'TencentRecognizeAsyncPreservesCallerCancellation')
    text = replace(text, '''            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains("腾讯云连接失败", exception.Message);''', '''            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));''')
    write(path, text)

    path = 'OcrLineTool.Tests/paddle_local_ocr_test.py'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '            self.assertEqual(cache, prepared)', '''            self.assertEqual(cache, prepared.parent)
            self.assertEqual(64, len(prepared.name))
            self.assertTrue(str(prepared).isascii())''')
    text = text.replace('(cache / name / "inference.json")', '(prepared / name / "inference.json")')
    text = text.replace('(cache / name / "inference.pdiparams")', '(prepared / name / "inference.pdiparams")')
    write(path, text)

    write('docs/audit-phase6-applied.md', '# Regression corrections\n\n'
        'Keep type separation when a folder has several materials. Formula cards use their explicit payload parser, not numbers in the formula expression. '
        'Dispose is idempotent. Sidecar ownership/locks and backups live in dedicated subdirectories and no longer pollute the external data-file list. '
        'The CUDA trait is attached to the actual test class rather than its collection definition. '
        'Updated old tests that expected inferred periods, network-error wrapping of explicit cancellation, or a non-versioned model-cache root. '
        'These expectation changes follow the reviewed contract; normal data assertions are retained.\n')

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
