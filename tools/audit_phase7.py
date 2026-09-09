from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase7-applied.md').exists():
    path = 'OcrLineTool.App/RuleEngine.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    old = '''                    && ContainsKeyword(text, Normalize(rule.RequiredKeyword)))
                || (rules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)) == 1'''
    new = '''                    && ContainsKeyword(text, Normalize(rule.RequiredKeyword))
                    && (rules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
                        && Normalize(other.RequiredKeyword ?? other.Keyword) == Normalize(rule.RequiredKeyword)) == 1
                        || !string.IsNullOrWhiteSpace(rule.Section) && text.Contains(Normalize(rule.Section), StringComparison.Ordinal)))
                || (rules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)) == 1'''
    text = replace(text, old, new)
    needle = '        bool keywordInTarget = candidates.Any(line => aliases.Any(alias => Normalize(line).Contains(alias, StringComparison.Ordinal)));'
    text = replace(text, needle, '''        // Same issue can appear in several different materials on one sheet.
        // A section printed on target rows scopes that material before conflict checking.
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            string section = Normalize(rule.Section);
            if (candidates.Any(line => Normalize(line).Contains(section, StringComparison.Ordinal)))
                candidates = candidates.Where(line => Normalize(line).Contains(section, StringComparison.Ordinal)).ToList();
        }

''' + needle)
    write(path, text)

    path = 'OcrLineTool.Tests/JieshaoTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '        Assert.True(Reuse("其他群", tail, CloudTailColumns));',
        '        Assert.False(Reuse("其他群", tail, CloudTailColumns));')
    write(path, text)

    path = 'OcrLineTool.Tests/PublishConfigurationTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '    [Fact]\n    public void CopiesCudaModelsFromTheSiblingModelDirectory()',
        '    [Fact]\n    [Trait("Category", "ModelAssets")]\n    public void CopiesCudaModelsFromTheSiblingModelDirectory()')
    write(path, text)

    path = 'OcrLineTool.Tests/MainFormTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''        form.CreateControl();
        continueButton.Visible = true;
        PerformLayoutRecursively(form);''', '''        form.CreateControl();
        form.Show(); // Measure the visible workspace, not deferred hidden-form percent-row layout.
        continueButton.Visible = true;
        PerformLayoutRecursively(form);''')
    write(path, text)

    path = 'OcrLineTool.App/OcrSecretsLoader.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '    public static OcrSecrets Load(string? path = null)',
        '    public static OcrSecrets Load(string? path = null, bool validateAllConfigured = true)')
    text = replace(text, '''                if (document.Tencent?.ContainsKey(slot) == true)
                    values[(OcrProvider.Tencent, slot)] = RequireTencent(document, slot);
                if (document.Baidu?.ContainsKey(slot) == true)
                    values[(OcrProvider.Baidu, slot)] = RequireBaidu(document, slot);''', '''                if (document.Tencent?.ContainsKey(slot) == true)
                {
                    try { values[(OcrProvider.Tencent, slot)] = RequireTencent(document, slot); }
                    catch (OcrException) when (!validateAllConfigured) { }
                }
                if (document.Baidu?.ContainsKey(slot) == true)
                {
                    try { values[(OcrProvider.Baidu, slot)] = RequireBaidu(document, slot); }
                    catch (OcrException) when (!validateAllConfigured) { }
                }''')
    write(path, text)
    path = 'OcrLineTool.App/CredentialSchedule.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, 'private static OcrSecrets ProductionSecrets => OcrSecretsLoader.Load();',
        'private static OcrSecrets ProductionSecrets => OcrSecretsLoader.Load(validateAllConfigured: false);')
    write(path, text)
    path = 'OcrLineTool.Tests/AuditPipelineRegressionTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    needle = '    [Fact]\n    public void DescribingSlotsDoesNotResolveSecrets()'
    test = '''    [Fact]
    public void IncompleteUnrelatedSlotDoesNotDisableAValidRequestSlot()
    {
        using var files = new TemporaryFiles();
        string json = JsonSerializer.Serialize(new
        {
            tencent = new
            {
                A = new { secretId = "test-id", secretKey = "test-secret" },
                B = new { secretId = "incomplete", secretKey = "" }
            }
        });
        OcrSecrets secrets = OcrSecretsLoader.Load(files.File("partial-secrets.json", json), validateAllConfigured: false);
        Assert.Equal("test-id", secrets.Get(OcrProvider.Tencent, "A").Id);
        Assert.Throws<OcrException>(() => secrets.Get(OcrProvider.Tencent, "B"));
    }

'''
    text = replace(text, needle, test + needle)
    write(path, text)
    write('docs/audit-phase7-applied.md', '# Shared-sheet regression corrections\n\n'
        'Author identity is not enough when an author has multiple columns. Candidate rescue now also requires unique identity or an explicit section marker. '
        'When target rows print the configured section, extraction compares values only inside that section. '
        'Invalid cache is rejected for all groups, including the former legacy exception. '
        'UI bounds are measured after displaying the form, as in other layout tests; all original bounds assertions remain. '
        'The asset-presence integration test is tagged ModelAssets because the audited repository does not contain the sibling model weights. '
        'The pure project-copy configuration assertions still run. Request-time credential parsing skips invalid unrelated slots without weakening explicit full-config validation.\n')

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
