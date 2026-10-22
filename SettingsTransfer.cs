using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;

namespace Sonora;

public sealed class SettingsDocument
{
    public string Format { get; set; } = "Sonora settings";
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public Settings Settings { get; set; } = new();
    public bool? StartWithWindows { get; set; }
    public List<DeviceReference> Devices { get; set; } = new();
}
public sealed record DeviceReference(string Id, string Name, AudioFlow Flow);
public sealed record SettingsImport(Settings Settings, string Source, bool? StartWithWindows, List<string> Warnings);

public static class StartupRegistration
{
#if SONORA_STORE
    private static Windows.ApplicationModel.StartupTask? startupTask;
    public static async Task InitializeAsync() => startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("SonoraStartup");
    public static bool IsEnabled() => startupTask?.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
    public static async Task SetAsync(bool enabled)
    {
        var task = startupTask ?? throw new InvalidOperationException("Sonora's Windows startup task is not available.");
        if (!enabled) { task.Disable(); return; }
        var state = await task.RequestEnableAsync();
        if (state is not (Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy))
            throw new InvalidOperationException("Windows has disabled Sonora's startup task. Enable Sonora in Windows Settings > Apps > Startup.");
    }
#else
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static Task InitializeAsync() => Task.CompletedTask;
    public static bool IsEnabled() { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue("Sonora") != null; }
    public static Task SetAsync(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (enabled) key.SetValue("Sonora", "\"" + Environment.ProcessPath + "\" --tray");
        else key.DeleteValue("Sonora", false);
        return Task.CompletedTask;
    }
#endif
}

public static class SettingsTransfer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public static Settings Clone(Settings settings) => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings), JsonOptions)!;
    public static string Export(Settings settings, IEnumerable<AudioDevice> devices, bool? startWithWindows)
    {
        Validate(settings);
        var document = new SettingsDocument
        {
            Settings = Clone(settings), StartWithWindows = startWithWindows,
            Devices = devices.Select(d => new DeviceReference(d.Id, d.Name, d.Flow)).ToList()
        };
        return "// Sonora settings — edit this UTF-8 text file in Notepad, then import it in Settings.\n" +
               "// Device names are used to reconnect saved choices when Windows changes their IDs.\n" +
               JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }
    public static void ExportFile(string path, Settings settings, IEnumerable<AudioDevice> devices, bool? startup) =>
        File.WriteAllText(path, Export(settings, devices, startup), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    public static SettingsImport ReadFile(string path, Settings current, IReadOnlyList<AudioDevice> devices)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException("Settings files must be smaller than 2 MB.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd(), current, devices);
    }
    public static SettingsImport Parse(string text, Settings current, IReadOnlyList<AudioDevice> devices)
    {
        try
        {
            using var json = JsonDocument.Parse(text.TrimStart('\uFEFF'), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Choose a Sonora settings export or a SoundSwitch configuration file.");
            if (root.TryGetProperty("Format", out _))
            {
                var document = root.Deserialize<SettingsDocument>(JsonOptions)!;
                if (document.Format != "Sonora settings" || document.FormatVersion != 1) throw new InvalidDataException("This settings export uses an unsupported format or version.");
                if (!root.TryGetProperty("Settings", out var settingsJson)) throw new InvalidDataException("The export is missing its settings.");
                RequireNativeFields(settingsJson);
                Validate(document.Settings);
                if (document.Devices == null || document.Devices.Any(d => d == null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || (d.Flow != AudioFlow.Output && d.Flow != AudioFlow.Input))) throw new InvalidDataException("The export contains an invalid device reference.");
                var warnings = new List<string>();
                Remap(document.Settings, document.Devices, devices, warnings);
                return new(document.Settings, "Sonora", document.StartWithWindows, warnings);
            }
            // Also accept an existing Sonora settings.json copied or renamed as .txt.
            if (root.TryGetProperty("SchemaVersion", out _))
            {
                RequireNativeFields(root);
                var settings = root.Deserialize<Settings>(JsonOptions)!; Validate(settings);
                return new(settings, "Sonora", null, new());
            }
            if ((root.TryGetProperty("SelectedDevices", out _) || root.TryGetProperty("SelectedPlaybackDeviceListId", out _) || root.TryGetProperty("SelectedRecordingDeviceListId", out _)) &&
                (root.TryGetProperty("PlaybackHotKey", out _) || root.TryGetProperty("RecordingHotKey", out _))) return ImportSoundSwitch(root, current, devices);
            throw new InvalidDataException("This file is not a recognized Sonora or SoundSwitch settings file.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        {
            throw new InvalidDataException("The settings file is invalid. Your current settings were kept. " + ex.Message, ex);
        }
    }
    private static void RequireNativeFields(JsonElement root)
    {
        foreach (var name in new[] { "SchemaVersion", "Outputs", "Inputs", "OutputShortcut", "InputShortcut", "Profiles" })
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out _)) throw new InvalidDataException("The settings file is missing " + name + ".");
    }
    public static void Validate(Settings settings)
    {
        if (settings == null || settings.SchemaVersion != 1) throw new InvalidDataException("Unsupported settings version.");
        if (settings.Outputs == null || settings.Inputs == null || settings.Profiles == null || settings.Outputs.Concat(settings.Inputs).Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Invalid device selections.");
        if (settings.Outputs.Count + settings.Inputs.Count > 1000 || settings.Profiles.Count > 128) throw new InvalidDataException("Too many saved devices or profiles.");
        var active = new List<Shortcut>();
        void Hotkey(Shortcut key, bool enabled)
        {
            if (key == null || key.Modifiers == 0 || (key.Modifiers & ~7u) != 0 || key.Key == 0 || key.Key > 254 || key.Key == 0x7B) throw new InvalidDataException("A shortcut must use Ctrl, Alt or Shift with a valid key other than F12.");
            if (enabled && active.Any(k => k.Modifiers == key.Modifiers && k.Key == key.Key)) throw new InvalidDataException("Two enabled actions use the same shortcut. Assign different shortcuts before importing.");
            if (enabled) active.Add(key);
        }
        Hotkey(settings.OutputShortcut, settings.HotkeysEnabled && settings.OutputHotkeyEnabled);
        Hotkey(settings.InputShortcut, settings.HotkeysEnabled && settings.InputHotkeyEnabled);
        var profileIds = new HashSet<string>();
        foreach (var profile in settings.Profiles)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Id) || !profileIds.Add(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || (string.IsNullOrWhiteSpace(profile.OutputId) && string.IsNullOrWhiteSpace(profile.InputId))) throw new InvalidDataException("A profile has an invalid name, ID or device pair.");
            if (profile.Hotkey != null) Hotkey(profile.Hotkey, settings.HotkeysEnabled);
        }
        // Labels are display text; derive them from the actual keys instead of trusting edited labels.
        settings.OutputShortcut = NormalizeShortcut(settings.OutputShortcut);
        settings.InputShortcut = NormalizeShortcut(settings.InputShortcut);
        foreach (var profile in settings.Profiles) if (profile.Hotkey != null) profile.Hotkey = NormalizeShortcut(profile.Hotkey);
    }
    private static Shortcut NormalizeShortcut(Shortcut key)
    {
        var parts = new List<string>();
        if ((key.Modifiers & 2) != 0) parts.Add("Ctrl"); if ((key.Modifiers & 1) != 0) parts.Add("Alt"); if ((key.Modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(key.Key >= 0x30 && key.Key <= 0x39 ? ((char)key.Key).ToString() : KeyInterop.KeyFromVirtualKey((int)key.Key).ToString());
        return key with { Label = string.Join(" + ", parts) };
    }
    private static SettingsImport ImportSoundSwitch(JsonElement root, Settings current, IReadOnlyList<AudioDevice> devices)
    {
        var settings = Clone(current); var references = new List<DeviceReference>(); var warnings = new List<string>();
        var hasSelections = root.TryGetProperty("SelectedDevices", out var selected);
        if (hasSelections && selected.ValueKind != JsonValueKind.Array) throw new InvalidDataException("SoundSwitch's selected devices must be a list.");
        if (hasSelections) { settings.Outputs.Clear(); settings.Inputs.Clear(); }
        if (hasSelections) foreach (var device in selected.EnumerateArray())
        {
            var id = device.GetProperty("Id").GetString(); var name = device.GetProperty("Name").GetString(); var type = device.GetProperty("Type").GetInt32();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || type is < 0 or > 1) throw new InvalidDataException("SoundSwitch contains an invalid selected device.");
            references.Add(new(id, name, (AudioFlow)type)); (type == 0 ? settings.Outputs : settings.Inputs).Add(id);
        }
        foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            if (root.TryGetProperty(flow == AudioFlow.Output ? "SelectedPlaybackDeviceListId" : "SelectedRecordingDeviceListId", out var legacy))
            {
                if (legacy.ValueKind != JsonValueKind.Array) throw new InvalidDataException("SoundSwitch contains an invalid legacy device list.");
                var choices = flow == AudioFlow.Output ? settings.Outputs : settings.Inputs;
                if (!hasSelections) choices.Clear();
                foreach (var item in legacy.EnumerateArray()) { var id = item.GetString(); if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("SoundSwitch contains an invalid device ID."); choices.Add(id); }
            }
        }
        void ReadShortcut(string name, AudioFlow flow)
        {
            if (!root.TryGetProperty(name, out var hotkey)) return;
            var key = hotkey.GetProperty("Keys").GetUInt32(); var modifiers = hotkey.GetProperty("Modifier").GetUInt32();
            var enabled = !hotkey.TryGetProperty("Enabled", out var state) || state.GetBoolean();
            if (flow == AudioFlow.Output) settings.OutputHotkeyEnabled = enabled; else settings.InputHotkeyEnabled = enabled;
            if (key == 0 || key > 254 || key == 0x7B || modifiers == 0 || (modifiers & ~7u) != 0)
            {
                if (enabled) throw new InvalidDataException(name + " uses a shortcut Sonora cannot register. Change it in SoundSwitch first.");
                return; // Disabled, unassigned combinations retain a usable Sonora combination.
            }
            var shortcut = NormalizeShortcut(new(modifiers, key, ""));
            if (flow == AudioFlow.Output) settings.OutputShortcut = shortcut; else settings.InputShortcut = shortcut;
        }
        ReadShortcut("PlaybackHotKey", AudioFlow.Output); ReadShortcut("RecordingHotKey", AudioFlow.Input);
        settings.HotkeysEnabled = true;
        if (root.TryGetProperty("ChangeCommunications", out var communications)) settings.CommunicationsToo = communications.GetBoolean();
        // SoundSwitch NotificationType: NoNotification=2. Other styles become Sonora notifications.
        if (root.TryGetProperty("SwitchDeviceNotification", out var notification)) settings.Notifications = notification.GetInt32() != 2;
        if (root.TryGetProperty("Profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array && profiles.GetArrayLength() > 0) warnings.Add("SoundSwitch profiles and automation are not transferred. Existing Sonora profiles are kept.");
        if (root.TryGetProperty("ProfileSettings", out var profileSettings) && profileSettings.ValueKind == JsonValueKind.Array && profileSettings.GetArrayLength() > 0) warnings.Add("SoundSwitch profile settings are not transferred. Existing Sonora profiles are kept.");
        if (root.TryGetProperty("MuteRecordingHotKey", out var mute) && mute.TryGetProperty("Enabled", out var muteEnabled) && muteEnabled.GetBoolean()) warnings.Add("SoundSwitch's microphone-mute shortcut is not supported in Sonora.");
        if (root.TryGetProperty("SwitchDeviceNotification", out var style) && style.GetInt32() is not 0 and not 2) warnings.Add("SoundSwitch's notification style is replaced by Sonora's standard notification.");
        if (root.TryGetProperty("AppSoundRules", out var rules) && rules.ValueKind == JsonValueKind.Array && rules.GetArrayLength() > 0) warnings.Add("SoundSwitch per-app rules are not transferred.");
        settings.Initialized = true; Validate(settings); Remap(settings, references, devices, warnings);
        return new(settings, "SoundSwitch", null, warnings.Distinct().ToList());
    }
    private static void Remap(Settings settings, IReadOnlyList<DeviceReference> references, IReadOnlyList<AudioDevice> live, List<string> warnings)
    {
        string Map(string id, AudioFlow flow)
        {
            var exact = live.FirstOrDefault(d => d.Flow == flow && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)); if (exact != null) return exact.Id;
            var saved = references.FirstOrDefault(d => d.Flow == flow && d.Id == id);
            if (saved != null)
            {
                var names = live.Where(d => d.Flow == flow && string.Equals(d.Name, saved.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (names.Count == 1) return names[0].Id;
            }
            warnings.Add("Device unavailable or ambiguous: " + (saved?.Name ?? id) + ". Its saved selection is kept for reconnection."); return id;
        }
        settings.Outputs = settings.Outputs.Select(id => Map(id, AudioFlow.Output)).ToHashSet();
        settings.Inputs = settings.Inputs.Select(id => Map(id, AudioFlow.Input)).ToHashSet();
        foreach (var profile in settings.Profiles)
        {
            if (profile.OutputId != null) profile.OutputId = Map(profile.OutputId, AudioFlow.Output);
            if (profile.InputId != null) profile.InputId = Map(profile.InputId, AudioFlow.Input);
        }
    }
    public static async Task<string> ApplyAsync(SettingsStore store, SettingsImport imported, IReadOnlyList<AudioDevice> devices)
    {
        Validate(imported.Settings); var previous = store.Load(); var previousStartup = StartupRegistration.IsEnabled();
        var backupDirectory = Path.Combine(Path.GetDirectoryName(store.FilePath)!, "Backups"); Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, "Sonora-before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff") + ".txt");
        ExportFile(backupPath, previous, devices, previousStartup);
        try
        {
            if (imported.StartWithWindows is bool startup) await StartupRegistration.SetAsync(startup);
            store.Save(imported.Settings);
        }
        catch
        {
            if (imported.StartWithWindows.HasValue && StartupRegistration.IsEnabled() != previousStartup) await StartupRegistration.SetAsync(previousStartup);
            throw;
        }
        return backupPath;
    }
    public static string? FindSoundSwitchConfiguration() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundSwitch", "SoundSwitchConfiguration.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundSwitch", "SoundSwitchConfiguration.json")
    }.FirstOrDefault(File.Exists);
}
