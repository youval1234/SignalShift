using System;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private void CycleProfiles()
    {
        try
        {
            RefreshDevices();
            var next = ProfileCyclePlanner.Next(Preferences.Profiles, outputs, inputs,
                audio.GetDefaultId(AudioFlow.Output, 1), audio.GetDefaultId(AudioFlow.Input, 1), activeProfileId);
            if (next == null) { SetStatus("Create a profile with connected audio devices first.", true); return; }
            ActivateProfile(next, fromHotkey: true);
        }
        catch (Exception ex) { SetStatus("Could not cycle profiles: " + ex.Message, true); }
    }
    private void BuildProfileCycleShortcut()
    {
        var content = new StackPanel();
        content.Children.Add(Text("↻  CYCLE AUDIO PROFILES", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "Your whole setup. One shortcut.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        content.Children.Add(new TextBlock { Text = "Switch output and microphone together. Cycles through Profiles in order, skips disconnected setups, and wraps back to the first.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        line.Children.Add(Card(Text(Preferences.ProfileCycleShortcut.Label, 17, Purple, FontWeights.SemiBold), 13));
        var edit = Button("Record shortcut", ChangeProfileCycleShortcut); edit.Margin = new Thickness(14, 0, 0, 0);
        line.Children.Add(edit); content.Children.Add(line);
        var enabled = new CheckBox { Content = "Enable profile cycling shortcut", IsChecked = Preferences.ProfileCycleHotkeyEnabled, Margin = new Thickness(0, 16, 0, 0) };
        enabled.Click += (_, _) =>
        {
            var previous = Preferences.ProfileCycleHotkeyEnabled; Preferences.ProfileCycleHotkeyEnabled = enabled.IsChecked == true;
            try { SettingsTransfer.Validate(Preferences); RegisterShortcuts(); Save(); }
            catch (Exception ex) { Preferences.ProfileCycleHotkeyEnabled = previous; enabled.IsChecked = previous; RegisterShortcuts(); SetStatus(ex.Message, true); }
        };
        content.Children.Add(enabled);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
    private void ChangeProfileCycleShortcut()
    {
        var shortcut = CaptureShortcut(Preferences.ProfileCycleShortcut);
        if (shortcut == null) return;
        if (IsDuplicateShortcut(shortcut, "profile-cycle", null)) { SetStatus("That shortcut is already assigned in SignalShift.", true); return; }
        hotkeys?.Clear();
        if (hotkeys != null && !hotkeys.Register(500, shortcut, () => { })) { RegisterShortcuts(); SetStatus("Another app uses that shortcut. Your previous shortcut is kept.", true); return; }
        hotkeys?.Clear(); Preferences.ProfileCycleShortcut = shortcut; RegisterShortcuts(); Save(); Navigate("Shortcuts");
    }
}
