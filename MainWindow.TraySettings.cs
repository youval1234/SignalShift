using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private void BuildTrayIconSettings()
    {
        var content = new StackPanel();
        content.Children.Add(Text("MICROPHONE ICON", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "Keep your microphone in sight.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 12) });
        var visible = new CheckBox { Content = "Show a separate microphone status icon", IsChecked = Preferences.ShowMicrophoneTrayIcon };
        AutomationPropertiesName(visible, "Show separate microphone status icon");
        visible.Click += (_, _) =>
        {
            var previous = Preferences.ShowMicrophoneTrayIcon;
            try { Preferences.ShowMicrophoneTrayIcon = visible.IsChecked == true; Save(); UpdateMicrophoneMuteVisuals(); }
            catch (System.Exception ex)
            {
                Preferences.ShowMicrophoneTrayIcon = previous; visible.IsChecked = previous; UpdateMicrophoneMuteVisuals();
                SetStatus("Could not save microphone icon preference: " + ex.Message, true);
            }
        };
        content.Children.Add(visible);
        content.Children.Add(new TextBlock { Text = "To keep it beside the clock, drag the microphone from the ^ hidden-icons menu onto the taskbar. Windows remembers your choice. You can also use Windows taskbar settings.", FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var setup = Button("Set taskbar visibility  ↗", () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true }); }
            catch (System.Exception ex) { SetStatus("Could not open Windows taskbar settings: " + ex.Message, true); }
        });
        setup.HorizontalAlignment = HorizontalAlignment.Left;
        setup.ToolTip = "Open Windows taskbar settings. Windows controls whether tray icons appear outside the ^ menu.";
        content.Children.Add(setup);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
}
