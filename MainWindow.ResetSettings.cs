using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private bool resettingSettings;
    private void BuildResetSettings()
    {
        var content = new StackPanel();
        content.Children.Add(Text("START FRESH", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "Restore default settings.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
        content.Children.Add(new TextBlock { Text = "Restore the default theme, shortcuts, and preferences. This removes saved profiles and disables the Start with Windows option. Your current audio devices and microphone mute state will remain unchanged. A backup of your settings will be saved first.", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var reset = Button("Reset to default settings", ResetSettings);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 16, 0, 0);
        AutomationPropertiesName(reset, "Reset to default settings");
        content.Children.Add(reset);
        var card = Card(content, 24); card.Margin = new Thickness(0, 18, 0, 0); page.Children.Add(card);
    }

    private async void ResetSettings()
    {
        if (resettingSettings) return;
        resettingSettings = true; paused = true;
        try
        {
            if (MessageBox.Show(this,
                "Restore the default SignalShift settings?\n\nThis removes saved profiles, restores the default shortcuts, selects the Dark theme, and enables automatic updates. It also disables the Start with Windows and Start microphone live options. Your current audio devices and mute state will not change.\n\nA backup will be saved first; choose Import settings file to restore your previous setup.",
                "Reset SignalShift settings", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            await RestoreDefaultSettingsAsync();
        }
        catch (Exception ex) { SetStatus("Could not reset settings: " + ex.Message, true); }
        finally { resettingSettings = false; paused = false; }
    }

    private async Task RestoreDefaultSettingsAsync()
    {
        RefreshDevices();
        var devices = outputs.Concat(inputs).ToList();
        var defaults = SettingsTransfer.CreateDefaults(devices);
        var backup = await SettingsTransfer.ApplyAsync(store, new(defaults, "Defaults", false, new()), devices);
        Preferences = defaults; activeProfileId = null;
        soundCues.Cancel(); switchNotification?.Dismiss();
        ThemePalette.Apply(Preferences.Theme); UpdateThemeToggle();
        RegisterShortcuts(); UpdateMicrophoneMuteVisuals(); UpdateTray();
        if (Program.IsInterface) HostConnection.TrySend("reload");
        transferMessage = "Default settings restored. Choose Import settings file to restore your previous setup. Backup: " + backup;
        Navigate("Settings"); SetStatus("Default settings restored");
    }
}
