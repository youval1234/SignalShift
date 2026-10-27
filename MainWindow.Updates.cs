using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private FileSystemWatcher? updateWatcher;
    private TextBlock? updateStatus;
    private void BuildUpdates()
    {
        var content = new StackPanel();
        content.Children.Add(Text("UPDATES", 11, Purple, FontWeights.SemiBold));
        var enabled = new CheckBox { Content = "Automatically update when SignalShift launches", IsChecked = Preferences.AutomaticUpdates, Margin = new Thickness(0, 16, 0, 8) };
        enabled.Click += (_, _) => { Preferences.AutomaticUpdates = enabled.IsChecked == true; Save(); };
        content.Children.Add(enabled);
        content.Children.Add(new TextBlock { Text = "When enabled, SignalShift checks its GitHub releases in the background, downloads and verifies a newer version, then restarts to install it. Your settings and app location are preserved.", TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12, Margin = new Thickness(30, 0, 0, 16) });
        updateStatus = new TextBlock { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; content.Children.Add(updateStatus);
        UpdateStatusText();
        var check = Button("Check for updates", () =>
        {
            if (useDemoDevices) return;
            try { HostConnection.Send("check-update-manual"); SetStatus("Checking for updates in the background"); }
            catch (Exception ex) { SetStatus("Could not check for updates: " + ex.Message, true); }
        }); check.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(check);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
        if (!useDemoDevices && updateWatcher == null)
        {
            updateWatcher = new FileSystemWatcher(SettingsStore.DirectoryPath, "update-status.txt") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
            updateWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateStatusText));
            updateWatcher.Created += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateStatusText));
            Closed += (_, _) => updateWatcher?.Dispose();
        }
    }
    private void UpdateStatusText()
    {
        if (updateStatus == null) return;
        try
        {
            updateStatus.Text = !useDemoDevices && File.Exists(AppUpdater.StatusPath) ? File.ReadAllText(AppUpdater.StatusPath) : "Version " + AppUpdater.Current.ToString(3) + " · GitHub Releases";
            if (!useDemoDevices && currentPage == "Settings") SetStatus(updateStatus.Text);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
