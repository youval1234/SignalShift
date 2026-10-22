using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Sonora;

// One reusable, non-activating window. Every switch replaces its content and expiry;
// there is no notification queue or delayed animation callback to show an old device.
public sealed class SwitchNotification : Window, IDisposable
{
    internal static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(160);
    private readonly DispatcherTimer lifetime = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch elapsed = new();
    private readonly TextBlock caption;
    private readonly TextBlock device;
    private readonly TextBlock glyph;
    private readonly Border progress;
    private bool disposed;
    internal string CurrentMessage => device.Text;

    public SwitchNotification()
    {
        Title = "Sonora switch notification";
        Width = 408; Height = 126;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Focusable = false; IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true; SnapsToDevicePixels = true;

        var card = new Border
        {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(18),
            Background = ColorBrush("#171923"), BorderBrush = ColorBrush("#3B354F"), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.25, Color = Colors.Black }
        };
        var body = new Grid { Margin = new Thickness(18, 14, 18, 10) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        glyph = new TextBlock
        {
            Text = "\uE7F5", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 21,
            Foreground = ColorBrush("#D7CBFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        body.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Background = ColorBrush("#302840"), Child = glyph, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        var copy = new StackPanel(); Grid.SetColumn(copy, 2);
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        caption = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = ColorBrush("#B8A3FF") };
        heading.Children.Add(caption);
        var brand = new TextBlock { Text = "SONORA", FontSize = 9, Foreground = ColorBrush("#74788F"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(brand, 1); heading.Children.Add(brand); copy.Children.Add(heading);
        device = new TextBlock
        {
            FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ColorBrush("#F3F0FC"),
            Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 42
        };
        copy.Children.Add(device);
        progress = new Border { Background = ColorBrush("#8BE2C5"), CornerRadius = new CornerRadius(1), Height = 2, HorizontalAlignment = HorizontalAlignment.Left, Width = 288 };
        copy.Children.Add(new Border { Height = 2, Background = ColorBrush("#292C3C"), CornerRadius = new CornerRadius(1), Child = progress });
        body.Children.Add(copy); card.Child = body; Content = card;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, -20);
            // Stay out of Alt+Tab, pass mouse clicks through, and never steal typing focus.
            SetWindowLong(handle, -20, style | 0x08000000 | 0x00000080 | 0x00000020);
        };
        lifetime.Tick += (_, _) => UpdateLifetime();
    }

    public void Present(string title, string message, AudioFlow? flow = null)
    {
        if (disposed) return;
        SetContent(title, message, flow);
        elapsed.Restart(); Opacity = 1;
        PositionOnActiveScreen();
        if (!IsVisible) Show();
        PositionOnActiveScreen();
        lifetime.Start();
    }

    internal void SetContent(string title, string message, AudioFlow? flow = null)
    {
        caption.Text = title.ToUpperInvariant(); device.Text = message;
        glyph.Text = flow == AudioFlow.Input ? "\uE720" : flow == AudioFlow.Output ? "\uE7F5" : "\uE8A9";
        progress.Width = 288;
    }

    private void UpdateLifetime()
    {
        var age = elapsed.Elapsed;
        if (age >= HoldDuration + FadeDuration) { Dismiss(); return; }
        progress.Width = 288 * Math.Max(0, 1 - age.TotalMilliseconds / HoldDuration.TotalMilliseconds);
        if (SystemParameters.ClientAreaAnimation && age > HoldDuration)
            Opacity = 1 - (age - HoldDuration).TotalMilliseconds / FadeDuration.TotalMilliseconds;
    }

    private void PositionOnActiveScreen()
    {
        var foreground = GetForegroundWindow();
        var screen = Forms.Screen.FromHandle(foreground);
        var area = screen.WorkingArea;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        // First move onto the monitor so WPF receives its DPI change before sizing.
        if (Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName)
            SetWindowPos(handle, new IntPtr(-1), area.Left, area.Top, 0, 0, 0x0011 | 0x0200);
        var scale = GetDpiForWindow(handle) / 96.0;
        if (scale <= 0) scale = 1;
        var width = Math.Min(area.Width, (int)Math.Round(Width * scale));
        var height = Math.Min(area.Height, (int)Math.Round(Height * scale));
        var gap = (int)Math.Round(32 * scale);
        var x = area.Left + (area.Width - width) / 2;
        var y = Math.Max(area.Top, area.Bottom - height - gap);
        SetWindowPos(handle, new IntPtr(-1), x, y, width, height, 0x0010 | 0x0200);
    }

    public void Dismiss()
    {
        lifetime.Stop(); elapsed.Reset(); Hide(); Opacity = 1;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Stop(); Close();
    }

    private static SolidColorBrush ColorBrush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
