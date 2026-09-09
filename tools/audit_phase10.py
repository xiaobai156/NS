from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json
import subprocess

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase10-applied.md').exists():
    write('tools/Set-TestDisplay.ps1', r'''$ErrorActionPreference = 'Stop'
# This is a hosted-CI fixture, never a desktop setting for a developer or production computer.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    Write-Host 'Display setup is restricted to GitHub-hosted runners; local settings are unchanged.'
    exit 0
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OcrHostedTestDisplay
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public uint dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DEVMODE mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref DEVMODE mode, uint flags);

    public static string Configure()
    {
        DEVMODE mode = new DEVMODE();
        mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettings(null, -1, ref mode))
            throw new InvalidOperationException("Cannot read the hosted test display mode.");
        string before = mode.dmPelsWidth + "x" + mode.dmPelsHeight;
        if (mode.dmPelsWidth < 1920 || mode.dmPelsHeight < 1080)
        {
            mode.dmPelsWidth = 1920;
            mode.dmPelsHeight = 1080;
            mode.dmFields = 0x00080000 | 0x00100000;
            int result = ChangeDisplaySettings(ref mode, 0);
            if (result != 0)
                throw new InvalidOperationException("Hosted display setup failed: " + result);
        }
        if (!EnumDisplaySettings(null, -1, ref mode) || mode.dmPelsWidth < 1920 || mode.dmPelsHeight < 1080)
            throw new InvalidOperationException("Hosted desktop did not retain the required test resolution.");
        return "Hosted test desktop: " + before + " -> " + mode.dmPelsWidth + "x" + mode.dmPelsHeight;
    }
}
'@
[OcrHostedTestDisplay]::Configure()
''')
    path = 'OcrLineTool.Tests/MainFormTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''        // Top-level windows are capped by the host desktop's MaxWindowTrackSize.
        // A child-form viewport tests the intended scaled workspace even on a small CI desktop.
        using var host = new Panel { Size = new Size(2200, 1600) };
        using var form = new MainForm { TopLevel = false, Size = new Size(1100, 700) };
        host.Controls.Add(form);''', '''        using var form = new MainForm { Size = new Size(1100, 700) };''')
    text = replace(text, '        form.Show(); // Measure the visible workspace, not deferred hidden-form percent-row layout.',
        '        form.Show();')
    write(path, text)
    write('docs/audit-phase10-applied.md', '# Hosted desktop fixture\n\n'
        'The failure was confirmed to be an actual test-window clamp at 1044x788, exactly the hosted desktop MaxWindowTrackSize. '
        'The production application layout is unchanged. The original top-level form test remains intact and additionally verifies its requested scaled height. '
        'A guarded fixture configures only GitHub-hosted CI desktops to at least 1920x1080 before GUI tests. '
        'The helper refuses to change local or self-hosted desktops. It does not install drivers, run OCR, access user images or deploy software.\n')

subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
                '-File', str(ROOT / 'tools/Set-TestDisplay.ps1')], cwd=ROOT, check=True)
MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
