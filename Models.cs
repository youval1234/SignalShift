using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sonora;

public enum AudioFlow { Output = 0, Input = 1 }
public sealed record AudioDevice(string Id, string Name, AudioFlow Flow, bool IsDefault, bool IsCommunications)
{
    public string Title => Name.Contains(" (") ? Name[..Name.IndexOf(" (", StringComparison.Ordinal)] : Name;
    public string Detail => Name.Contains(" (") ? Name[(Name.IndexOf(" (", StringComparison.Ordinal) + 2)..].TrimEnd(')') : (Flow == AudioFlow.Output ? "Playback device" : "Recording device");
}
public sealed record Shortcut(uint Modifiers, uint Key, string Label);
public sealed class AudioProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My setup";
    public string? OutputId { get; set; }
    public string? InputId { get; set; }
    public Shortcut? Hotkey { get; set; }
}
public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public HashSet<string> Outputs { get; set; } = new();
    public HashSet<string> Inputs { get; set; } = new();
    public Shortcut OutputShortcut { get; set; } = new(3, 0x7A, "Ctrl + Alt + F11");
    public Shortcut InputShortcut { get; set; } = new(3, 0x79, "Ctrl + Alt + F10");
    public bool HotkeysEnabled { get; set; } = true;
    public bool OutputHotkeyEnabled { get; set; } = true;
    public bool InputHotkeyEnabled { get; set; } = true;
    public bool CommunicationsToo { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool Initialized { get; set; }
    public List<AudioProfile> Profiles { get; set; } = new();
}
public sealed class SettingsStore
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sonora");
    public string FilePath { get; }
    public SettingsStore(string? path = null) => FilePath = path ?? Path.Combine(DirectoryPath, "settings.json");
    public Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Empty settings");
            SettingsTransfer.Validate(value);
            return value;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            File.Copy(FilePath, FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            return new();
        }
    }
    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
public static class CyclePlanner
{
    public static AudioDevice? Next(IReadOnlyList<AudioDevice> devices, HashSet<string> selected, string? current)
    {
        var available = new List<AudioDevice>();
        foreach (var device in devices) if (selected.Contains(device.Id)) available.Add(device);
        if (available.Count == 0) return null;
        var index = available.FindIndex(d => d.Id == current);
        return available[(index + 1) % available.Count];
    }
}
