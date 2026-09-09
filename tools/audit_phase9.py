from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase9-applied.md').exists():
    path = 'OcrLineTool.Tests/MainFormTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    start = text.index('    public void KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(float scale)')
    end = text.index('    [Fact]', start)
    block = text[start:end]
    block = replace(block, '        using var form = new MainForm { Size = new Size(1100, 700) };', '''        // Top-level windows are capped by the host desktop's MaxWindowTrackSize.
        // A child-form viewport tests the intended scaled workspace even on a small CI desktop.
        using var host = new Panel { Size = new Size(2200, 1600) };
        using var form = new MainForm { TopLevel = false, Size = new Size(1100, 700) };
        host.Controls.Add(form);''')
    block = replace(block, '        PerformLayoutRecursively(form);', '''        PerformLayoutRecursively(form);
        Assert.True(form.Height >= (int)Math.Ceiling(700 * scale),
            $"The viewport must retain the requested scale. Actual={form.Size}; DesktopLimit={SystemInformation.MaxWindowTrackSize}.");''')
    text = text[:start] + block + text[end:]
    write(path, text)
    write('docs/audit-phase9-applied.md', '# Deterministic UI viewport\n\n'
        'The high-DPI workspace bounds test now uses a child-form viewport instead of a top-level window constrained by the hosted desktop maximum tracking size. '
        'It additionally asserts the requested scaled height and retains every original positive-size and containment assertion. '
        'This is a test-environment change only: production form sizing, colors, controls, and layout are unchanged.\n')

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
