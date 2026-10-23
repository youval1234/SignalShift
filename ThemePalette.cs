using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media;

namespace SonarShift;

// UI-only, bounded palette. Shared brush instances update existing controls and
// open dialogs without rebuilding the tree or retaining earlier windows.
internal static class ThemePalette
{
    private static readonly Dictionary<string, SolidColorBrush> brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PaletteColor> colors = new(StringComparer.OrdinalIgnoreCase);
    private sealed class PaletteColor : INotifyPropertyChanged
    {
        private Color value;
        public Color Value { get => value; set { if (this.value == value) return; this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static bool light;
    private static readonly Dictionary<string, string> lightColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#0D0F15"] = "#F5F6FB", ["#11131B"] = "#ECEEF6", ["#171A23"] = "#FFFFFF",
        ["#F4F4FA"] = "#202439", ["#F0F0F6"] = "#202439", ["#ECEAF5"] = "#24283D",
        ["#E6E4F0"] = "#303449", ["#DADBE7"] = "#303449", ["#9297AC"] = "#626A82",
        ["#5D637B"] = "#68718B", ["#B8A3FF"] = "#6950C7", ["#B6A0FF"] = "#6950C7",
        ["#C5B4FF"] = "#6249BF", ["#BAA8FF"] = "#6249BF", ["#171124"] = "#FFFFFF",
        ["#8BE2C5"] = "#176F58", ["#222632"] = "#F0F1F8", ["#202430"] = "#F0F1F8",
        ["#10131B"] = "#FAFAFD", ["#2A2E3D"] = "#D8DCEA", ["#292D3A"] = "#D8DCEA",
        ["#35394A"] = "#CFD4E3", ["#34394B"] = "#CFD4E3", ["#4B5166"] = "#858DA5",
        ["#20232E"] = "#D8DCEA", ["#E1D7FF"] = "#583BAE", ["#29223F"] = "#E1DAF8",
        ["#302944"] = "#E7E1F8", ["#DDCEFF"] = "#583BAE", ["#504266"] = "#B5A6DC",
        ["#1D3531"] = "#D9F2E8", ["#3C4950"] = "#A0C9BC", ["#192126"] = "#F0FAF6",
        ["#F0B68A"] = "#9E4718", ["#39304D"] = "#E5DEF6", ["#2C263E"] = "#ECE7F8",
        ["#494057"] = "#C8BDDE", ["#363B50"] = "#AAB1C7", ["#70609F"] = "#C5B7EA",
        ["#2E2146"] = "#EAE3FA", ["#1A1F2E"] = "#E1EDF4", ["#493760"] = "#CDC0E8",
        ["#C6B4F8"] = "#624B9B", ["#BFB4D7"] = "#675B7B", ["#CEBFE9"] = "#675B7B",
        ["#B7ABCF"] = "#675B7B"
    };
    internal static Color Color(string key) => (Color)ColorConverter.ConvertFromString(light && lightColors.TryGetValue(key, out var value) ? value : key);
    internal static SolidColorBrush Get(string key)
    {
        if (!brushes.TryGetValue(key, out var brush))
        {
            var token = new PaletteColor { Value = Color(key) }; colors.Add(key, token);
            brush = new SolidColorBrush();
            // An expression prevents resource/style sealing from freezing this
            // shared brush. One binding per palette color, never per control.
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(PaletteColor.Value)) { Source = token, Mode = BindingMode.OneWay });
            brushes.Add(key, brush);
        }
        return brush;
    }
    internal static void Apply(string theme)
    {
        light = theme == "Light";
        foreach (var pair in colors) pair.Value.Value = Color(pair.Key);
        // Resource keys cover templates as well as ordinary controls. No per-switch
        // palette allocation: existing shared brush instances are reused.
        foreach (var key in lightColors.Keys) Application.Current.Resources["Theme_" + key[1..].ToUpperInvariant()] = Get(key);
    }
}
