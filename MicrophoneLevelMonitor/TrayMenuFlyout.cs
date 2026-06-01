using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfControl = System.Windows.Controls.Control;
using WpfPoint = System.Windows.Point;

namespace MicrophoneLevelMonitor;

internal sealed class TrayMenuFlyout : Window
{
    private const double MenuWidth = 238;
    private const double MenuItemHeight = 24;
    private const double CursorHorizontalOffset = 36;
    private const double TrayIconHorizontalOffset = -10;
    private const double TrayIconVerticalOffset = 2;
    private const double ChromePadding = 8;
    private const double ScreenMargin = 8;
    private bool isClosing;

    public TrayMenuFlyout(Action setVolume, Action openSoundSettings, Action exitApplication)
    {
        AllowsTransparency = true;
        Background = Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;
        Topmost = true;
        Width = MenuWidth;
        WindowStyle = WindowStyle.None;

        Deactivated += (sender, args) => CloseIfOpen();
        KeyDown += (sender, args) =>
        {
            if (args.Key == Key.Escape)
            {
                CloseIfOpen();
            }
        };

        Content = CreateChrome(CreateContent(setVolume, openSoundSettings, exitApplication));
    }

    public void ShowNearTray(System.Drawing.Point cursorPosition, Forms.Screen screen)
    {
        Left = -10000;
        Top = -10000;
        Show();
        Activate();
        UpdateLayout();

        var workingArea = ToDeviceIndependentRect(screen.WorkingArea);
        var cursor = ToDeviceIndependentPoint(cursorPosition);
        var width = ActualWidth > 0 ? ActualWidth : MenuWidth;
        var height = ActualHeight;

        var x = GetHorizontalPosition(workingArea, cursor, width);
        var y = GetVerticalPosition(workingArea, cursor, height);

        Left = x;
        Top = y;
    }

    public void ShowNearTrayIcon(System.Drawing.Rectangle iconBounds)
    {
        Left = -10000;
        Top = -10000;
        Show();
        Activate();
        UpdateLayout();

        var workingArea = ToDeviceIndependentRect(Forms.Screen.FromRectangle(iconBounds).WorkingArea);
        var icon = ToDeviceIndependentRect(iconBounds);
        var width = ActualWidth > 0 ? ActualWidth : MenuWidth;
        var height = ActualHeight;

        var x = GetIconHorizontalPosition(workingArea, icon, width);
        var y = GetIconVerticalPosition(workingArea, icon, height);

        Left = x;
        Top = y;
    }

    private static Border CreateChrome(UIElement child)
    {
        var palette = ThemePalette.Current();

        return new Border
        {
            Background = Media.Brushes.Transparent,
            Padding = new Thickness(ChromePadding),
            Child = new Border
            {
                Background = palette.Background,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 16,
                    Direction = 270,
                    Opacity = palette.ShadowOpacity,
                    ShadowDepth = 3,
                    Color = Media.Colors.Black
                },
                Child = child
            }
        };
    }

    private static StackPanel CreateContent(Action setVolume, Action openSoundSettings, Action exitApplication)
    {
        var palette = ThemePalette.Current();
        var panel = new StackPanel
        {
            Margin = new Thickness(5)
        };

        panel.Children.Add(CreateMenuButton("Set to 80%", setVolume, palette));
        panel.Children.Add(CreateMenuButton("Sound settings", openSoundSettings, palette));
        panel.Children.Add(CreateSeparator(palette));
        panel.Children.Add(CreateMenuButton("Exit", exitApplication, palette));

        return panel;
    }

    private static WpfButton CreateMenuButton(string label, Action action, ThemePalette palette)
    {
        var button = new WpfButton
        {
            Content = new TextBlock
            {
                Text = label,
                FontFamily = new Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12.5,
                Foreground = palette.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            },
            Cursor = System.Windows.Input.Cursors.Hand,
            Height = MenuItemHeight,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 0, 10, 0)
        };

        button.Style = CreateMenuButtonStyle(palette);
        button.Click += (sender, args) =>
        {
            action();
            if (Window.GetWindow(button) is TrayMenuFlyout flyout)
            {
                flyout.CloseIfOpen();
            }
        };

        return button;
    }

    public void CloseIfOpen()
    {
        if (isClosing || !IsVisible)
        {
            return;
        }

        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        isClosing = true;
        base.OnClosing(e);
    }

    private static Style CreateMenuButtonStyle(ThemePalette palette)
    {
        var style = new Style(typeof(WpfButton));
        style.Setters.Add(new Setter(WpfControl.BackgroundProperty, Media.Brushes.Transparent));
        style.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Media.Brushes.Transparent));
        style.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(WpfControl.TemplateProperty, CreateMenuButtonTemplate()));

        var hoverTrigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, palette.Hover));
        style.Triggers.Add(hoverTrigger);

        var pressedTrigger = new Trigger
        {
            Property = WpfButtonBase.IsPressedProperty,
            Value = true
        };
        pressedTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, palette.Pressed));
        style.Triggers.Add(pressedTrigger);

        return style;
    }

    private static ControlTemplate CreateMenuButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background))
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        contentPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentPresenter.SetBinding(MarginProperty, new System.Windows.Data.Binding(nameof(Padding))
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        border.AppendChild(contentPresenter);

        return new ControlTemplate(typeof(WpfButton))
        {
            VisualTree = border
        };
    }

    private static Border CreateSeparator(ThemePalette palette)
    {
        return new Border
        {
            Background = palette.Separator,
            Height = 1,
            Margin = new Thickness(8, 3, 8, 3)
        };
    }

    private double GetVerticalPosition(Rect workingArea, WpfPoint cursor, double height)
    {
        var y = cursor.Y - height - ScreenMargin;
        return Math.Clamp(y, workingArea.Top + ScreenMargin, workingArea.Bottom - height - ScreenMargin);
    }

    private static double GetHorizontalPosition(Rect workingArea, WpfPoint cursor, double width)
    {
        var leftEdge = cursor.X - width + CursorHorizontalOffset;
        var minLeft = workingArea.Left + ScreenMargin;
        var maxLeft = workingArea.Right - width - ScreenMargin;

        return Math.Clamp(leftEdge, minLeft, maxLeft);
    }

    private static double GetIconHorizontalPosition(Rect workingArea, Rect icon, double width)
    {
        var minLeft = workingArea.Left + ScreenMargin;
        var maxLeft = workingArea.Right - width - ScreenMargin;

        return Math.Clamp(icon.Left + TrayIconHorizontalOffset, minLeft, maxLeft);
    }

    private static double GetIconVerticalPosition(Rect workingArea, Rect icon, double height)
    {
        var y = icon.Top - height - ScreenMargin + TrayIconVerticalOffset;
        return Math.Clamp(y, workingArea.Top + ScreenMargin, workingArea.Bottom - height - ScreenMargin);
    }

    private Rect ToDeviceIndependentRect(System.Drawing.Rectangle rectangle)
    {
        var topLeft = ToDeviceIndependentPoint(new System.Drawing.Point(rectangle.Left, rectangle.Top));
        var bottomRight = ToDeviceIndependentPoint(new System.Drawing.Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private WpfPoint ToDeviceIndependentPoint(System.Drawing.Point point)
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Media.Matrix.Identity;
        return transform.Transform(new WpfPoint(point.X, point.Y));
    }

    private sealed class ThemePalette
    {
        private ThemePalette(bool isDarkMode)
        {
            if (isDarkMode)
            {
                Background = Brush(32, 33, 36);
                Border = Brush(64, 67, 72);
                Text = Brush(232, 234, 237);
                Hover = Brush(64, 64, 64);
                Pressed = Brush(78, 78, 78);
                Separator = Brush(64, 67, 72);
                ShadowOpacity = 0.36;
            }
            else
            {
                Background = Brush(250, 250, 250);
                Border = Brush(213, 217, 222);
                Text = Brush(31, 35, 40);
                Hover = Brush(231, 231, 231);
                Pressed = Brush(218, 218, 218);
                Separator = Brush(225, 229, 234);
                ShadowOpacity = 0.18;
            }
        }

        public SolidColorBrush Background { get; }
        public SolidColorBrush Border { get; }
        public SolidColorBrush Text { get; }
        public SolidColorBrush Hover { get; }
        public SolidColorBrush Pressed { get; }
        public SolidColorBrush Separator { get; }
        public double ShadowOpacity { get; }

        public static ThemePalette Current()
        {
            return new ThemePalette(IsDarkModeEnabled());
        }

        private static SolidColorBrush Brush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static bool IsDarkModeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
