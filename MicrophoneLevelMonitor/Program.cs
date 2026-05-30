using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace MicrophoneLevelMonitor;

static class Program
{
    private const string AppMutexName = "MicrophoneLevelMonitor-AF648ED7-1F0E-4A2B-B919-8521846E83A1";
    private const string AppUserModelId = "MicrophoneLevelMonitor";
    private const string NotificationTitle = "Mic Check!";
    private const int PollIntervalMilliseconds = 5 * 1000;
    private const int LowVolumeReminderMilliseconds = 30 * 1000;
    private const float LowVolumeThresholdPercent = 75f;
    private const float ResetVolumeScalar = 0.805f;

    private static float? previousVolume;
    private static DateTime previousVolumeChangeTimestamp;

    private static NotifyIcon? notifyIcon;
    private static MMDeviceEnumerator? enumerator;
    private static MMDevice? defaultMic;
    private static string? defaultMicId;
    private static Icon? appIcon;
    private static Icon? ownedTrayIcon;
    private static TrayMenuFlyout? trayMenuFlyout;
    private static AppSettings appSettings = new();

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        using var appMutex = new Mutex(initiallyOwned: true, AppMutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        // this has to come first
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);
        appSettings = LoadAppSettings();

        using var form = new Forms.Form { WindowState = FormWindowState.Minimized, ShowInTaskbar = false };
        using var contextMenu = CreateLegacyContextMenu();
        using var timer = new Forms.Timer { Interval = PollIntervalMilliseconds };

        form.Load += (sender, args) =>
        {
            form.Visible = false;
            form.ShowInTaskbar = false;
        };

        notifyIcon = new NotifyIcon
        {
            Icon = appIcon = CreateAppIcon(),
            Visible = true,
            Text = "Microphone Level Monitor"
        };

        if (appSettings.UseModernTrayMenu)
        {
            notifyIcon.MouseUp += NotifyIcon_MouseUp;
        }
        else
        {
            notifyIcon.ContextMenuStrip = contextMenu;
            notifyIcon.DoubleClick += (sender, args) => OpenSoundSettings();
        }

        enumerator = new MMDeviceEnumerator();
        ResolveDefaultMic();

        timer.Tick += (s, e) => ProcessCurrentVolume();
        timer.Start();

        ProcessCurrentVolume();

        try
        {
            Forms.Application.Run();
        }
        finally
        {
            Cleanup();
        }
    }

    private static ContextMenuStrip CreateLegacyContextMenu()
    {
        var contextMenu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer()
        };

        contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication()));
        contextMenu.Items.Add(new ToolStripMenuItem("80%", null, (s, e) => ResetMicrophoneVolume()));

        return contextMenu;
    }

    private static AppSettings LoadAppSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            return settings ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            return new AppSettings();
        }
    }

    private static void NotifyIcon_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            ShowModernTrayMenu();
        }
    }

    private static void ShowModernTrayMenu()
    {
        if (trayMenuFlyout is { IsVisible: true })
        {
            trayMenuFlyout.CloseIfOpen();
            return;
        }

        trayMenuFlyout = new TrayMenuFlyout(
            ResetMicrophoneVolume,
            OpenSoundSettings,
            ExitApplication);
        trayMenuFlyout.Closed += (sender, args) => trayMenuFlyout = null;
        trayMenuFlyout.ShowNearTray(Forms.Cursor.Position, Forms.Screen.FromPoint(Forms.Cursor.Position));
    }

    private static void ResetMicrophoneVolume()
    {
        ResolveDefaultMic();
        if (defaultMic == null)
        {
            return;
        }

        try
        {
            defaultMic.AudioEndpointVolume.MasterVolumeLevelScalar = ResetVolumeScalar;
            ProcessCurrentVolume();
        }
        catch (COMException ex)
        {
            Debug.WriteLine(ex);
            ResetDefaultMic();
        }
    }

    private static void ExitApplication()
    {
        if (notifyIcon != null)
        {
            notifyIcon.Visible = false;
        }

        Forms.Application.Exit();
    }

    private static void OpenSoundSettings()
    {
        try
        {
            // Modern sound settings (Windows 10+)
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch
        {
            // Fallback: Open classic Sound Control Panel
            Process.Start("rundll32.exe", "shell32.dll,Control_RunDLL mmsys.cpl,,0");
        }
    }

    private static void ProcessCurrentVolume()
    {
        if (notifyIcon == null)
        {
            return;
        }

        ResolveDefaultMic();
        if (defaultMic == null)
        {
            notifyIcon.Text = "No input device found.";
            SetTrayIcon(SystemIcons.Warning);
            previousVolume = null;
            return;
        }

        float currentVolume;
        try
        {
            currentVolume = defaultMic.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
        }
        catch (COMException ex)
        {
            Debug.WriteLine(ex);
            ResetDefaultMic();
            return;
        }

        notifyIcon.Text = $"Microphone volume is {(int)currentVolume}%";
        Debug.WriteLine(previousVolume + " -> " + currentVolume);

        if (previousVolume != currentVolume)
        {
            Debug.WriteLine("Volume changed");
            previousVolumeChangeTimestamp = DateTime.Now;

            notifyIcon.ShowBalloonTip(1000, NotificationTitle, $"Microphone volume is {(int)currentVolume}%", ToolTipIcon.None);
            SetTrayIcon(currentVolume < 100f ? CreateIconWithText(currentVolume) : appIcon, ownsIcon: currentVolume < 100f);
        }
        else if (currentVolume < LowVolumeThresholdPercent &&
            DateTime.Now.Subtract(previousVolumeChangeTimestamp).TotalMilliseconds > LowVolumeReminderMilliseconds)
        {
            notifyIcon.ShowBalloonTip(1000, NotificationTitle, $"Microphone volume is {(int)currentVolume}%", ToolTipIcon.Warning);
            previousVolumeChangeTimestamp = DateTime.Now;
        }

        previousVolume = currentVolume;
    }

    private static void ResolveDefaultMic()
    {
        if (enumerator == null)
        {
            return;
        }

        MMDevice? currentDefaultMic = null;
        try
        {
            currentDefaultMic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }
        catch (COMException ex)
        {
            Debug.WriteLine(ex);
        }

        if (currentDefaultMic == null)
        {
            ResetDefaultMic();
            return;
        }

        if (currentDefaultMic.ID == defaultMicId)
        {
            currentDefaultMic.Dispose();
            return;
        }

        ResetDefaultMic();
        defaultMic = currentDefaultMic;
        defaultMicId = currentDefaultMic.ID;
        previousVolume = null;
    }

    private static void ResetDefaultMic()
    {
        defaultMic?.Dispose();
        defaultMic = null;
        defaultMicId = null;
    }

    private static void SetTrayIcon(Icon? icon, bool ownsIcon = false)
    {
        if (notifyIcon == null)
        {
            if (ownsIcon)
            {
                icon?.Dispose();
            }

            return;
        }

        var previousOwnedIcon = ownedTrayIcon;
        notifyIcon.Icon = icon;
        ownedTrayIcon = ownsIcon ? icon : null;
        previousOwnedIcon?.Dispose();
    }

    private static Icon CreateAppIcon()
    {
        return Icon.ExtractAssociatedIcon(Forms.Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    }

    private static Icon CreateIconWithText(float currentVolume)
    {
        const int iconSize = 32;
        const int dpi = 96;

        var background = currentVolume < LowVolumeThresholdPercent ? new SolidColorBrush(Colors.Red) : null;

        var textBlock = new TextBlock
        {
            Text = ((int)currentVolume).ToString(),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = background,
            Width = iconSize,
            Height = iconSize,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        textBlock.Measure(new System.Windows.Size(iconSize, iconSize));
        textBlock.Arrange(new Rect(0, 0, iconSize, iconSize));

        var bitmap = new RenderTargetBitmap(iconSize, iconSize, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(textBlock);

        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        stream.Position = 0;

        using var bmp = new Bitmap(stream);
        var hIcon = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void Cleanup()
    {
        if (notifyIcon != null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            notifyIcon = null;
        }

        trayMenuFlyout?.CloseIfOpen();
        trayMenuFlyout = null;
        ownedTrayIcon?.Dispose();
        ownedTrayIcon = null;
        appIcon?.Dispose();
        appIcon = null;
        ResetDefaultMic();
        enumerator?.Dispose();
        enumerator = null;
    }

    private sealed class AppSettings
    {
        public bool UseModernTrayMenu { get; init; } = true;
    }

    private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer()
            : base(new TrayMenuColorTable())
        {
        }
    }

    private sealed class TrayMenuColorTable : ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(231, 231, 231);
        private static readonly System.Drawing.Color Pressed = System.Drawing.Color.FromArgb(218, 218, 218);
        private static readonly System.Drawing.Color Border = System.Drawing.Color.FromArgb(196, 196, 196);

        public override System.Drawing.Color MenuItemBorder => Border;
        public override System.Drawing.Color MenuItemPressedGradientBegin => Pressed;
        public override System.Drawing.Color MenuItemPressedGradientEnd => Pressed;
        public override System.Drawing.Color MenuItemPressedGradientMiddle => Pressed;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
    }
}
