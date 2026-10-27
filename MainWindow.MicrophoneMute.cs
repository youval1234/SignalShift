using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private Button? microphoneMuteButton;
    private TextBlock? microphoneMuteLabel;
    private void BuildMicrophoneMuteControls(StackPanel content)
    {
        var line = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        microphoneMuteButton = Button("Mute microphone", ToggleMicrophoneMute);
        microphoneMuteButton.Padding = new Thickness(10, 5, 10, 5);
        microphoneMuteButton.ToolTip = "Toggle all current default microphone roles.";
        AutomationPropertiesName(microphoneMuteButton, "Toggle microphone mute");
        microphoneMuteLabel = Text("", 11, Muted);
        microphoneMuteLabel.VerticalAlignment = VerticalAlignment.Center;
        microphoneMuteLabel.Margin = new Thickness(12, 0, 0, 0);
        line.Children.Add(microphoneMuteButton); line.Children.Add(microphoneMuteLabel); content.Children.Add(line);
        UpdateMicrophoneMuteVisuals();
    }
    private void UpdateMicrophoneMuteVisuals()
    {
        microphoneTrayIndicator?.SetVisible(Preferences.ShowMicrophoneTrayIcon);
        microphoneTrayIndicator?.Update(microphoneMute.State, hotkeyErrors.Count != 0);
        if (microphoneMuteButton == null || microphoneMuteLabel == null) return;
        var state = useDemoDevices ? MicrophoneMuteState.Live : microphoneMute.State;
        microphoneMuteButton.Content = state == MicrophoneMuteState.Muted ? "Unmute microphone" : "Mute microphone";
        microphoneMuteButton.IsEnabled = state != MicrophoneMuteState.Unavailable;
        microphoneMuteLabel.Text = state switch { MicrophoneMuteState.Muted => "●  Muted", MicrophoneMuteState.Live => "●  Live", MicrophoneMuteState.Mixed => "●  Mixed", _ => "Unavailable" };
        microphoneMuteLabel.Foreground = state == MicrophoneMuteState.Muted ? Brush("#F16D85") : state == MicrophoneMuteState.Live ? Mint : Muted;
        microphoneMuteLabel.ToolTip = microphoneMute.Error ?? "Controls the Windows default microphone endpoints for audio and calls.";
    }
    private void ToggleMicrophoneMute()
    {
        if (useDemoDevices) return;
        try
        {
            if (Program.IsInterface)
            {
                using var response = JsonDocument.Parse(HostConnection.Send("mic-toggle"));
                if (!response.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
                    throw new InvalidOperationException(response.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "Microphone control failed.");
            }
            else
            {
                var muted = microphoneMute.Toggle() == MicrophoneMuteState.Muted;
                if (Preferences.HotkeySounds) soundCues.Play(muted ? AudioFlow.Input : AudioFlow.Output);
                Notify("Microphone", muted ? "Microphone muted" : "Microphone live");
            }
            microphoneMute.Refresh(); UpdateMicrophoneMuteVisuals();
            SetStatus(microphoneMute.State == MicrophoneMuteState.Muted ? "Microphone muted" : "Microphone live");
        }
        catch (Exception ex) { SetStatus("Could not change microphone mute: " + ex.Message, true); }
    }
    private void BuildMicrophoneMuteShortcut()
    {
        var content = new StackPanel(); content.Children.Add(Text("MICROPHONE MUTE", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "Your voice, on your terms.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        content.Children.Add(new TextBlock { Text = "Mute or unmute your default microphone devices for audio and calls. The app button and shortcut use the same control.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        line.Children.Add(Card(Text(Preferences.MicrophoneMuteShortcut.Label, 17, Purple, FontWeights.SemiBold), 13));
        var edit = Button("Record shortcut", ChangeMicrophoneMuteShortcut); edit.Margin = new Thickness(14, 0, 0, 0); line.Children.Add(edit); content.Children.Add(line);
        var enabled = new CheckBox { Content = "Enable microphone mute shortcut", IsChecked = Preferences.MicrophoneMuteHotkeyEnabled, Margin = new Thickness(0, 16, 0, 0) };
        enabled.Click += (_, _) =>
        {
            var previous = Preferences.MicrophoneMuteHotkeyEnabled; Preferences.MicrophoneMuteHotkeyEnabled = enabled.IsChecked == true;
            try { SettingsTransfer.Validate(Preferences); RegisterShortcuts(); Save(); }
            catch (Exception ex) { Preferences.MicrophoneMuteHotkeyEnabled = previous; enabled.IsChecked = previous; RegisterShortcuts(); SetStatus(ex.Message, true); }
        };
        content.Children.Add(enabled);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
    private void ChangeMicrophoneMuteShortcut()
    {
        var shortcut = CaptureShortcut(Preferences.MicrophoneMuteShortcut);
        if (shortcut == null) return;
        if (IsDuplicateShortcut(shortcut, "microphone-mute", null)) { SetStatus("That shortcut is already assigned in SignalShift.", true); return; }
        hotkeys?.Clear();
        if (hotkeys != null && !hotkeys.Register(500, shortcut, () => { })) { RegisterShortcuts(); SetStatus("Another app uses that shortcut. Your previous shortcut is kept.", true); return; }
        hotkeys?.Clear(); Preferences.MicrophoneMuteShortcut = shortcut; RegisterShortcuts(); Save(); Navigate("Shortcuts");
    }
}
