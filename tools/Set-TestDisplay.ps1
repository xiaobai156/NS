$ErrorActionPreference = 'Stop'
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
