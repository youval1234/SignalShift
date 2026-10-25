using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, (Border Card, Button Activate, Border Badge, string Name)> profileRows = new();
    private void RefreshProfileVisuals()
    {
        if (currentPage != "Profiles") return;
        var current = ProfileSelection.Current(Preferences.Profiles, outputs.FirstOrDefault(d => d.IsDefault)?.Id, inputs.FirstOrDefault(d => d.IsDefault)?.Id, activeProfileId);
        foreach (var row in profileRows)
        {
            var selected = row.Key == current;
            row.Value.Card.Background = Brush(selected ? "#192126" : "#171A23");
            row.Value.Card.BorderBrush = Brush(selected ? "#3C4950" : "#292D3A");
            row.Value.Badge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (row.Value.Activate.IsEnabled == selected)
            {
                row.Value.Activate.Content = Text(selected ? "Active" : "Activate profile  ↗", 13, Brush("#171124"), FontWeights.SemiBold);
                row.Value.Activate.IsEnabled = !selected;
            }
            AutomationPropertiesName(row.Value.Card, (selected ? "Active profile: " : "Profile: ") + row.Value.Name);
        }
    }
}
