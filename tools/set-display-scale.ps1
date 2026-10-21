<#
.SYNOPSIS
Sets the primary display's resolution and scale (DPI) on a CI runner,
without signing out, as the Settings app's "Scale" does.

.DESCRIPTION
For CI runners only. The resolution goes through ChangeDisplaySettingsEx
(when -Width/-Height are given), the scale through
DisplayConfigSetDeviceInfo's DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE (the
undocumented request the Settings app makes): Windows offers scales as steps
relative to the recommended one, so the step for -Percent is worked out
from the recommended scale. Prints what Windows reports before and after.
Processes started afterwards that are per-monitor DPI aware see the new
scale; the system DPI of the session (that of DPI-unaware and system-aware
processes) only changes at the next sign-in.
#>
param([int]$Percent = 150, [int]$Width = 0, [int]$Height = 0)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class OtterDisplay
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr param);

    public static string Modes()
    {
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
        var seen = new System.Collections.Generic.SortedSet<string>();
        for (int i = 0; EnumDisplaySettings(null, i, ref mode); i++) seen.Add(mode.dmPelsWidth + "x" + mode.dmPelsHeight);
        return string.Join(", ", seen);
    }

    public static string Current()
    {
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
        EnumDisplaySettings(null, -1, ref mode);
        return mode.dmPelsWidth + "x" + mode.dmPelsHeight;
    }

    public static int SetResolution(int width, int height)
    {
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
        EnumDisplaySettings(null, -1, ref mode);
        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmFields = 0x80000 | 0x100000; // DM_PELSWIDTH | DM_PELSHEIGHT
        return ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, 0x1 /* CDS_UPDATEREGISTRY */, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo
    {
        public LUID SourceAdapter; public uint SourceId; public uint SourceModeIndex; public uint SourceFlags;
        public LUID TargetAdapter; public uint TargetId; public uint TargetModeIndex; public uint OutputTechnology;
        public uint Rotation; public uint Scaling; public uint RefreshNumerator; public uint RefreshDenominator;
        public uint ScanLineOrdering; public int TargetAvailable; public uint TargetFlags; public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ScaleGet { public int Type; public int Size; public LUID Adapter; public uint Id; public int Minimum; public int Current; public int Maximum; }

    [StructLayout(LayoutKind.Sequential)]
    struct ScaleSet { public int Type; public int Size; public LUID Adapter; public uint Id; public int Scale; }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] byte[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref ScaleGet request);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref ScaleSet request);

    public static readonly int[] Scales = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    static PathInfo Primary()
    {
        uint pathCount, modeCount;
        GetDisplayConfigBufferSizes(2, out pathCount, out modeCount);
        var paths = new PathInfo[pathCount];
        var modes = new byte[modeCount * 64];
        int error = QueryDisplayConfig(2 /* QDC_ONLY_ACTIVE_PATHS */, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (error != 0 || pathCount == 0) throw new InvalidOperationException("QueryDisplayConfig failed: " + error);
        return paths[0];
    }

    // Recommended, current, minimum and maximum scale in percent.
    public static int[] Get()
    {
        var path = Primary();
        var request = new ScaleGet { Type = -3, Size = Marshal.SizeOf(typeof(ScaleGet)), Adapter = path.SourceAdapter, Id = path.SourceId };
        int error = DisplayConfigGetDeviceInfo(ref request);
        if (error != 0) throw new InvalidOperationException("DisplayConfigGetDeviceInfo failed: " + error);
        int recommended = Math.Abs(request.Minimum);
        Func<int, int> at = step => Scales[Math.Max(0, Math.Min(Scales.Length - 1, recommended + step))];
        return new[] { Scales[recommended], at(request.Current), at(request.Minimum), at(request.Maximum) };
    }

    public static int Set(int percent)
    {
        var path = Primary();
        var get = new ScaleGet { Type = -3, Size = Marshal.SizeOf(typeof(ScaleGet)), Adapter = path.SourceAdapter, Id = path.SourceId };
        DisplayConfigGetDeviceInfo(ref get);
        int recommended = Math.Abs(get.Minimum);
        int wanted = Array.IndexOf(Scales, percent);
        if (wanted < 0) throw new ArgumentException("not a scale Windows offers: " + percent);
        var request = new ScaleSet { Type = -4, Size = Marshal.SizeOf(typeof(ScaleSet)), Adapter = path.SourceAdapter, Id = path.SourceId, Scale = wanted - recommended };
        return DisplayConfigSetDeviceInfo(ref request);
    }

    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(long point, uint flags);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    public static string Dpi()
    {
        IntPtr old = SetThreadDpiAwarenessContext(new IntPtr(-4)); // per monitor v2
        uint x, y;
        GetDpiForMonitor(MonitorFromPoint(0, 1), 0, out x, out y);
        uint system = GetDpiForSystem();
        SetThreadDpiAwarenessContext(old);
        return "monitor " + x + " dpi, system " + system + " dpi";
    }
}
'@

function Report([string]$When) {
    $scale = [OtterDisplay]::Get()
    Write-Host "$When - resolution $([OtterDisplay]::Current()) (modes: $([OtterDisplay]::Modes())); scale $($scale[1])% (recommended $($scale[0])%, from $($scale[2])% to $($scale[3])%); $([OtterDisplay]::Dpi())"
}

Report 'before'
if ($Width -gt 0 -and $Height -gt 0) {
    $result = [OtterDisplay]::SetResolution($Width, $Height)
    Write-Host "ChangeDisplaySettingsEx(${Width}x$Height): $result"
    Start-Sleep -Seconds 2
}
$result = [OtterDisplay]::Set($Percent)
Write-Host "DisplayConfigSetDeviceInfo($Percent%): $result"
Start-Sleep -Seconds 3
Report 'after'
