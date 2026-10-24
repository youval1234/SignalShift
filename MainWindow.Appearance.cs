using System;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private void ToggleTheme() => SetTheme(Preferences.Theme == "Dark" ? "Light" : "Dark");
    private void SetTheme(string theme)
    {
        Preferences.Theme = theme;
        ThemePalette.Apply(theme);
        Save();
        // Only the hero gradient depends on Color rather than a shared brush.
        Navigate(currentPage);
        SetStatus(theme + " mode enabled");
    }
    private void BuildAppearance()
    {
        var content = new StackPanel();
        content.Children.Add(Text("APPEARANCE", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "A different shade of focus.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        content.Children.Add(Text("Choose a look, or change it with your appearance shortcut.", 12, Muted));
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var choice = Button((theme == "Dark" ? "☾   " : "☀   ") + theme + (Preferences.Theme == theme ? "   ✓" : ""), () => SetTheme(theme), Preferences.Theme == theme);
            choice.Margin = new Thickness(0, 0, 10, 0); choices.Children.Add(choice);
        }
        content.Children.Add(choices);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
    private void BuildThemeShortcut()
    {
        var content = new StackPanel(); content.Children.Add(Text("☀  SWITCH APPEARANCE", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "Dark ↔ Light", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        content.Children.Add(Text("Works in the tray too. Your next settings window uses the chosen look.", 12, Muted));
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        line.Children.Add(Card(Text(Preferences.ThemeShortcut.Label, 17, Purple, FontWeights.SemiBold), 13));
        var edit = Button("Record shortcut", () => ChangeThemeShortcut()); edit.Margin = new Thickness(14, 0, 0, 0); line.Children.Add(edit); content.Children.Add(line);
        var enabled = new CheckBox { Content = "Enable this shortcut", IsChecked = Preferences.ThemeHotkeyEnabled, Margin = new Thickness(0, 16, 0, 0) };
        enabled.Click += (_, _) =>
        {
            var previous = Preferences.ThemeHotkeyEnabled; Preferences.ThemeHotkeyEnabled = enabled.IsChecked == true;
            try { SettingsTransfer.Validate(Preferences); RegisterShortcuts(); Save(); }
            catch (Exception ex) { Preferences.ThemeHotkeyEnabled = previous; enabled.IsChecked = previous; RegisterShortcuts(); SetStatus(ex.Message, true); }
        };
        content.Children.Add(enabled);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
    private void ChangeThemeShortcut()
    {
        var shortcut = CaptureShortcut(Preferences.ThemeShortcut);
        if (shortcut == null) return;
        if (IsDuplicateShortcut(shortcut, "theme", null)) { SetStatus("That shortcut is already assigned in SignalShift.", true); return; }
        hotkeys?.Clear();
        if (hotkeys != null && !hotkeys.Register(500, shortcut, () => { })) { RegisterShortcuts(); SetStatus("Another app uses that shortcut. Your previous shortcut is kept.", true); return; }
        hotkeys?.Clear(); Preferences.ThemeShortcut = shortcut; RegisterShortcuts(); Save(); Navigate("Shortcuts");
    }
}
