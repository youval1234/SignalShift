using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;

namespace SonarShift;

public sealed partial class MainWindow
{
    private StackPanel CaptionControls()
    {
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        Button Caption(string label, string geometry, System.Action action, bool close = false)
        {
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), StrokeThickness = 1, Width = 12, Height = 12, Stretch = Stretch.None, SnapsToDevicePixels = true };
            var button = Button(path, action);
            path.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new Binding("Foreground") { Source = button });
            button.Style = (Style)FindResource(close ? "CaptionClose" : "CaptionButton");
            button.ToolTip = label; button.Tag = "WindowCaption";
            AutomationProperties.SetName(button, label);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            controls.Children.Add(button); return button;
        }
        Caption("Minimize to tray", "M1,6.5 L11,6.5", MinimizeToTray);
        var maximize = Caption("Maximize", "M1.5,1.5 H10.5 V10.5 H1.5 Z", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        StateChanged += (_, _) =>
        {
            var restored = WindowState == WindowState.Maximized;
            ((System.Windows.Shapes.Path)maximize.Content).Data = Geometry.Parse(restored ? "M3.5,3.5 V1.5 H10.5 V8.5 H8.5 M1.5,3.5 H8.5 V10.5 H1.5 Z" : "M1.5,1.5 H10.5 V10.5 H1.5 Z");
            maximize.ToolTip = restored ? "Restore" : "Maximize";
            AutomationProperties.SetName(maximize, (string)maximize.ToolTip);
        };
        Caption("Close to tray", "M1.5,1.5 L10.5,10.5 M10.5,1.5 L1.5,10.5", Close, true);
        return controls;
    }
}
