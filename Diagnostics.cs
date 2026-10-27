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
        UpdaterChecks.Run(Check, Require);
        Check("Reset defaults uses current devices, clears customization and preserves a restorable backup on failure", () =>
        {
            var devices = new[] { new AudioDevice("out", "Speakers", AudioFlow.Output, true, true), new AudioDevice("other", "Headphones", AudioFlow.Output, false, false), new AudioDevice("in", "Microphone", AudioFlow.Input, true, true) };
            var defaults = SettingsTransfer.CreateDefaults(devices);
            Require(defaults.Initialized && defaults.Outputs.SetEquals(new[] { "out" }) && defaults.Inputs.SetEquals(new[] { "in" }), "Default cycles did not select only current devices");
            Require(defaults.Theme == "Dark" && defaults.Profiles.Count == 0 && defaults.AutomaticUpdates && !defaults.StartMicrophoneLive && !defaults.MicrophoneMuteHotkeyEnabled && !defaults.ProfileCycleHotkeyEnabled && defaults.ShowMicrophoneTrayIcon, "Factory preferences differ");
            SettingsTransfer.Validate(defaults);
            var directory = Path.Combine(Path.GetTempPath(), "SignalShift-reset-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(Path.Combine(directory, "settings.json"));
                var previous = new Settings { Theme = "Scary", AutomaticUpdates = false, Profiles = new() { new() { Name = "Custom", OutputId = "out" } } };
                store.Save(previous);
                // Startup registration is deliberately excluded from diagnostics on the real PC.
                var imported = new SettingsImport(defaults, "Defaults", null, new());
                using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var rejected = false;
                    try { SettingsTransfer.ApplyAsync(store, imported, devices).GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
                    Require(rejected, "Reset ignored a settings lock");
                }
                Require(store.Load().Theme == "Scary", "Failed reset changed existing preferences");
                var backup = SettingsTransfer.ApplyAsync(store, imported, devices).GetAwaiter().GetResult();
                Require(store.Load().Theme == "Dark" && store.Load().Profiles.Count == 0, "Reset was not persisted");
                var restored = SettingsTransfer.ReadFile(backup, defaults, devices).Settings;
                Require(restored.Theme == "Scary" && !restored.AutomaticUpdates && restored.Profiles.Single().Name == "Custom", "Backup cannot restore customization");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        Check("Audio notifications distinguish default changes from topology changes and stop safely", () =>
        {
            var seen = new List<AudioChangeKind>();
            var callback = new AudioDeviceNotifications.Callback(kind => seen.Add(kind));
            callback.OnDefaultDeviceChanged(0, 1, "a"); callback.OnDeviceAdded("a"); callback.OnDeviceRemoved("a");
            callback.OnDeviceStateChanged("a", 1); callback.OnPropertyValueChanged("a", default); callback.OnDefaultDeviceChanged(99, 1, "a");
            Require(seen.Count == 6 && seen[0] == AudioChangeKind.Defaults && seen.Skip(1).All(k => k == AudioChangeKind.Topology), "Unsafe notification classification");
            callback.Stop(); callback.OnDeviceAdded("b"); Require(seen.Count == 6, "Disposed callback still dispatched");
            var throwing = new AudioDeviceNotifications.Callback((AudioChangeKind _) => throw new InvalidOperationException("test"));
            Require(throwing.OnDeviceAdded("a") == 0, "Exception escaped native callback");
        });
        Check("Cached defaults preserve device metadata and require rescans for unknown IDs", () =>
        {
            IReadOnlyList<AudioDevice> devices = new[] { new AudioDevice("a", "One", AudioFlow.Output, true, false), new AudioDevice("b", "Two", AudioFlow.Output, false, true) };
            Require(DeviceDefaults.TryApply(devices, "a", "b", out var unchanged) && ReferenceEquals(devices, unchanged), "Unchanged defaults allocated a new snapshot");
            Require(DeviceDefaults.TryApply(devices, "b", "a", out var next) && next[1].IsDefault && next[0].IsCommunications && !next[0].IsDefault && !next[1].IsCommunications, "Default roles were mixed up");
            Require(next[0].Name == "One" && devices[0].IsDefault, "Cache metadata or original snapshot was mutated");
            Require(!DeviceDefaults.TryApply(devices, "new-device", "a", out var rejected) && ReferenceEquals(rejected, devices), "Unknown device bypassed a full scan");
            Require(DeviceDefaults.TryApply(devices, null, null, out var emptyDefaults) && emptyDefaults.All(d => !d.IsDefault && !d.IsCommunications), "Removed defaults stayed selected");
        });
        Check("Tray layout cache detects edits, order and topology without rebuilding for default flags", () =>
        {
            var devices = new[] { new AudioDevice("a", "One", AudioFlow.Output, true, false), new AudioDevice("b", "Two", AudioFlow.Output, false, false) };
            var profiles = new List<AudioProfile> { new() { Id = "first", Name = "Desk", OutputId = "a" }, new() { Id = "second", Name = "Calls", OutputId = "b" } };
            var cache = new TrayLayoutCache(); var inputs = Array.Empty<AudioDevice>();
            Require(cache.Update(devices, inputs, profiles), "Initial tray not built");
            Require(!cache.Update(new[] { devices[0] with { IsDefault = false }, devices[1] with { IsDefault = true } }, inputs, profiles), "Default flags rebuilt the tray layout");
            profiles[0].Name = "Edited"; Require(cache.Update(devices, inputs, profiles), "In-place profile rename missed");
            profiles[0].OutputId = "b"; Require(cache.Update(devices, inputs, profiles), "Profile routing edit missed");
            profiles.Reverse(); Require(cache.Update(devices, inputs, profiles), "Profile order change missed");
            devices[0] = devices[0] with { Name = "Renamed" }; Require(cache.Update(devices, inputs, profiles), "In-place device rename missed");
            Require(cache.Update(new[] { devices[1] }, inputs, profiles), "Device removal missed");
            Require(!cache.Update(new[] { devices[1] }, inputs, profiles), "Unchanged tray rebuilt");
            var stable = new[] { devices[1] }; cache.Update(stable, inputs, profiles);
            var allocated = GC.GetTotalAllocatedBytes(true);
            for (var i = 0; i < 10000; i++) cache.Update(stable, inputs, profiles);
            Require(GC.GetTotalAllocatedBytes(true) - allocated < 50000, "Stable tray metadata still allocates on every refresh");
        });
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) Check("Second launch shows the window with one IPC request before requesting updates", () =>
        {
            using var mutex = new System.Threading.Mutex(true, HostConnection.InstanceMutex, out var owner);
            Require(owner, "Startup test instance already exists");
            using var stop = new System.Threading.CancellationTokenSource();
            var commands = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var listener = HostConnection.ListenAsync(command => { commands.Enqueue(command); return System.Threading.Tasks.Task.FromResult("{}"); }, stop.Token);
            try
            {
                using var client = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true })!;
                Require(client.WaitForExit(10000) && client.ExitCode == 0, "Second launch did not complete");
                Require(commands.Count == 1 && commands.TryPeek(out var first) && first == "show-launch", "Window display waited for an extra update-check request");
            }
            finally { stop.Cancel(); listener.GetAwaiter().GetResult(); mutex.ReleaseMutex(); }
        });
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) Check("Microphone command sends one toggle and rejects legacy or failed hosts without retrying", () =>
        {
            foreach (var scenario in new[] { "success", "legacy", "failure" })
            {
                using var stop = new System.Threading.CancellationTokenSource(); var toggles = 0;
                var listener = HostConnection.ListenAsync(command =>
                {
                    if (command == "status") return System.Threading.Tasks.Task.FromResult(scenario == "legacy" ? "{\"pid\":1}" : "{\"microphoneMute\":\"Live\"}");
                    System.Threading.Interlocked.Increment(ref toggles);
                    return System.Threading.Tasks.Task.FromResult(scenario == "failure" ? "{\"success\":false,\"error\":\"test failure\"}" : "{\"success\":true,\"microphoneMute\":\"Muted\"}");
                }, stop.Token);
                try
                {
                    var rejected = false;
                    try { _ = MicrophoneCommand.Run(); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected == (scenario != "success"), "Unexpected command result: " + scenario);
                    Require(toggles == (scenario == "legacy" ? 0 : 1), "A toggle was retried or sent to a legacy host");
                }
                finally { stop.Cancel(); Require(listener.Wait(2000), "Command fixture did not stop"); }
            }
        });
        Check("Microphone mute settings migrate safely, roundtrip and reject conflicts", () =>
        {
            var old = JsonNode.Parse(JsonSerializer.Serialize(new Settings()))!.AsObject();
            old.Remove("MicrophoneMuteShortcut"); old.Remove("MicrophoneMuteHotkeyEnabled");
            var migrated = SettingsTransfer.Parse(old.ToJsonString(), new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(!migrated.MicrophoneMuteHotkeyEnabled && migrated.MicrophoneMuteShortcut.Key == 0x4D, "Upgrade unexpectedly claimed a mute shortcut");
            migrated.MicrophoneMuteHotkeyEnabled = true;
            var loaded = SettingsTransfer.Parse(SettingsTransfer.Export(migrated, Array.Empty<AudioDevice>(), false), new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(loaded.MicrophoneMuteHotkeyEnabled && loaded.MicrophoneMuteShortcut == migrated.MicrophoneMuteShortcut, "Mute shortcut was lost");
            migrated.MicrophoneMuteShortcut = migrated.OutputShortcut;
            var rejected = false; try { SettingsTransfer.Validate(migrated); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Conflicting mute shortcut was accepted");
        });
        Check("Microphone state distinguishes muted, live, mixed and unavailable", () =>
        {
            Require(MicrophoneMuteService.Classify(Array.Empty<MicrophoneMuteSnapshot>()) == MicrophoneMuteState.Unavailable, "Missing microphone reported live");
            Require(MicrophoneMuteService.Classify(new[] { new MicrophoneMuteSnapshot("a", true) }) == MicrophoneMuteState.Muted, "Muted input reported live");
            Require(MicrophoneMuteService.Classify(new[] { new MicrophoneMuteSnapshot("a", false) }) == MicrophoneMuteState.Live, "Live input reported muted");
            Require(MicrophoneMuteService.Classify(new[] { new MicrophoneMuteSnapshot("a", false), new MicrophoneMuteSnapshot("b", true) }) == MicrophoneMuteState.Mixed, "Mixed role states lost");
        });
        Check("Microphone startup opt-in preserves old settings and survives export", () =>
        {
            var old = JsonNode.Parse(JsonSerializer.Serialize(new Settings()))!.AsObject(); old.Remove("StartMicrophoneLive");
            var migrated = SettingsTransfer.Parse(old.ToJsonString(), new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(!migrated.StartMicrophoneLive, "Upgrade unexpectedly unmutes microphones");
            migrated.StartMicrophoneLive = true;
            var loaded = SettingsTransfer.Parse(SettingsTransfer.Export(migrated, Array.Empty<AudioDevice>(), true), new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(loaded.StartMicrophoneLive, "Sign-in microphone choice was lost");
        });
        Check("Sign-in microphone policy waits briefly, applies once and respects cancellation", () =>
        {
            var writes = 0; Action makeLive = () => writes++;
            var disabled = new MicrophoneStartupPolicy(false);
            disabled.TryApply(MicrophoneMuteState.Muted, TimeSpan.Zero, makeLive);
            Require(writes == 0, "Ordinary startup changed mute");
            var pending = new MicrophoneStartupPolicy(true);
            Require(!pending.TryApply(MicrophoneMuteState.Unavailable, TimeSpan.FromSeconds(2), makeLive) && pending.Pending, "Delayed device was not awaited");
            Require(pending.TryApply(MicrophoneMuteState.Mixed, TimeSpan.FromSeconds(3), makeLive), "Connected microphones were not made live");
            pending.TryApply(MicrophoneMuteState.Muted, TimeSpan.FromSeconds(4), makeLive);
            Require(writes == 1 && !pending.Pending, "Later intentional mute was overwritten");
            var cancelled = new MicrophoneStartupPolicy(true); cancelled.Cancel(); cancelled.TryApply(MicrophoneMuteState.Muted, TimeSpan.Zero, makeLive);
            var expired = new MicrophoneStartupPolicy(true); expired.TryApply(MicrophoneMuteState.Muted, TimeSpan.FromSeconds(30), makeLive);
            Require(writes == 1 && !expired.Pending, "Cancelled or expired policy opened a microphone");
            var live = new MicrophoneStartupPolicy(true); live.TryApply(MicrophoneMuteState.Live, TimeSpan.Zero, makeLive);
            Require(writes == 1 && !live.Pending, "Already-live microphone was rewritten");
            var failed = new MicrophoneStartupPolicy(true);
            try { failed.TryApply(MicrophoneMuteState.Muted, TimeSpan.Zero, () => throw new InvalidOperationException("fixture")); } catch (InvalidOperationException) { }
            Require(!failed.Pending, "Failed startup keeps trying to override mute");
        });
        Check("Microphone tray icons are distinct, cached and have accessible state labels", () =>
        {
            using var icons = new MicrophoneTrayIcons();
            var handles = new HashSet<IntPtr>();
            foreach (var state in Enum.GetValues<MicrophoneMuteState>())
            {
                Require(handles.Add(icons.For(state).Handle), "States share one icon");
                Require(MicrophoneTrayIcons.Tooltip(state).Contains(MicrophoneTrayIcons.Description(state)), "Tray state is not labelled");
                Require(MicrophoneTrayIcons.Tooltip(state, true).Length <= 63, "Tray tooltip exceeds the Windows limit");
            }
            var muted = icons.For(MicrophoneMuteState.Muted); _ = MicrophoneTrayIcons.Tooltip(MicrophoneMuteState.Muted);
            var same = true;
            void Lookups() { for (var index = 0; index < 10000; index++) { same &= ReferenceEquals(muted, icons.For(MicrophoneMuteState.Muted)); _ = MicrophoneTrayIcons.Tooltip(MicrophoneMuteState.Muted); } }
            Lookups(); Lookups(); Lookups();
            var allocated = GC.GetAllocatedBytesForCurrentThread(); Lookups();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Require(same, "Refresh replaced its icon");
            Require(bytes < 50000, "Tray state refresh allocated " + bytes + " bytes");
            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!; Directory.CreateDirectory(directory);
            foreach (var state in Enum.GetValues<MicrophoneMuteState>()) { using var image = icons.For(state).ToBitmap(); image.Save(Path.Combine(directory, "microphone-tray-" + state + ".png")); }
            BackgroundFeedback.ExportMicrophonePreview(Path.Combine(directory, "microphone-muted-popup.png"), true);
            BackgroundFeedback.ExportMicrophonePreview(Path.Combine(directory, "microphone-live-popup.png"), false);
        });
        Check("Separate microphone indicator preserves the app icon, reuses updates and leaves menu ownership with the host", () =>
        {
            using var brand = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            using var main = new System.Windows.Forms.NotifyIcon { Icon = brand, Text = "SignalShift", Visible = true };
            using var menu = new System.Windows.Forms.ContextMenuStrip(); menu.Items.Add("Open SignalShift"); main.ContextMenuStrip = menu;
            using var indicator = new MicrophoneTrayIndicator(() => { }); indicator.SetMenu(menu);
            foreach (var state in Enum.GetValues<MicrophoneMuteState>())
            {
                indicator.Update(state);
                Require(indicator.Matches(state) && indicator.IsSeparateFrom(main), "Microphone status replaced the app icon");
                Require(ReferenceEquals(main.Icon, brand) && main.Text == "SignalShift", "App branding changed with microphone state");
            }
            indicator.Update(MicrophoneMuteState.Muted);
            void Updates() { for (var index = 0; index < 10000; index++) indicator.Update(MicrophoneMuteState.Muted); }
            Updates(); Updates(); Updates(); var allocated = GC.GetAllocatedBytesForCurrentThread(); Updates();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Require(bytes < 50000, "Unchanged microphone indicator updates allocate: " + bytes);
            indicator.SetVisible(false);
            Require(indicator.Matches(MicrophoneMuteState.Muted) && indicator.IsSeparateFrom(main), "Hiding microphone icon disturbed app identity or cached state");
            indicator.SetVisible(true);
            Require(indicator.Matches(MicrophoneMuteState.Muted) && main.Visible, "Showing microphone icon disturbed app tray");
            indicator.Dispose();
            Require(!menu.IsDisposed && main.Visible && ReferenceEquals(main.Icon, brand), "Disposing microphone indicator destroyed the app tray/menu");
        });
        Check("Microphone tray visibility stays enabled on upgrade and roundtrips independently of mute", () =>
        {
            var old = JsonNode.Parse(JsonSerializer.Serialize(new Settings()))!.AsObject(); old.Remove("ShowMicrophoneTrayIcon");
            var migrated = SettingsTransfer.Parse(old.ToJsonString(), new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(migrated.ShowMicrophoneTrayIcon, "Upgrade unexpectedly hid the microphone icon");
            migrated.ShowMicrophoneTrayIcon = false;
            var restored = SettingsTransfer.Parse(SettingsTransfer.Export(migrated, Array.Empty<AudioDevice>(), false), new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(!restored.ShowMicrophoneTrayIcon && !restored.StartMicrophoneLive, "Icon preference changed microphone startup behavior");
        });
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) Check("Microphone visibility setting saves and rolls back its checkbox when settings are locked", () =>
        {
            var window = new MainWindow(previewMode: true, previewPage: "Settings");
            _ = new WindowInteropHelper(window).EnsureHandle();
            System.Windows.Controls.CheckBox? Find(DependencyObject root)
            {
                if (root is System.Windows.Controls.CheckBox check && System.Windows.Automation.AutomationProperties.GetName(check) == "Show separate microphone status icon") return check;
                foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) if (Find(child) is { } result) return result;
                return null;
            }
            var visible = Find(window) ?? throw new InvalidOperationException("Microphone visibility setting missing");
            visible.IsChecked = false; visible.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(!new SettingsStore().Load().ShowMicrophoneTrayIcon && !window.Preferences.ShowMicrophoneTrayIcon, "Visibility choice was not saved");
            using (var locked = new FileStream(new SettingsStore().FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                visible.IsChecked = true; visible.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(visible.IsChecked == false && !window.Preferences.ShowMicrophoneTrayIcon, "Failed save left a misleading visible state");
            }
            visible.IsChecked = true; visible.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(new SettingsStore().Load().ShowMicrophoneTrayIcon, "Checkbox could not recover after settings lock");
            ThemePalette.Apply("Dark");
        });
        Check("Live device watcher coexists with audio-test COM wrappers", () =>
        {
            using var watcher = new AudioDeviceNotifications(() => { });
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input }) _ = new AudioService().Enumerate(flow);
            var endpoints = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.All, NAudio.CoreAudioApi.DeviceState.Active);
            Require(endpoints.Count >= 0, "Could not enumerate audio-test endpoints");
        });
        Check("Retired theme shortcut is ignored and omitted from exports", () =>
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(new Settings()))!.AsObject();
            json["Theme"] = "Light"; json["ThemeHotkeyEnabled"] = true;
            json["ThemeShortcut"] = new JsonObject { ["Modifiers"] = 0, ["Key"] = 0, ["Label"] = "retired" };
            var loaded = SettingsTransfer.Parse(json.ToJsonString(), new Settings(), Array.Empty<AudioDevice>()).Settings;
            var exported = SettingsTransfer.Export(loaded, Array.Empty<AudioDevice>(), false);
            Require(loaded.Theme == "Light" && !exported.Contains("ThemeShortcut") && !exported.Contains("ThemeHotkeyEnabled"), "Retired theme hotkey survived migration");
        });
        Check("Microphone test clips stay bounded, frame aligned and clear on disposal", () =>
        {
            using var clip = new AudioDeviceTest.Clip();
            var packet = new byte[9601]; Array.Fill(packet, (byte)127);
            for (var i = 0; i < 1000; i++) clip.Append(packet, packet.Length);
            Require(clip.Length == AudioDeviceTest.MaximumBytes && clip.Length % 2 == 0, "Capture buffer exceeded its five-second limit");
            using var stream = clip.Open();
            Require(stream.TotalTime.TotalSeconds == 5 && clip.Peak > 0, "Unexpected test format or missing meter");
            clip.Dispose();
            clip.Append(packet, packet.Length);
            Require(clip.Length == 0 && stream.ReadByte() == 0 && clip.Peak == 0, "Recording bytes were not erased");
        });
        Check("Output test tone is short, bounded and faded", () =>
        {
            using var tone = AudioDeviceTest.Tone();
            Require(tone.TotalTime.TotalSeconds == 1 && tone.ReadByte() == 0 && tone.ReadByte() == 0, "Tone duration or attack is wrong");
            var maximum = 0; using var reader = new BinaryReader(tone);
            while (tone.Position < tone.Length) maximum = Math.Max(maximum, Math.Abs((int)reader.ReadInt16()));
            Require(maximum > 1000 && maximum < short.MaxValue * 0.13, "Tone exceeded its quiet amplitude");
        });
        Check("Cancelled device tests do not open endpoints", () =>
        {
            using var stop = new System.Threading.CancellationTokenSource(); stop.Cancel();
            using var clip = new AudioDeviceTest.Clip(); using var tone = AudioDeviceTest.Tone();
            bool recordCancelled = false, playCancelled = false;
            try { AudioDeviceTest.RecordAsync("does-not-exist", clip, stop.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { recordCancelled = true; }
            try { AudioDeviceTest.PlayAsync("does-not-exist", tone, stop.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { playCancelled = true; }
            Require(recordCancelled && playCancelled && clip.Length == 0, "Cancelled operation touched audio hardware");
        });
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) Check("Sidebar theme button switches both ways and persists the choice", () =>
        {
            var window = new MainWindow(previewMode: true, previewTheme: "Dark");
            _ = new WindowInteropHelper(window).EnsureHandle();
            System.Windows.Controls.Button? Find(System.Windows.DependencyObject root)
            {
                if (root is System.Windows.Controls.Button button && System.Windows.Automation.AutomationProperties.GetName(button) == "Switch to light mode") return button;
                foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
                    if (Find(child) is { } match) return match;
                return null;
            }
            var toggle = Find(window) ?? throw new InvalidOperationException("Sidebar theme button missing");
            foreach (var expected in new[] { "Light", "Dark" })
            {
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(window.Preferences.Theme == expected && new SettingsStore().Load().Theme == expected, "Theme button did not persist " + expected);
            }
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ThemePalette.Apply(theme);
                foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input })
                {
                    var dialog = window.CreateDeviceTestDialog(new AudioDevice("test", "Test device", flow, false, false));
                    dialog.Measure(new Size(590, 540)); dialog.Arrange(new Rect(0, 0, 590, 540));
                    Require(dialog.Title.Contains("Test"), "Device test dialog could not be built");
                    if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null)
                    {
                        var surface = (FrameworkElement)dialog.Content;
                        surface.Measure(new Size(570, 490)); surface.Arrange(new Rect(0, 0, 570, 490)); surface.UpdateLayout();
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(570, 490, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(surface);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(SettingsStore.DirectoryPath, "test-" + theme + "-" + flow + ".png")); encoder.Save(file);
                    }
                }
            }
            ThemePalette.Apply("Dark");
        });
        Check("Every curated theme roundtrips and keeps readable text, muted text and action labels", () =>
        {
            double Luminance(System.Windows.Media.Color color)
            {
                double Linear(byte channel) { var value = channel / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
                return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
            }
            double Contrast(string theme, string a, string b)
            {
                var first = Luminance((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeCatalog.Resolve(theme, a)));
                var second = Luminance((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeCatalog.Resolve(theme, b)));
                return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
            }
            foreach (var theme in ThemeCatalog.All)
            {
                var settings = new Settings { Theme = theme.Name };
                Require(SettingsTransfer.Parse(SettingsTransfer.Export(settings, Array.Empty<AudioDevice>(), false), new Settings(), Array.Empty<AudioDevice>()).Settings.Theme == theme.Name, "Theme export lost " + theme.Name);
                var previewDirectory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!; Directory.CreateDirectory(previewDirectory);
                BackgroundFeedback.ExportPreview(Path.Combine(previewDirectory, "popup-" + theme.Name + ".png"), theme.Name);
                Require(Contrast(theme.Name, "#F4F4FA", "#0D0F15") >= 4.5, "Unreadable main text: " + theme.Name);
                Require(Contrast(theme.Name, "#9297AC", "#171A23") >= 4.5, "Unreadable secondary text: " + theme.Name);
                Require(Contrast(theme.Name, "#171124", "#B8A3FF") >= 4.5, "Unreadable primary button: " + theme.Name);
                Require(Contrast(theme.Name, "#8BE2C5", "#1D3531") >= 4.5, "Unreadable active state: " + theme.Name);
                if (theme.Name is not ("Dark" or "Light"))
                {
                    Require(Contrast(theme.Name, "#9297AC", "#2E2146") >= 4.5 && Contrast(theme.Name, "#9297AC", "#1A1F2E") >= 4.5, "Unreadable hero text: " + theme.Name);
                    foreach (var token in ThemeCatalog.ResourceKeys) Require(ThemeCatalog.HasToken(theme.Name, token), "Unmapped theme token: " + theme.Name + " " + token);
                }
            }
            var invalid = new Settings { Theme = "Unknown" }; var rejected = false;
            try { SettingsTransfer.Validate(invalid); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Unknown imported theme was accepted");
        });
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) Check("Settings appearance cards apply all themes, persist selection and reuse shared brushes", () =>
        {
            var window = new MainWindow(previewMode: true, previewTheme: "Dark", previewPage: "Settings");
            _ = new WindowInteropHelper(window).EnsureHandle();
            System.Windows.Controls.Button? Find(DependencyObject root, string name)
            {
                if (root is System.Windows.Controls.Button button && System.Windows.Automation.AutomationProperties.GetName(button) == name) return button;
                foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) if (Find(child, name) is { } found) return found;
                return null;
            }
            var background = ThemePalette.Get("#0D0F15");
            foreach (var theme in ThemeCatalog.All)
            {
                var name = "Apply " + theme.Name + " theme";
                var choice = Find(window, name) ?? Find(window, name + ", selected") ?? throw new InvalidOperationException("Theme card missing: " + theme.Name);
                choice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(window.Preferences.Theme == theme.Name && new SettingsStore().Load().Theme == theme.Name, "Settings did not persist " + theme.Name);
                Require(Find(window, name + ", selected") != null, "Selection marker missing: " + theme.Name);
                Require(ReferenceEquals(background, ThemePalette.Get("#0D0F15")), "Theme replaced cached brushes");
            }
            var nextTheme = ThemeCatalog.IsLight(window.Preferences.Theme) ? "Dark" : "Light";
            var themeToggle = Find(window, "Switch to " + nextTheme.ToLowerInvariant() + " mode") ?? throw new InvalidOperationException("Preset did not preserve the sidebar toggle");
            themeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Require(window.Preferences.Theme == nextTheme, "Sidebar cannot leave a custom theme");
            ThemePalette.Apply("Dark");
        });
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) Check("Occupied IPC channel yields and cancels without blocking startup", () =>
        {
            var suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SettingsStore.DirectoryPath)))[..16];
            using var occupied = new System.IO.Pipes.NamedPipeServerStream("SonarShift-" + suffix, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            using var stop = new System.Threading.CancellationTokenSource();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var listener = HostConnection.ListenAsync(_ => System.Threading.Tasks.Task.FromResult("{}"), stop.Token);
            try { Require(watch.ElapsedMilliseconds < 1000 && !listener.IsCompleted, "Listener blocked or stopped on a transient collision"); }
            finally { stop.Cancel(); Require(listener.Wait(2000), "Listener did not cancel promptly"); }
        });
        Check("Older settings preserve audio and do not opt into profile cycling", () =>
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(new Settings()))!.AsObject();
            json.Remove("ProfileCycleShortcut"); json.Remove("ProfileCycleHotkeyEnabled");
            json["CameraEnabled"] = true; json["Cameras"] = new JsonArray("retired-device");
            var loaded = SettingsTransfer.Parse(json.ToJsonString(), new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(!loaded.ProfileCycleHotkeyEnabled && loaded.ProfileCycleShortcut.Key == 0x50, "Upgrade opted into a new hotkey");
            Require(!SettingsTransfer.Export(loaded, Array.Empty<AudioDevice>(), false).Contains("CameraEnabled"), "Removed camera preferences were exported");
        });
        Check("Profile-cycle preferences export, import and reject shortcut conflicts", () =>
        {
            var settings = new Settings { Theme = "Light", ProfileCycleHotkeyEnabled = true };
            var exported = SettingsTransfer.Export(settings, Array.Empty<AudioDevice>(), false);
            var loaded = SettingsTransfer.Parse(exported, new Settings(), Array.Empty<AudioDevice>()).Settings;
            Require(loaded.ProfileCycleHotkeyEnabled && loaded.Theme == "Light" && loaded.ProfileCycleShortcut == settings.ProfileCycleShortcut, "Profile-cycle preferences were lost");
            settings.ProfileCycleShortcut = settings.OutputShortcut;
            bool rejected = false; try { SettingsTransfer.Validate(settings); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Conflicting profile-cycle shortcut was accepted");
        });
        Check("Profile cycle wraps, skips disconnected pairs and follows external audio changes", () =>
        {
            var outputs = new[] { new AudioDevice("o1", "One", AudioFlow.Output, false, false), new AudioDevice("o2", "Two", AudioFlow.Output, false, false) };
            var inputs = new[] { new AudioDevice("i1", "One", AudioFlow.Input, false, false), new AudioDevice("i2", "Two", AudioFlow.Input, false, false) };
            var first = new AudioProfile { OutputId = "o1", InputId = "i1" };
            var missing = new AudioProfile { OutputId = "missing", InputId = "i1" };
            var second = new AudioProfile { OutputId = "o2", InputId = "i2" };
            var profiles = new[] { first, missing, second };
            Require(ProfileCyclePlanner.Next(profiles, outputs, inputs, "o1", "i1") == second, "Did not skip disconnected pair");
            Require(ProfileCyclePlanner.Next(profiles, outputs, inputs, "o2", "i2") == first, "Did not wrap");
            Require(ProfileCyclePlanner.Next(profiles, outputs, inputs, "external", "i1") == first, "External state should start the cycle");
            Require(ProfileCyclePlanner.Next(new[] { missing }, outputs, inputs, "o1", "i1") == null, "Disconnected cycle was not empty");
            Require(ProfileCyclePlanner.Next(Array.Empty<AudioProfile>(), outputs, inputs, null, null) == null, "Empty cycle was not a no-op");
            var duplicate = new AudioProfile { OutputId = "o1", InputId = "i1" };
            Require(ProfileCyclePlanner.Next(new[] { first, duplicate }, outputs, inputs, "o1", "i1", duplicate.Id) == first, "Identical pairs did not wrap by last active profile");
        });
        Check("Active profile follows actual defaults, partial setups and external changes", () =>
        {
            var partial = new AudioProfile { Id = "partial", OutputId = "o1" };
            var first = new AudioProfile { Id = "first", OutputId = "o1", InputId = "i1" };
            var second = new AudioProfile { Id = "second", OutputId = "o2", InputId = "i1" };
            var empty = new AudioProfile { Id = "empty" };
            var profiles = new[] { empty, partial, first, second };
            Require(ProfileSelection.Current(profiles, "o1", "i1") == first.Id, "Complete matching setup should take precedence");
            Require(ProfileSelection.Current(profiles, "o2", "i1", first.Id) == second.Id, "Highlight did not follow changed defaults");
            Require(ProfileSelection.Current(profiles, "o1", "external") == partial.Id, "Leave-unchanged microphone was not respected");
            Require(ProfileSelection.Current(new[] { first, second, empty }, "external", "i1", first.Id) == null, "Stale profile stayed highlighted");
            Require(ProfileSelection.Current(profiles, null, null) == null, "Disconnected defaults matched a profile");
            var duplicate = new AudioProfile { Id = "duplicate", OutputId = "o1", InputId = "i1" };
            Require(ProfileSelection.Current(new[] { first, duplicate }, "o1", "i1", duplicate.Id) == duplicate.Id, "Activated duplicate did not retain precedence");
        });
        Check("Live appearance changes recolor existing text, backgrounds and templates", () =>
        {
            ThemePalette.Apply("Dark");
            var background = ThemePalette.Get("#0D0F15"); var text = ThemePalette.Get("#F4F4FA"); var dark = background.Color;
            ThemePalette.Apply("Light");
            Require(ReferenceEquals(background, ThemePalette.Get("#0D0F15")) && background.Color != dark, "Existing surfaces did not update");
            Require(background.Color.R > text.Color.R, "Light text contrast is inverted");
            Require(Application.Current.FindResource("Theme_222632") is System.Windows.Media.Brush, "Button template palette is missing");
            ThemePalette.Apply("Dark"); Require(background.Color == dark, "Dark appearance was not restored");
        });
        var audio = new AudioService();
        Check("Core Audio notification registration releases its callback without polling", () =>
        {
            using var notifications = new AudioDeviceNotifications(() => { });
        });
        if (!args.Contains("--headless")) Check("Enumerate real playback and recording endpoints", () =>
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
            var directory = Path.Combine(Path.GetTempPath(), "SignalShiftTest-" + Guid.NewGuid().ToString("N"));
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
            using var first = new HwndSource(new HwndSourceParameters("SignalShiftDiagnostics1") { Width = 0, Height = 0, WindowStyle = 0 });
            using var second = new HwndSource(new HwndSourceParameters("SignalShiftDiagnostics2") { Width = 0, Height = 0, WindowStyle = 0 });
            using var a = new HotkeyService(first.Handle); using var b = new HotkeyService(second.Handle);
            var key = new Shortcut(7, 0x87, "Ctrl + Alt + Shift + F24");
            Require(a.Register(1, key, () => { }), "Could not register diagnostic shortcut"); Require(!b.Register(1, key, () => { }), "Conflicting shortcut was accepted");
            a.Clear(); Require(b.Register(1, key, () => { }), "Released shortcut unavailable");
            Require(!a.Register(2, new(3, 0x7B, "F12"), () => { }), "Reserved F12 accepted");
        });
        Check("Notepad text export restores all preferences, profiles, Unicode names and startup choice", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SignalShiftTransfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
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
                var oldExport = File.ReadAllText(path).Replace("SignalShift settings", "Sonora settings", StringComparison.Ordinal);
                var oldRestored = SettingsTransfer.Parse(oldExport, new Settings(), devices);
                Require(JsonSerializer.Serialize(oldRestored.Settings) == JsonSerializer.Serialize(original), "Legacy Sonora exports must preserve every setting after rebranding");
                Require(SettingsStore.DirectoryPath == (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") is string isolated ? Path.GetFullPath(isolated) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sonora")), "Rebranding must preserve legacy data and explicit test isolation");
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
            var current = new Settings { Profiles = new() { new AudioProfile { Name = "Keep my SignalShift profile", OutputId = "new-output" } } }; var before = JsonSerializer.Serialize(current);
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
            foreach (var invalid in new[] { "{broken", "{}", "null", "{\"Format\":\"SignalShift settings\",\"FormatVersion\":999}", "{\"SchemaVersion\":1}", JsonSerializer.Serialize(duplicate) })
            {
                var rejected = false; try { SettingsTransfer.Parse(invalid, current, Array.Empty<AudioDevice>()); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "An invalid settings file was accepted: " + invalid);
            }
            Require(JsonSerializer.Serialize(current) == before, "A failed import changed current settings");
        });
        Check("Applying an import makes a restorable text backup before replacing the settings", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SignalShiftBackup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
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
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { success = !failed, hardwareChecksExcluded = args.Contains("--headless"), checks = report }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failed ? 1 : 0;
    }
}
