using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms; // Alias WinForms
using NAudio.CoreAudioApi;

namespace MicrophoneLevelMonitor;

static class Program
{
    private static float? previousVolume;
    private static DateTime previousVolumeChangeTimestamp;

    private static NotifyIcon notifyIcon;
    private static MMDevice defaultMic;

    [DllImport("shell32.dll", SetLastError = true)]
    static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // this has to come first
        SetCurrentProcessExplicitAppUserModelID("Mic Check!");

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);

        // Create a hidden form just to keep the application running
        using (var form = new Form { WindowState = FormWindowState.Minimized, ShowInTaskbar = false })
        {
            form.Load += (sender, args) =>
            {
                form.Visible = false;
                form.ShowInTaskbar = false;
            };

            notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = ""
            };

            // Add a context menu
            var contextMenu = new ContextMenuStrip();

            // Add a context menu item to exit the application
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) =>
            {
                notifyIcon.Visible = false;
                Forms.Application.Exit();
            });
            contextMenu.Items.Add(exitItem);

            // Add a context menu item to reset the volume
            var resetItem = new ToolStripMenuItem("80%", null, (s, e) =>
            {
                if (defaultMic != null)
                {
                    defaultMic.AudioEndpointVolume.MasterVolumeLevelScalar = 0.805f;
                }
            });
            contextMenu.Items.Add(resetItem);

            // set the context menu
            notifyIcon.ContextMenuStrip = contextMenu;

            // Handle the DoubleClick event to open sound settings
            notifyIcon.DoubleClick += (sender, args) =>
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
            };

            // Setup the default audio device (input)
            var enumerator = new MMDeviceEnumerator();

            // You can choose DefaultDevice or DefaultCommunicationsDevice depending on your preference:
            defaultMic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

            // Set up a timer to update the tooltip
            var timer = new System.Windows.Forms.Timer { Interval = 5 * 1000 }; // update every n seconds
            timer.Tick += (s, e) =>
            {
                ProcessCurrentVolume();
            };
            timer.Start();

            // kick off the initial volume check
            ProcessCurrentVolume();

            Forms.Application.Run();
        }

        static void ProcessCurrentVolume()
        {
            // if there's not a notifyIcon, we're done
            if (notifyIcon == null) return;

            // if there's not a default mic, we're done
            if (defaultMic == null)
            {
                notifyIcon.Text = "No input device found.";
                return;
            }

            // Volume range is typically 0.0 to 1.0, multiply by 100 for percentage
            float currentVolume = defaultMic.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;

            Debug.WriteLine(previousVolume + " -> " + currentVolume);

            // did the volume change?
            if (previousVolume != currentVolume)
            {
                Debug.WriteLine("Volume changed");
                previousVolumeChangeTimestamp = DateTime.Now;

                notifyIcon.Icon = SystemIcons.Information;
                notifyIcon.ShowBalloonTip(1000, "", $"Microphone volume is {(int)currentVolume}%", ToolTipIcon.None);

                if (currentVolume < 100f)
                {
                    Debug.WriteLine("Volume < 100");

                    // Render text using WPF
                    Thread iconThread = new Thread(() =>
                    {
                        notifyIcon.Icon = CreateIconWithText(currentVolume);
                    });

                    iconThread.SetApartmentState(ApartmentState.STA);
                    iconThread.Start();
                }
                else
                {
                    notifyIcon.Icon = null;
                }
            }

            // otherwise, if the volume is below the threshold and it's been more than a minute, show the balloon tip again
            else if (currentVolume < 75 && DateTime.Now.Subtract(previousVolumeChangeTimestamp).TotalMilliseconds > 30 * 1000)
            {
                notifyIcon.ShowBalloonTip(1000, "", $"Microphone volume is {(int)currentVolume}%", ToolTipIcon.Warning);
                previousVolumeChangeTimestamp = DateTime.Now;
            }

            // update previousVolume
            previousVolume = currentVolume;
        }

        static Icon CreateIconWithText(float currentVolume)
        {
            int iconSize = 32;
            int dpi = 96;

            // red background if volume too low
            var background = currentVolume < 75 ? new SolidColorBrush(Colors.Red) : null;

            var textBlock = new TextBlock
            {
                Text = ((int)currentVolume).ToString(),
                FontSize = 24, // Adjusted for visibility
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

            using (MemoryStream stream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                using (Bitmap bmp = new Bitmap(stream))
                {
                    return Icon.FromHandle(bmp.GetHicon());
                }
            }
        }
    }
}