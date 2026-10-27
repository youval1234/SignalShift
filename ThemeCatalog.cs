using System;
using System.Collections.Generic;
using System.Linq;

namespace SonarShift;

// String-only theme metadata is safe for the lightweight tray host: no WPF types.
internal sealed record AppTheme(string Name, string Description, bool IsLight);
internal static class ThemeCatalog
{
    internal static readonly IReadOnlyList<AppTheme> All = new[]
    {
        new AppTheme("Dark", "Midnight violet", false), new AppTheme("Light", "Clean lilac", true),
        new AppTheme("Aurora", "Emerald after dark", false), new AppTheme("Ocean", "Deep blue · electric cyan", false),
        new AppTheme("Sunset", "Coral · warm dusk", false), new AppTheme("Rose", "Orchid · soft neon", false),
        new AppTheme("Sakura", "Blush · cherry blossom", true), new AppTheme("Sand", "Amber · warm ivory", true)
    };
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
        ["#B7ABCF"] = "#675B7B", ["#F16D85"] = "#B82C50"
    };
    private static readonly Dictionary<string, Dictionary<string, string>> custom = new(StringComparer.Ordinal)
    {
        ["Aurora"] = Build("#0A1715", "#0D201C", "#122B25", "#1A3930", "#2C5045", "#E9FFF6", "#9BBAAC", "#6CE5B1", "#082219", "#163F32", "#9BF2CB", "#8BE2C5", "#123D30", "#154537", "#102C34"),
        ["Ocean"] = Build("#091322", "#0D1C30", "#14283F", "#1D3651", "#35526F", "#EFF8FF", "#9FB6CF", "#70CFFF", "#06243A", "#133F5B", "#A5E5FF", "#8FE3C2", "#173E3B", "#10465F", "#202A54"),
        ["Sunset"] = Build("#1C1016", "#29151E", "#34202A", "#432B35", "#69414B", "#FFF2EA", "#D2ADA9", "#FFB08E", "#37170C", "#5C2D2A", "#FFD4B9", "#ACDFC1", "#294539", "#66313E", "#35234A"),
        ["Rose"] = Build("#190F23", "#25162F", "#33213E", "#422C50", "#624573", "#FFF0FE", "#C6A7CE", "#F7A8DA", "#35112B", "#592E52", "#FFD1EF", "#9FE1C2", "#244638", "#642F68", "#293658"),
        ["Sakura"] = Build("#FFF4F7", "#F5E6ED", "#FFFFFF", "#F8EAF0", "#DFC4D0", "#3F2335", "#795469", "#9D326A", "#FFFFFF", "#F3D5E4", "#7F1F52", "#216447", "#DEF3E5", "#F4D7E6", "#EEE5FC"),
        ["Sand"] = Build("#FAF4E8", "#F0E6D3", "#FFFCF5", "#F2E9D8", "#D7C7AA", "#3E3321", "#75634A", "#855318", "#FFFFFF", "#EDDBBB", "#71420F", "#285F45", "#E0EBDC", "#F6E4C6", "#E3EBDD")
    };
    internal static IEnumerable<string> ResourceKeys => lightColors.Keys;
    internal static bool HasToken(string theme, string key) => theme == "Light" ? lightColors.ContainsKey(key) : custom.TryGetValue(theme, out var palette) && palette.ContainsKey(key);
    internal static bool Contains(string? name) => name != null && All.Any(t => t.Name == name);
    internal static bool IsLight(string name) => name is "Light" or "Sakura" or "Sand";
    internal static string Resolve(string theme, string key)
    {
        var palette = theme == "Light" ? lightColors : custom.TryGetValue(theme, out var selected) ? selected : null;
        return palette != null && palette.TryGetValue(key, out var value) ? value : key;
    }
    private static Dictionary<string, string> Build(string root, string sidebar, string surface, string raised, string border, string text, string muted, string accent, string onAccent, string selected, string selectedText, string success, string successBackground, string heroStart, string heroEnd)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Group(string value, string keys) { foreach (var key in keys.Split(' ')) result[key] = value; }
        Group(root, "#0D0F15 #10131B"); Group(sidebar, "#11131B"); Group(surface, "#171A23");
        Group(raised, "#222632 #202430 #20232E");
        Group(border, "#2A2E3D #292D3A #35394A #34394B #4B5166 #504266 #494057 #363B50 #493760");
        Group(text, "#F4F4FA #F0F0F6 #ECEAF5 #E6E4F0 #DADBE7"); Group(muted, "#9297AC #5D637B #BFB4D7 #CEBFE9 #B7ABCF");
        Group(accent, "#B8A3FF #B6A0FF #C5B4FF #BAA8FF #C6B4F8"); Group(onAccent, "#171124");
        Group(selected, "#29223F #302944 #39304D #2C263E #70609F"); Group(selectedText, "#E1D7FF #DDCEFF");
        Group(success, "#8BE2C5 #3C4950"); Group(successBackground, "#1D3531 #192126");
        Group(heroStart, "#2E2146"); Group(heroEnd, "#1A1F2E");
        Group(IsLightBackground(root) ? "#A04413" : "#FFCA9D", "#F0B68A");
        Group(IsLightBackground(root) ? "#B82C50" : "#FF839A", "#F16D85");
        return result;
    }
    private static bool IsLightBackground(string color) => color is "#FFF4F7" or "#FAF4E8";
}
