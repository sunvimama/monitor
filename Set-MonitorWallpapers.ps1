#requires -Version 5.1

Add-Type -AssemblyName System.Windows.Forms

$interopSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    [PreserveSig]
    int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                     [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [PreserveSig]
    int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out IntPtr wallpaper);

    [PreserveSig]
    int GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorId);

    [PreserveSig]
    int GetMonitorDevicePathCount(out uint count);

    [PreserveSig]
    int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect displayRect);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

public sealed class WallpaperMonitor
{
    public string Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public override string ToString()
    {
        return Width + " x " + Height;
    }
}

public static class PerMonitorWallpaper
{
    private static readonly Guid DesktopWallpaperClassId =
        new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    public static WallpaperMonitor[] GetMonitors()
    {
        Type wallpaperType = Type.GetTypeFromCLSID(DesktopWallpaperClassId);
        if (wallpaperType == null)
            throw new InvalidOperationException("Windows does not provide the desktop wallpaper service.");

        IDesktopWallpaper desktop = (IDesktopWallpaper)Activator.CreateInstance(wallpaperType);
        try
        {
            uint count;
            CheckResult(desktop.GetMonitorDevicePathCount(out count));

            List<WallpaperMonitor> monitors = new List<WallpaperMonitor>();
            for (uint index = 0; index < count; index++)
            {
                IntPtr monitorIdPointer = IntPtr.Zero;
                try
                {
                    CheckResult(desktop.GetMonitorDevicePathAt(index, out monitorIdPointer));
                    string monitorId = Marshal.PtrToStringUni(monitorIdPointer);

                    NativeRect rect;
                    CheckResult(desktop.GetMonitorRECT(monitorId, out rect));
                    monitors.Add(new WallpaperMonitor
                    {
                        Id = monitorId,
                        Width = rect.Right - rect.Left,
                        Height = rect.Bottom - rect.Top
                    });
                }
                finally
                {
                    if (monitorIdPointer != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(monitorIdPointer);
                }
            }

            return monitors.ToArray();
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }
    }

    public static void SetWallpaper(string monitorId, string wallpaperPath)
    {
        Type wallpaperType = Type.GetTypeFromCLSID(DesktopWallpaperClassId);
        if (wallpaperType == null)
            throw new InvalidOperationException("Windows does not provide the desktop wallpaper service.");

        IDesktopWallpaper desktop = (IDesktopWallpaper)Activator.CreateInstance(wallpaperType);
        try
        {
            CheckResult(desktop.SetWallpaper(monitorId, wallpaperPath));
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }
    }

    private static void CheckResult(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }
}
'@

try {
    Add-Type -TypeDefinition $interopSource -ErrorAction Stop

    $monitors = [PerMonitorWallpaper]::GetMonitors()
    if ($monitors.Count -lt 2) {
        throw "At least two connected monitors are required."
    }

    $landscapeMonitors = @($monitors | Where-Object { $_.Width -gt $_.Height })
    $portraitMonitors = @($monitors | Where-Object { $_.Height -gt $_.Width })
    if ($landscapeMonitors.Count -eq 0 -or $portraitMonitors.Count -eq 0) {
        throw "Could not find both a landscape monitor and a portrait monitor. Check the display orientation in Windows Display settings."
    }

    $imageDialog = New-Object System.Windows.Forms.OpenFileDialog
    $imageDialog.Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"
    $imageDialog.Title = "Choose the wallpaper for landscape monitor(s)"
    if ($imageDialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        exit 0
    }
    $landscapeImage = $imageDialog.FileName

    $imageDialog.Title = "Choose the wallpaper for portrait monitor(s)"
    if ($imageDialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        exit 0
    }
    $portraitImage = $imageDialog.FileName
    $imageDialog.Dispose()

    $assignments = @()
    foreach ($monitor in $landscapeMonitors) {
        $assignments += "Landscape monitor ($($monitor.Width) x $($monitor.Height)): $landscapeImage"
    }
    foreach ($monitor in $portraitMonitors) {
        $assignments += "Portrait monitor ($($monitor.Width) x $($monitor.Height)): $portraitImage"
    }

    $message = "Apply these wallpapers?`r`n`r`n" + ($assignments -join "`r`n")
    $answer = [System.Windows.Forms.MessageBox]::Show(
        $message,
        "Set monitor wallpapers",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
        exit 0
    }

    foreach ($monitor in $landscapeMonitors) {
        [PerMonitorWallpaper]::SetWallpaper($monitor.Id, $landscapeImage)
    }
    foreach ($monitor in $portraitMonitors) {
        [PerMonitorWallpaper]::SetWallpaper($monitor.Id, $portraitImage)
    }

    [System.Windows.Forms.MessageBox]::Show(
        "Wallpapers were set successfully.",
        "Done",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        $_.Exception.Message,
        "Could not set wallpapers",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
    exit 1
}
