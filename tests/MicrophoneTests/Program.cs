using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using SonarShift;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: MicrophoneTests <app.exe> <isolated-report-directory>. Temporarily toggles default microphones and restores them.");
        var exe = Path.GetFullPath(args[0]); var data = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(data); Environment.SetEnvironmentVariable("SONARSHIFT_TEST_DATA", data);
        System.Windows.Forms.Application.EnableVisualStyles();
        var audio = new AudioService();
        var defaults = (from flow in new[] { AudioFlow.Output, AudioFlow.Input } from role in new[] { 0, 1, 2 } select audio.GetDefaultId(flow, role)).ToArray();
        var speakers = defaults.Take(3).Where(id => id != null).Distinct().ToDictionary(id => id!, id => MicrophoneMuteService.ReadEndpoint(id!));
        var callbacks = 0;
        using var microphones = new MicrophoneMuteService(() => Interlocked.Increment(ref callbacks));
        microphones.Refresh(); var snapshot = microphones.Snapshot();
        if (snapshot.Count == 0 || microphones.Error != null) throw new InvalidOperationException("No controllable default microphone: " + microphones.Error);
        var levels = snapshot.ToDictionary(v => v.Id, v => MicrophoneMuteService.ReadEndpoint(v.Id).Volume);
        var settings = new Settings { Initialized = true, HotkeysEnabled = true, OutputHotkeyEnabled = false, InputHotkeyEnabled = false, MicrophoneMuteHotkeyEnabled = true,
            MicrophoneMuteShortcut = new(7, 0x78, "Ctrl + Alt + Shift + F9"), Notifications = false, HotkeySounds = false };
        File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(settings));
        Process Launch(params string[] arguments)
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            return Process.Start(info)!;
        }
        JsonDocument Command(string command) => JsonDocument.Parse(HostConnection.Send(command));
        void WaitFor(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("Microphone state did not update.");
                System.Windows.Forms.Application.DoEvents(); Thread.Sleep(30);
            }
        }
        bool All(bool muted) => microphones.Snapshot().All(v => v.Muted == muted);
        void ToggleClient()
        {
            using var client = Launch("--toggle-microphone");
            if (!client.WaitForExit(10000) || client.ExitCode != 0) throw new InvalidOperationException("Microphone command failed.");
        }
        Process? host = null; var coldStart = false; var defaultSwitchChecked = false; long defaultFullScans = -1, muteFullScans = -1;
        try
        {
            host = Launch("--tray"); Thread.Sleep(750);
            using var state = Command("status");
            if (state.RootElement.GetProperty("conflicts").GetArrayLength() != 0) throw new InvalidOperationException("Test hotkey is unavailable.");
            var alternate = audio.Enumerate(AudioFlow.Output).FirstOrDefault(d => d.Id != defaults[1]);
            if (alternate != null && defaults.Take(3).All(id => id != null))
            {
                speakers.TryAdd(alternate.Id, MicrophoneMuteService.ReadEndpoint(alternate.Id));
                var scans = state.RootElement.GetProperty("fullEnumerations").GetInt64();
                try
                {
                    audio.SetDefault(alternate, true);
                    WaitFor(() => { using var current = Command("status"); return current.RootElement.GetProperty("testOutputDefault").GetString() == alternate.Id && current.RootElement.GetProperty("testOutputCalls").GetString() == alternate.Id; });
                    using var switched = Command("status");
                    if (!switched.RootElement.GetProperty("testTrayChecksCorrect").GetBoolean()) throw new InvalidOperationException("Tray default checks are stale.");
                    defaultFullScans = switched.RootElement.GetProperty("fullEnumerations").GetInt64() - scans;
                    defaultSwitchChecked = true;
                }
                finally
                {
                    for (var role = 0; role < 3; role++) audio.SetRoles(defaults[role]!, new[] { role });
                    WaitFor(() => { using var current = Command("status"); return current.RootElement.GetProperty("testOutputDefault").GetString() == defaults[1] && current.RootElement.GetProperty("testOutputCalls").GetString() == defaults[2]; });
                }
            }
            var window = new IntPtr(state.RootElement.GetProperty("testHotkeyWindow").GetInt64());
            var target = MicrophoneMuteService.Classify(snapshot) != MicrophoneMuteState.Muted;
            using var beforeMute = Command("status");
            ToggleClient(); WaitFor(() => All(target));
            WaitFor(() => { using var current = Command("status"); return current.RootElement.GetProperty("fastMuteRefreshes").GetInt32() > beforeMute.RootElement.GetProperty("fastMuteRefreshes").GetInt32(); });
            using (var afterMute = Command("status")) muteFullScans = afterMute.RootElement.GetProperty("fullEnumerations").GetInt64() - beforeMute.RootElement.GetProperty("fullEnumerations").GetInt64();
            if (!PostMessage(window, 0x312, new IntPtr(5), IntPtr.Zero)) throw new InvalidOperationException("Could not dispatch registered mute hotkey.");
            WaitFor(() => All(!target));
            var previousCallbacks = callbacks;
            var first = snapshot[0]; MicrophoneMuteService.RestoreEndpoint(first.Id, target);
            WaitFor(() => callbacks > previousCallbacks);
            using (Command("quit")) { }
            if (!host.WaitForExit(5000)) throw new InvalidOperationException("Test host did not exit.");
            microphones.Refresh(); var beforeCold = microphones.State;
            ToggleClient();
            WaitFor(() => All(beforeCold != MicrophoneMuteState.Muted));
            using var cold = Command("status");
            if (cold.RootElement.GetProperty("interfaceRunning").GetBoolean() || cold.RootElement.GetProperty("wpfLoaded").GetBoolean()
                || cold.RootElement.GetProperty("audioTestsLoaded").GetBoolean()) throw new InvalidOperationException("Cold mute command loaded the settings or audio test UI.");
            coldStart = true;
            void StopHost()
            {
                using var status = Command("status"); using var ownedHost = Process.GetProcessById(status.RootElement.GetProperty("pid").GetInt32());
                using (Command("quit")) { }
                if (!ownedHost.WaitForExit(5000)) throw new InvalidOperationException("Startup test host did not exit.");
            }
            void RequireTray(string expected)
            {
                WaitFor(() => { using var status = Command("status"); return status.RootElement.GetProperty("microphoneMute").GetString() == expected && status.RootElement.GetProperty("testTrayMicrophoneCorrect").GetBoolean(); });
            }
            StopHost();
            foreach (var endpoint in snapshot) MicrophoneMuteService.RestoreEndpoint(endpoint.Id, true);
            settings.StartMicrophoneLive = false;
            File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(settings));
            host?.Dispose(); host = Launch("--tray", "--startup");
            RequireTray("Muted"); if (!All(true)) throw new InvalidOperationException("Startup without opt-in changed mute.");
            StopHost();
            settings.StartMicrophoneLive = true;
            File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(settings));
            host.Dispose(); host = Launch("--tray");
            RequireTray("Muted"); if (!All(true)) throw new InvalidOperationException("Ordinary restart overrode mute.");
            StopHost(); host.Dispose(); host = Launch("--tray", "--startup");
            WaitFor(() => All(false)); RequireTray("Live");
            foreach (var endpoint in snapshot) MicrophoneMuteService.RestoreEndpoint(endpoint.Id, true);
            RequireTray("Muted"); Thread.Sleep(1500);
            if (!All(true)) throw new InvalidOperationException("Sign-in policy overwrote later mute.");
        }
        finally
        {
            try { using var quit = Command("quit"); } catch { }
            if (host != null && !host.HasExited) host.WaitForExit(5000);
            host?.Dispose();
            foreach (var value in snapshot) MicrophoneMuteService.RestoreEndpoint(value.Id, value.Muted);
            foreach (var value in snapshot)
            {
                var restored = MicrophoneMuteService.ReadEndpoint(value.Id);
                if (restored.Muted != value.Muted || restored.Volume != levels[value.Id]) throw new InvalidOperationException("Microphone state was not restored exactly.");
            }
            foreach (var speaker in speakers)
                if (MicrophoneMuteService.ReadEndpoint(speaker.Key) != speaker.Value) throw new InvalidOperationException("Speaker volume or mute changed.");
            var after = (from flow in new[] { AudioFlow.Output, AudioFlow.Input } from role in new[] { 0, 1, 2 } select audio.GetDefaultId(flow, role)).ToArray();
            if (!defaults.SequenceEqual(after)) throw new InvalidOperationException("A default device changed.");
        }
        var report = new { success = true, defaultSwitchChecked, defaultFullScans, muteFullScans, endpoints = snapshot.Count, callbacks, commandLineToggle = true, registeredHotkey = true, externalMuteNotification = true,
            coldStart, signInUnmuteOptIn = true, ordinaryRestartPreservesMute = true, trayFollowsExternalMute = true, laterMutePreserved = true, originalMicrophoneStatesRestored = true, speakerMuteAndVolumePreserved = true, allSixDefaultRolesPreserved = true, microphoneRecorded = false };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(data, "hardware-report.json"), json); Console.WriteLine(json); return 0;
    }
}
