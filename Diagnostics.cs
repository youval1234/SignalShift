using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Interop;

namespace Sonora;

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
            var directory = Path.Combine(Path.GetTempPath(), "SonoraTest-" + Guid.NewGuid().ToString("N"));
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
            using var first = new HwndSource(new HwndSourceParameters("SonoraDiagnostics1") { Width = 0, Height = 0, WindowStyle = 0 });
            using var second = new HwndSource(new HwndSourceParameters("SonoraDiagnostics2") { Width = 0, Height = 0, WindowStyle = 0 });
            using var a = new HotkeyService(first.Handle); using var b = new HotkeyService(second.Handle);
            var key = new Shortcut(7, 0x87, "Ctrl + Alt + Shift + F24");
            Require(a.Register(1, key, () => { }), "Could not register diagnostic shortcut"); Require(!b.Register(1, key, () => { }), "Conflicting shortcut was accepted");
            a.Clear(); Require(b.Register(1, key, () => { }), "Released shortcut unavailable");
            Require(!a.Register(2, new(3, 0x7B, "F12"), () => { }), "Reserved F12 accepted");
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
