using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Media;

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

    [PreserveSig]
    int SetPosition(DesktopWallpaperPosition position);
}

internal enum DesktopWallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal sealed class WallpaperMonitor
{
    public string Id;
    public int Width;
    public int Height;
}

internal sealed class WallpaperSettings
{
    public string LandscapePath;
    public string PortraitPath;

    private static string SettingsPath
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MonitorWallpaper",
                "settings.ini");
        }
    }

    public static WallpaperSettings Load()
    {
        string path = SettingsPath;
        if (!File.Exists(path))
            return new WallpaperSettings();

        string[] lines = File.ReadAllLines(path);
        if (lines.Length != 2)
            throw new InvalidDataException("The saved wallpaper settings are invalid. Please choose both images again.");

        try
        {
            return new WallpaperSettings
            {
                LandscapePath = Encoding.UTF8.GetString(Convert.FromBase64String(lines[0])),
                PortraitPath = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1]))
            };
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The saved wallpaper settings are invalid. Please choose both images again.", exception);
        }
    }

    public void Save()
    {
        string directory = Path.GetDirectoryName(SettingsPath);
        Directory.CreateDirectory(directory);
        File.WriteAllLines(SettingsPath, new[]
        {
            Convert.ToBase64String(Encoding.UTF8.GetBytes(LandscapePath)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(PortraitPath))
        });
    }
}

internal static class WallpaperService
{
    private static readonly Guid DesktopWallpaperClassId =
        new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    public static WallpaperMonitor[] GetMonitors()
    {
        IDesktopWallpaper desktop = CreateDesktopWallpaper();
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

    public static void Apply(WallpaperSettings settings)
    {
        if (String.IsNullOrWhiteSpace(settings.PortraitPath))
            throw new InvalidOperationException("Choose a portrait wallpaper first.");

        if (!File.Exists(settings.PortraitPath))
            throw new FileNotFoundException("The saved portrait wallpaper could not be found. Open the app and choose it again.");

        bool hasLandscapeVideo = !String.IsNullOrWhiteSpace(settings.LandscapePath) &&
            String.Equals(Path.GetExtension(settings.LandscapePath), ".mp4", StringComparison.OrdinalIgnoreCase);
        if (hasLandscapeVideo && !File.Exists(settings.LandscapePath))
            throw new FileNotFoundException("The saved landscape video could not be found. Open the app and choose it again.");

        WallpaperMonitor[] monitors = GetMonitors();
        bool hasLandscape = false;
        bool hasPortrait = false;
        foreach (WallpaperMonitor monitor in monitors)
        {
            hasLandscape |= monitor.Width >= monitor.Height;
            hasPortrait |= monitor.Height > monitor.Width;
        }

        if (!hasLandscape || !hasPortrait)
            throw new InvalidOperationException(
                "A landscape and a portrait monitor are both required. Check that both displays are connected and set to the correct orientation.");

        IDesktopWallpaper desktop = CreateDesktopWallpaper();
        try
        {
            CheckResult(desktop.SetPosition(DesktopWallpaperPosition.Fill));
            foreach (WallpaperMonitor monitor in monitors)
            {
                string path = monitor.Height > monitor.Width || hasLandscapeVideo
                    ? settings.PortraitPath
                    : settings.LandscapePath;
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    path = settings.PortraitPath;
                CheckResult(desktop.SetWallpaper(monitor.Id, path));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }
    }

    private static IDesktopWallpaper CreateDesktopWallpaper()
    {
        Type wallpaperType = Type.GetTypeFromCLSID(DesktopWallpaperClassId);
        if (wallpaperType == null)
            throw new InvalidOperationException("Windows does not provide the desktop wallpaper service.");

        return (IDesktopWallpaper)Activator.CreateInstance(wallpaperType);
    }

    private static void CheckResult(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }
}

internal sealed class WallpaperForm : Form
{
    private readonly TextBox landscapePath = new TextBox();
    private readonly TextBox portraitPath = new TextBox();
    private readonly CheckBox startWithWindows = new CheckBox();

    public WallpaperForm()
    {
        Text = "Monitor Wallpaper";
        Width = 700;
        Height = 250;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;

        Label intro = new Label
        {
            Text = "Use an MP4 video on landscape displays and a still image on portrait displays.",
            Left = 16,
            Top = 16,
            Width = 570,
            Height = 24
        };
        Controls.Add(intro);

        AddImagePicker("Landscape video:", landscapePath, 52, "Choose landscape video", true);
        AddImagePicker("Portrait image:", portraitPath, 96, "Choose portrait wallpaper", false);

        startWithWindows.Text = "Apply these wallpapers automatically when I sign in to Windows";
        startWithWindows.Left = 18;
        startWithWindows.Top = 143;
        startWithWindows.Width = 640;
        Controls.Add(startWithWindows);

        Button applyButton = new Button
        {
            Text = "Save and apply",
            Left = 508,
            Top = 177,
            Width = 165,
            Height = 30
        };
        applyButton.Click += ApplyButtonClick;
        Controls.Add(applyButton);

        AcceptButton = applyButton;

        WallpaperSettings settings = WallpaperSettings.Load();
        landscapePath.Text = settings.LandscapePath ?? String.Empty;
        portraitPath.Text = settings.PortraitPath ?? String.Empty;
        startWithWindows.Checked = StartupManager.IsEnabled() ||
            String.IsNullOrWhiteSpace(settings.PortraitPath);
    }

    private void AddImagePicker(string label, TextBox pathBox, int top, string dialogTitle, bool video)
    {
        Label imageLabel = new Label
        {
            Text = label,
            Left = 18,
            Top = top + 5,
            Width = 130,
            Height = 22
        };
        pathBox.Left = 150;
        pathBox.Top = top;
        pathBox.Width = 380;
        pathBox.ReadOnly = true;

        Button browseButton = new Button
        {
            Text = "Browse...",
            Left = 538,
            Top = top - 1,
            Width = 90,
            Height = 25
        };
        browseButton.Click += delegate
        {
            using (System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Title = dialogTitle;
                dialog.Filter = video
                    ? "MP4 video files|*.mp4|All files|*.*"
                    : "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";
                if (File.Exists(pathBox.Text))
                    dialog.FileName = pathBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    pathBox.Text = dialog.FileName;
            }
        };

        Controls.Add(imageLabel);
        Controls.Add(pathBox);
        Controls.Add(browseButton);
        if (video)
        {
            Button clearButton = new Button
            {
                Text = "Clear",
                Left = 632,
                Top = top - 1,
                Width = 48,
                Height = 25
            };
            clearButton.Click += delegate { pathBox.Clear(); };
            Controls.Add(clearButton);
        }
    }

    private void ApplyButtonClick(object sender, EventArgs e)
    {
        try
        {
            WallpaperSettings settings = new WallpaperSettings
            {
                LandscapePath = landscapePath.Text,
                PortraitPath = portraitPath.Text
            };
            WallpaperService.Apply(settings);
            settings.Save();
            StartupManager.SetEnabled(startWithWindows.Checked);
            VideoWallpaperService.Restart(settings.LandscapePath);
            MessageBox.Show(this, "Wallpapers saved and applied successfully.",
                "Monitor Wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not apply wallpapers",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class VideoWallpaperService
{
    private const string StopEventName = @"Local\MonitorWallpaper.Video.Stop";
    private const string PlayerMutexName = @"Local\MonitorWallpaper.Video.Mutex";

    public static void Restart(string videoPath)
    {
        StopExisting();
        if (String.IsNullOrWhiteSpace(videoPath) ||
            !String.Equals(Path.GetExtension(videoPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            return;

        string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(videoPath));
        Process.Start(new ProcessStartInfo
        {
            FileName = Application.ExecutablePath,
            Arguments = "--video64 " + encodedPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void StopExisting()
    {
        try
        {
            using (EventWaitHandle stopEvent = EventWaitHandle.OpenExisting(StopEventName))
                stopEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public static void Run(string videoPath)
    {
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("The selected landscape video could not be found.", videoPath);

        using (Mutex playerMutex = new Mutex(false, PlayerMutexName))
        {
            bool ownsMutex = false;
            try
            {
                try
                {
                    ownsMutex = playerMutex.WaitOne(TimeSpan.FromSeconds(30));
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                    throw new TimeoutException("The previous video wallpaper did not close in time.");

                using (EventWaitHandle stopEvent = new EventWaitHandle(
                    false, EventResetMode.AutoReset, StopEventName))
                {
                    System.Windows.Application app = new System.Windows.Application
                    {
                        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                    };
                    WallpaperMonitor[] monitors = WallpaperService.GetMonitors();
                    List<VideoWallpaperWindow> windows = new List<VideoWallpaperWindow>();
                    foreach (WallpaperMonitor monitor in monitors)
                    {
                        if (monitor.Width < monitor.Height)
                            continue;

                        windows.Add(new VideoWallpaperWindow(videoPath, monitor));
                    }

                    if (windows.Count == 0)
                        throw new InvalidOperationException("No landscape monitor is connected for video playback.");

                    app.Startup += delegate
                    {
                        foreach (VideoWallpaperWindow window in windows)
                            window.Show();
                    };

                    Thread stopThread = new Thread(delegate()
                    {
                        stopEvent.WaitOne();
                        app.Dispatcher.BeginInvoke(new Action(app.Shutdown));
                    });
                    stopThread.IsBackground = true;
                    stopThread.Start();
                    app.Run();
                }
            }
            finally
            {
                if (ownsMutex)
                    playerMutex.ReleaseMutex();
            }
        }
    }
}

internal sealed class VideoWallpaperWindow : System.Windows.Window
{
    private static readonly IntPtr InvalidWindowHandle = IntPtr.Zero;
    private const uint SpawnWorkerMessage = 0x052C;
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000);
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;

    private readonly WallpaperMonitor monitor;
    private readonly string videoPath;
    private readonly System.Windows.Controls.MediaElement player;

    public VideoWallpaperWindow(string videoPath, WallpaperMonitor targetMonitor)
    {
        monitor = targetMonitor;
        this.videoPath = videoPath;
        WindowStyle = System.Windows.WindowStyle.None;
        ResizeMode = System.Windows.ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = monitor.Width;
        Height = monitor.Height;
        Left = 0;
        Top = 0;
        Background = Brushes.Black;

        player = new System.Windows.Controls.MediaElement
        {
            LoadedBehavior = System.Windows.Controls.MediaState.Manual,
            UnloadedBehavior = System.Windows.Controls.MediaState.Stop,
            Stretch = Stretch.UniformToFill,
            IsMuted = true
        };
        player.MediaOpened += delegate { player.Play(); };
        player.Loaded += delegate { player.Play(); };
        player.MediaEnded += delegate
        {
            player.Position = TimeSpan.Zero;
            player.Play();
        };
        player.MediaFailed += delegate(object sender, System.Windows.ExceptionRoutedEventArgs eventArgs)
        {
            System.Windows.Forms.MessageBox.Show(eventArgs.ErrorException.Message, "Video could not be played",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            System.Windows.Application.Current.Shutdown();
        };
        Content = player;
        SourceInitialized += AttachToDesktop;
    }

    private void AttachToDesktop(object sender, EventArgs e)
    {
        IntPtr workerWindow = FindWallpaperHost();
        if (workerWindow == InvalidWindowHandle)
            throw new InvalidOperationException("Could not locate the Windows desktop wallpaper host.");

        IntPtr handle = new WindowInteropHelper(this).Handle;
        SetParent(handle, workerWindow);

        long style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        style = (style | WsChild) & ~WsPopup;
        SetWindowLongPtr(handle, GwlStyle, new IntPtr(style));

        NativeRect hostRect;
        if (!GetWindowRect(workerWindow, out hostRect))
            throw new InvalidOperationException("Could not determine the desktop wallpaper host bounds.");

        NativeRect targetRect;
        IDesktopWallpaper desktop = GetDesktopWallpaper();
        try
        {
            CheckResult(desktop.GetMonitorRECT(monitor.Id, out targetRect));
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }

        int left = targetRect.Left - hostRect.Left;
        int top = targetRect.Top - hostRect.Top;
        int width = targetRect.Right - targetRect.Left;
        int height = targetRect.Bottom - targetRect.Top;
        if (!SetWindowPos(handle, IntPtr.Zero, left, top, width, height,
            SwpNoZOrder | SwpShowWindow | SwpFrameChanged))
            throw new InvalidOperationException("Could not position the video wallpaper on its monitor.");

        player.Source = new Uri(videoPath, UriKind.Absolute);
    }

    private static IntPtr FindWallpaperHost()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            IntPtr result;
            SendMessageTimeout(progman, SpawnWorkerMessage, IntPtr.Zero, new IntPtr(0xD),
                0, 1000, out result);
        }

        IntPtr host = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            IntPtr shellView = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                host = FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return host;
    }

    private static IDesktopWallpaper GetDesktopWallpaper()
    {
        Type wallpaperType = Type.GetTypeFromCLSID(
            new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
        if (wallpaperType == null)
            throw new InvalidOperationException("Windows does not provide the desktop wallpaper service.");
        return (IDesktopWallpaper)Activator.CreateInstance(wallpaperType);
    }

    private static void CheckResult(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr window, int index);

    private static IntPtr GetWindowLongPtr(IntPtr window, int index)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : GetWindowLong32(window, index);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr window, int index, IntPtr value);

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(window, index, value) : SetWindowLong32(window, index, value);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MonitorWallpaper";

    public static bool IsEnabled()
    {
        using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
        {
            return key != null && key.GetValue(ValueName) != null;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (key == null)
                throw new InvalidOperationException("Could not configure Windows startup for this app.");

            if (enabled)
            {
                string executablePath = Application.ExecutablePath;
                key.SetValue(ValueName, "\"" + executablePath + "\" --apply");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool applyAtStartup = args.Length == 1 &&
            String.Equals(args[0], "--apply", StringComparison.OrdinalIgnoreCase);
        if (applyAtStartup)
        {
            try
            {
                WallpaperSettings settings = WallpaperSettings.Load();
                WallpaperService.Apply(settings);
                VideoWallpaperService.Restart(settings.LandscapePath);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Monitor Wallpaper startup error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        if (args.Length == 2 && String.Equals(args[0], "--video64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string videoPath = Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));
                VideoWallpaperService.Run(videoPath);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Monitor Wallpaper video error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        try
        {
            Application.Run(new WallpaperForm());
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Monitor Wallpaper error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
