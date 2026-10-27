using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SonarShift;

public sealed partial class MainWindow
{
    private Button? themeToggle;
    private void ToggleTheme() => SetTheme(ThemeCatalog.IsLight(Preferences.Theme) ? "Dark" : "Light");
    private void SetTheme(string theme)
    {
        Preferences.Theme = theme;
        ThemePalette.Apply(theme);
        UpdateThemeToggle();
        Save();
        Navigate(currentPage);
        SetStatus(theme + " theme enabled");
    }
    private void UpdateThemeToggle()
    {
        if (themeToggle == null) return;
        themeToggle.Content = ThemeCatalog.IsLight(Preferences.Theme) ? "☾   Dark mode" : "☀   Light mode";
        themeToggle.ToolTip = "Switch to " + (ThemeCatalog.IsLight(Preferences.Theme) ? "dark" : "light") + " mode";
        AutomationPropertiesName(themeToggle, themeToggle.ToolTip.ToString()!);
    }
    private void BuildAppearance()
    {
        var content = new StackPanel();
        content.Children.Add(Text("APPEARANCE", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "A different shade of focus.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        content.Children.Add(new TextBlock { Text = ThemeCatalog.All.Count + " looks. Pick your color, keep your flow. Changes apply immediately.", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var choices = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var theme in ThemeCatalog.All)
        {
            var selected = Preferences.Theme == theme.Name;
            var choice = Button(theme.Name, () => SetTheme(theme.Name));
            choice.Width = 148; choice.Padding = new Thickness(12); choice.Margin = new Thickness(0, 0, 10, 10);
            choice.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            choice.BorderBrush = selected ? Purple : Brush("#35394A"); choice.BorderThickness = new Thickness(selected ? 2 : 1);
            choice.ToolTip = "Apply " + theme.Name + " theme · " + theme.Description;
            AutomationPropertiesName(choice, "Apply " + theme.Name + " theme" + (selected ? ", selected" : ""));
            var body = new StackPanel();
            SolidColorBrush Swatch(string key)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeCatalog.Resolve(theme.Name, key))); brush.Freeze(); return brush;
            }
            var miniature = new Grid { Height = 58, Background = Swatch("#0D0F15"), ClipToBounds = true };
            miniature.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) }); miniature.ColumnDefinitions.Add(new ColumnDefinition());
            miniature.Children.Add(new Border { Background = Swatch("#11131B") });
            var sample = new StackPanel { Margin = new Thickness(9) }; Grid.SetColumn(sample, 1);
            sample.Children.Add(new Border { Background = Swatch("#B8A3FF"), Height = 5, Width = 54, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 7) });
            var line = new Grid(); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            line.Children.Add(new Border { Background = Swatch("#171A23"), Height = 23, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 5, 0) });
            var chip = new Border { Background = Swatch("#8BE2C5"), Height = 23, CornerRadius = new CornerRadius(4) }; Grid.SetColumn(chip, 1); line.Children.Add(chip); sample.Children.Add(line); miniature.Children.Add(sample);
            body.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = miniature });
            var title = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
            var marker = Text(selected ? "✓" : theme.IsLight ? "☀" : "☾", 13, selected ? Mint : Muted); DockPanel.SetDock(marker, Dock.Right); title.Children.Add(marker); title.Children.Add(Text(theme.Name, 14, null, FontWeights.SemiBold)); body.Children.Add(title);
            var description = Text(theme.Description, 10, Muted); description.MaxWidth = 124; description.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(description); choice.Content = body; choices.Children.Add(choice);
        }
        content.Children.Add(choices);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
}
