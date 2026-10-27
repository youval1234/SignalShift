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
    private static string theme = "Dark";
    internal static Color Color(string key) => (Color)ColorConverter.ConvertFromString(ThemeCatalog.Resolve(theme, key));
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
    internal static void Apply(string value)
    {
        if (!ThemeCatalog.Contains(value)) throw new ArgumentException("Unknown appearance.", nameof(value));
        theme = value;
        foreach (var pair in colors) pair.Value.Value = Color(pair.Key);
        // Resource keys cover templates as well as ordinary controls. No per-switch
        // palette allocation: existing shared brush instances are reused.
        foreach (var key in ThemeCatalog.ResourceKeys) Application.Current.Resources["Theme_" + key[1..].ToUpperInvariant()] = Get(key);
    }
}
