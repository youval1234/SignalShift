using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SonarShift;

public static class Diagnostics
{
    public static void Run(string[] args)
    {
        var report = new List<object>(); var failed = false;
        var reportPath = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(SettingsStore.DirectoryPath, "diagnostics.json");
        void Check(string name, Action action)
        {
            try { action(); report.Add(new { test = name, passed = true }); }
            catch (Exception ex) { failed = true; report.Add(new { test = name, passed = false, error = ex.ToString() }); }
        }
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var audio = new AudioService();
        Check("Enumerate real playback and recording endpoints", () =>
        {
            var outputs = audio.Enumerate(AudioFlow.Output); var inputs = audio.Enumerate(AudioFlow.Input);
            Require(outputs.Count > 0, "No playback endpoints"); Require(inputs.Count > 0, "No recording endpoints");
            Require(outputs.Any(d => d.IsDefault), "Playback default missing"); Require(inputs.Any(d => d.IsDefault), "Recording default missing");
            report.Add(new { playback = outputs.Select(d => d.Name), recording = inputs.Select(d => d.Name) });
        });
        Check("Cycle skips disconnected and unselected endpoints, wraps, handles external defaults and empty selection", () =>
        {
            var devices = new[] { new AudioDevice("a", "A", AudioFlow.Output, false, false), new AudioDevice("b", "B", AudioFlow.Output, false, false), new AudioDevice("c", "C", AudioFlow.Output, false, false) };
            var chosen = new HashSet<string> { "a", "c", "unplugged" };
            Require(CyclePlanner.Next(devices, chosen, "a")?.Id == "c", "Did not skip unselected endpoint");
            Require(CyclePlanner.Next(devices, chosen, "c")?.Id == "a", "Did not wrap");
            Require(CyclePlanner.Next(devices, chosen, "b")?.Id == "a", "External default not handled");
            Require(CyclePlanner.Next(devices, new(), "a") == null, "Empty cycle should be a no-op");
        });
        Check("Settings roundtrip and corrupt-file recovery preserve a backup", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SonarShiftTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new SettingsStore(Path.Combine(directory, "settings.json")); var settings = new Settings { Outputs = new() { "speaker-α" }, Profiles = new() { new AudioProfile { Name = "Desk", OutputId = "speaker-α", Hotkey = new(3, 0x41, "Ctrl + Alt + A") } } };
                store.Save(settings); var loaded = store.Load(); Require(loaded.Outputs.Contains("speaker-α") && loaded.Profiles[0].Name == "Desk", "Settings did not survive save/load");
                File.WriteAllText(store.FilePath, "{broken"); store.Load(); Require(Directory.GetFiles(directory, "*.invalid-*").Length == 1, "Corrupt settings backup missing");
            }
            finally { Directory.Delete(directory, true); }
        });
        Check("Windows global hotkey registration detects conflicts and releases reservations", () =>
        {
            using var first = new HwndSource(new HwndSourceParameters("SonarShiftDiagnostics1") { Width = 0, Height = 0, WindowStyle = 0 });
            using var second = new HwndSource(new HwndSourceParameters("SonarShiftDiagnostics2") { Width = 0, Height = 0, WindowStyle = 0 });
            using var a = new HotkeyService(first.Handle); using var b = new HotkeyService(second.Handle);
            var key = new Shortcut(7, 0x87, "Ctrl + Alt + Shift + F24");
            Require(a.Register(1, key, () => { }), "Could not register diagnostic shortcut"); Require(!b.Register(1, key, () => { }), "Conflicting shortcut was accepted");
            a.Clear(); Require(b.Register(1, key, () => { }), "Released shortcut unavailable");
            Require(!a.Register(2, new(3, 0x7B, "F12"), () => { }), "Reserved F12 accepted");
        });
        Check("Notepad text export restores all preferences, profiles, Unicode names and startup choice", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SonarShiftTransfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var devices = new[] { new AudioDevice("speaker", "Speakers — שולחן", AudioFlow.Output, true, false), new AudioDevice("mic", "Microphone α", AudioFlow.Input, true, false) };
                var original = new Settings { Outputs = new() { "speaker" }, Inputs = new() { "mic" }, OutputHotkeyEnabled = false, CommunicationsToo = false, Notifications = false, HotkeySounds = false, CloseToTray = false, StartMinimized = true, Initialized = true,
                    Profiles = new() { new AudioProfile { Name = "Quiet — שולחן", OutputId = "speaker", InputId = "mic", Hotkey = new(3, 0x41, "Ctrl + Alt + A") } } };
                var path = Path.Combine(directory, "settings.txt"); SettingsTransfer.ExportFile(path, original, devices, false);
                Require(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "Notepad UTF-8 marker missing");
                var restored = SettingsTransfer.ReadFile(path, new Settings(), devices);
                Require(JsonSerializer.Serialize(restored.Settings) == JsonSerializer.Serialize(original), "An exported preference or profile was lost");
                Require(restored.StartWithWindows == false && restored.Warnings.Count == 0, "Startup choice or device mapping was lost");
                var oldExport = File.ReadAllText(path).Replace("SonarShift settings", "Sonora settings", StringComparison.Ordinal);
                var oldRestored = SettingsTransfer.Parse(oldExport, new Settings(), devices);
                Require(JsonSerializer.Serialize(oldRestored.Settings) == JsonSerializer.Serialize(original), "Legacy Sonora exports must preserve every setting after rebranding");
                Require(SettingsStore.DirectoryPath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sonora"), "Rebranding must not strand the existing settings folder");
                var legacy = JsonNode.Parse(JsonSerializer.Serialize(original))!.AsObject(); legacy.Remove("HotkeySounds");
                Require(SettingsTransfer.Parse(legacy.ToJsonString(), new Settings(), devices).Settings.HotkeySounds, "Existing exports must enable confirmation sounds by default");
            }
            finally { Directory.Delete(directory, true); }
        });
        Check("SoundSwitch migration maps changed endpoint IDs by exact name and direction, preserving unsupported choices", () =>
        {
            var devices = new[] { new AudioDevice("new-output", "Sonar Gaming", AudioFlow.Output, true, false), new AudioDevice("new-input", "Sonar Microphone", AudioFlow.Input, true, false), new AudioDevice("wrong-direction", "Sonar Gaming", AudioFlow.Input, false, false) };
            var fixture = JsonSerializer.Serialize(new { SelectedDevices = new[] { new { Id = "old-output", Name = "Sonar Gaming", Type = 0 }, new { Id = "old-input", Name = "Sonar Microphone", Type = 1 }, new { Id = "offline", Name = "Unplugged headphones", Type = 0 } },
                PlaybackHotKey = new { Keys = 9, Modifier = 2, Enabled = true }, RecordingHotKey = new { Keys = 118, Modifier = 3, Enabled = false }, ChangeCommunications = false, SwitchDeviceNotification = 2 });
            var current = new Settings { Profiles = new() { new AudioProfile { Name = "Keep my SonarShift profile", OutputId = "new-output" } } }; var before = JsonSerializer.Serialize(current);
            var result = SettingsTransfer.Parse(fixture, current, devices);
            Require(result.Settings.Outputs.SetEquals(new[] { "new-output", "offline" }) && result.Settings.Inputs.SetEquals(new[] { "new-input" }), "Endpoint IDs were not remapped correctly");
            Require(result.Settings.OutputShortcut.Label == "Ctrl + Tab" && result.Settings.InputShortcut.Label == "Ctrl + Alt + F7", "SoundSwitch shortcuts were not converted correctly");
            Require(result.Settings.OutputHotkeyEnabled && !result.Settings.InputHotkeyEnabled && !result.Settings.CommunicationsToo && !result.Settings.Notifications, "SoundSwitch toggles were lost");
            Require(result.Settings.Profiles.Count == 1 && result.Warnings.Any(w => w.Contains("Unplugged headphones")), "Existing profile or disconnected device was lost");
            Require(JsonSerializer.Serialize(current) == before, "Previewing an import mutated the current settings");
            var ambiguous = devices.Concat(new[] { new AudioDevice("another-output", "Sonar Gaming", AudioFlow.Output, false, false) }).ToArray();
            Require(SettingsTransfer.Parse(fixture, current, ambiguous).Settings.Outputs.Contains("old-output"), "Ambiguous names must not choose a random device");
        });
        Check("Invalid, incomplete, future-version and duplicate-shortcut imports are rejected without data loss", () =>
        {
            var current = new Settings(); var before = JsonSerializer.Serialize(current);
            var duplicate = SettingsTransfer.Clone(current); duplicate.InputShortcut = duplicate.OutputShortcut;
            foreach (var invalid in new[] { "{broken", "{}", "null", "{\"Format\":\"SonarShift settings\",\"FormatVersion\":999}", "{\"SchemaVersion\":1}", JsonSerializer.Serialize(duplicate) })
            {
                var rejected = false; try { SettingsTransfer.Parse(invalid, current, Array.Empty<AudioDevice>()); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "An invalid settings file was accepted: " + invalid);
            }
            Require(JsonSerializer.Serialize(current) == before, "A failed import changed current settings");
        });
        Check("Applying an import makes a restorable text backup before replacing the settings", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SonarShiftBackup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var store = new SettingsStore(Path.Combine(directory, "settings.json")); var original = new Settings { Notifications = false }; store.Save(original);
                var imported = new Settings { StartMinimized = true }; var backup = SettingsTransfer.ApplyAsync(store, new(imported, "Test", null, new()), Array.Empty<AudioDevice>()).GetAwaiter().GetResult();
                Require(File.Exists(backup) && store.Load().StartMinimized, "Import was not committed with a backup");
                var restored = SettingsTransfer.ReadFile(backup, new Settings(), Array.Empty<AudioDevice>()).Settings;
                Require(!restored.Notifications && !restored.StartMinimized, "Backup does not restore the original preferences");
            }
            finally { Directory.Delete(directory, true); }
        });
        Check("Embedded confirmation chimes decode as distinct, short, quiet PCM audio", () =>
        {
            var waves = new List<byte[]>();
            foreach (var filename in new[] { "SwitchOutput.wav", "SwitchInput.wav" })
            {
                using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + filename)).Stream;
                using var bytes = new MemoryStream(); resource.CopyTo(bytes); waves.Add(bytes.ToArray()); bytes.Position = 0;
                using var reader = new BinaryReader(bytes);
                Require(new string(reader.ReadChars(4)) == "RIFF", "Missing WAV header"); reader.ReadUInt32();
                Require(new string(reader.ReadChars(4)) == "WAVE" && new string(reader.ReadChars(4)) == "fmt ", "Invalid WAV format");
                Require(reader.ReadUInt32() == 16 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 1, "Expected mono PCM audio");
                var rate = reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt16(); Require(reader.ReadUInt16() == 16, "Expected 16-bit samples");
                Require(new string(reader.ReadChars(4)) == "data", "Missing audio samples"); var size = reader.ReadUInt32();
                var duration = size / (rate * 2.0); Require(duration > 0.05 && duration < 0.3, "Confirmation sound must be brief");
                var peak = 0; for (var i = 0; i < size / 2; i++) peak = Math.Max(peak, Math.Abs((int)reader.ReadInt16()));
                Require(peak > 500 && peak < 0.15 * short.MaxValue, "Chime should be audible and quiet without clipping");
            }
            Require(!waves[0].SequenceEqual(waves[1]), "Input and output confirmations should be distinguishable");
            using var player = new SoundCueService(); player.Preload();
        });
        Check("Rapid notifications replace one popup immediately, reset expiry and never steal focus or reappear", () =>
        {
            void Wait(TimeSpan duration)
            {
                var frame = new DispatcherFrame();
                var wait = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
                wait.Tick += (_, _) => { wait.Stop(); frame.Continue = false; };
                wait.Start(); Dispatcher.PushFrame(frame);
            }
            using var popup = new SwitchNotification();
            var foreground = SwitchNotification.GetForegroundWindow(); var activations = 0;
            popup.Activated += (_, _) => activations++;
            popup.Present("Output switched", "First device", AudioFlow.Output);
            Require(SwitchNotification.GetForegroundWindow() == foreground, "Popup took foreground focus");
            var handle = new WindowInteropHelper(popup).Handle;
            Wait(SwitchNotification.HoldDuration - TimeSpan.FromMilliseconds(100));
            for (var i = 0; i < 50; i++) popup.Present("Output switched", "Device " + i, AudioFlow.Output);
            popup.Present("Input switched", "Latest microphone", AudioFlow.Input);
            var foregroundAfterReplacement = SwitchNotification.GetForegroundWindow();
            Require(popup.CurrentMessage == "Latest microphone" && popup.IsVisible, "Latest event was delayed or queued");
            Require(new WindowInteropHelper(popup).Handle == handle, "Rapid switches created another popup");
            Wait(TimeSpan.FromMilliseconds(350));
            Require(popup.IsVisible && popup.Opacity == 1, "Old expiry dismissed or faded a newer notification");
            Require(activations == 0 && !popup.ShowInTaskbar, "Popup activated or appeared in the taskbar");
            Require(foregroundAfterReplacement != handle, "Popup took foreground focus on replacement");
            // Replace content during the fade, too; stale fades must not dismiss it.
            Wait(SwitchNotification.HoldDuration - TimeSpan.FromMilliseconds(280));
            popup.Present("Profile activated", "Evening setup");
            Wait(TimeSpan.FromMilliseconds(200));
            Require(popup.IsVisible && popup.Opacity == 1 && popup.CurrentMessage == "Evening setup", "Update during fade was lost");
            Wait(SwitchNotification.HoldDuration + TimeSpan.FromMilliseconds(300));
            Require(!popup.IsVisible, "Popup did not dismiss after the most recent event");
            Wait(TimeSpan.FromMilliseconds(250));
            Require(!popup.IsVisible, "An old queued notification reappeared");
        });
        if (args.Contains("--audio-roundtrip"))
        {
            foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input })
            {
                Check(flow + " default switch changes all roles and restores original endpoints", () =>
                {
                    var original = Enumerable.Range(0, 3).Select(role => (Role: role, Id: audio.GetDefaultId(flow, role))).ToList();
                    var target = audio.Enumerate(flow).FirstOrDefault(d => d.Id != original[1].Id) ?? throw new InvalidOperationException("Need a second endpoint to verify switching");
                    try
                    {
                        audio.SetDefault(target, true);
                        foreach (var role in Enumerable.Range(0, 3)) Require(audio.GetDefaultId(flow, role) == target.Id, "Default endpoint did not change for role " + role);
                    }
                    finally
                    {
                        foreach (var endpoint in original) if (endpoint.Id != null) audio.SetRoles(endpoint.Id, new[] { endpoint.Role });
                        foreach (var endpoint in original) Require(audio.GetDefaultId(flow, endpoint.Role) == endpoint.Id, "Original endpoint failed to restore");
                    }
                });
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { success = !failed, checks = report }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failed ? 1 : 0;
    }
}
