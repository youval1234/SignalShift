using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private Button? themeToggle;
    private void ToggleTheme() => SetTheme(Preferences.Theme == "Dark" ? "Light" : "Dark");
    private void SetTheme(string theme)
    {
        Preferences.Theme = theme;
        ThemePalette.Apply(theme);
        UpdateThemeToggle();
        Save();
        Navigate(currentPage);
        SetStatus(theme + " mode enabled");
    }
    private void UpdateThemeToggle()
    {
        if (themeToggle == null) return;
        themeToggle.Content = Preferences.Theme == "Dark" ? "☀   Light mode" : "☾   Dark mode";
        themeToggle.ToolTip = "Switch to " + (Preferences.Theme == "Dark" ? "light" : "dark") + " mode";
        AutomationPropertiesName(themeToggle, themeToggle.ToolTip.ToString()!);
    }
    private void BuildAppearance()
    {
        var content = new StackPanel();
        content.Children.Add(Text("APPEARANCE", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "A different shade of focus.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        content.Children.Add(Text("Choose a look here or use the light/dark button in the sidebar.", 12, Muted));
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var choice = Button((theme == "Dark" ? "☾   " : "☀   ") + theme + (Preferences.Theme == theme ? "   ✓" : ""), () => SetTheme(theme), Preferences.Theme == theme);
            choice.Margin = new Thickness(0, 0, 10, 0); choices.Children.Add(choice);
        }
        content.Children.Add(choices);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
}
