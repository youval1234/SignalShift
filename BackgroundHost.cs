using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace SonarShift;

// Owns global shortcuts and audio policy without constructing a WPF Application/window.
internal sealed class BackgroundHost : Forms.ApplicationContext
{
    private readonly Forms.Control dispatch = new();
    private readonly KeyWindow keys = new();
    private readonly Forms.NotifyIcon tray;
    private readonly AudioService audio = new();
    private readonly MicrophoneMuteService microphoneMute;
    private Forms.ToolStripMenuItem? microphoneMuteItem;
    private readonly SettingsStore store = new();
    private Settings settings;
    private IReadOnlyList<AudioDevice> outputs = Array.Empty<AudioDevice>(), inputs = Array.Empty<AudioDevice>();
    private readonly CancellationTokenSource stop = new();
    private readonly Forms.Timer refresh = new() { Interval = 100 };
    private readonly Forms.Timer retry = new() { Interval = 5000 };
    private readonly Forms.Timer fallback = new() { Interval = 30000 };
    private readonly Forms.Timer settingsReload = new() { Interval = 200 };
    private int settingsReloadAttempts;
    private readonly FileSystemWatcher files;
    private AudioDeviceNotifications? notifications;
    private Process? ui;
    private readonly BackgroundFeedback feedback = new();
    private readonly List<string> conflicts = new();
    private string menuIdentity = "";
    private readonly Dictionary<(AudioFlow Flow, string Id), Forms.ToolStripMenuItem> trayDevices = new();
    private string? activeProfileId;
    private bool closing;
    internal int RefreshCount { get; private set; }

    internal BackgroundHost(bool startInTray)
    {
        microphoneMute = new(() => Post(QueueRefresh));
        _ = dispatch.Handle;
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        settings = store.Load();
        feedback.SetTheme(settings.Theme);
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") == null) StartupRegistration.InitializeAsync().GetAwaiter().GetResult();
        using var icon = typeof(BackgroundHost).Assembly.GetManifestResourceStream("SonarShift.TrayIcon")!;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(icon), Text = "SignalShift", Visible = true };
        tray.DoubleClick += (_, _) => ShowInterface();
        refresh.Tick += (_, _) => { refresh.Stop(); Refresh(); };
        retry.Tick += (_, _) => RegisterKeys();
        fallback.Tick += (_, _) => Refresh();
        settingsReload.Tick += (_, _) => ReloadChangedSettings();
        files = new FileSystemWatcher(SettingsStore.DirectoryPath) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        files.Changed += (_, e) => FileChanged(e.Name); files.Created += (_, e) => FileChanged(e.Name); files.Renamed += (_, e) => FileChanged(e.Name);
        files.EnableRaisingEvents = true;
        Refresh();
        if (!settings.Initialized)
        {
            foreach (var device in outputs.Where(d => d.IsDefault)) settings.Outputs.Add(device.Id);
            foreach (var device in inputs.Where(d => d.IsDefault)) settings.Inputs.Add(device.Id);
            settings.Initialized = true; store.Save(settings);
        }
        RegisterKeys();
        try { notifications = new AudioDeviceNotifications(() => Post(QueueRefresh)); }
        catch { fallback.Start(); }
        _ = HostConnection.ListenAsync(command =>
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() => { try { completion.TrySetResult(Command(command)); } catch (Exception ex) { completion.TrySetResult(JsonSerializer.Serialize(new { error = ex.Message })); } });
            return completion.Task;
        }, stop.Token);
        if (!startInTray && !settings.StartMinimized) ShowInterface();
    }
    private void Post(Action action)
    {
        if (closing || dispatch.IsDisposed) return;
        try { dispatch.BeginInvoke(action); } catch (InvalidOperationException) { }
    }
    private void QueueRefresh() { if (!refresh.Enabled) refresh.Start(); }
    private void QueueSettingsReload()
    {
        settingsReloadAttempts = 0;
        settingsReload.Stop(); settingsReload.Start();
    }
    private void ReloadChangedSettings()
    {
        settingsReload.Stop();
        try
        {
            var next = store.Load();
            settings = next; feedback.SetTheme(settings.Theme);
            if (!settings.Notifications) feedback.Dismiss();
            if (!settings.HotkeySounds) feedback.CancelSound();
            RegisterKeys(); QueueRefresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retain the last working preferences while another process saves.
            if (++settingsReloadAttempts < 10) settingsReload.Start();
            else feedback.Present("Settings update delayed", "Another program is using the settings file. Close it and save again.", null);
        }
    }
    private void FileChanged(string? name)
    {
        if (name == "settings.json") Post(QueueSettingsReload);
        else if (name == "show.signal") Post(() => { try { File.Delete(Path.Combine(SettingsStore.DirectoryPath, "show.signal")); } catch (IOException) { } ShowInterface(); });
    }
    private string Command(string command)
    {
        if (command.StartsWith('{'))
        {
            using var request = JsonDocument.Parse(command);
            var root = request.RootElement;
            if (root.GetProperty("action").GetString() == "notification" && settings.Notifications)
            {
                var title = root.GetProperty("title").GetString() ?? "SignalShift";
                var message = root.GetProperty("message").GetString() ?? "";
                var value = root.GetProperty("flow");
                feedback.Present(title[..Math.Min(120, title.Length)], message[..Math.Min(2048, message.Length)], value.ValueKind == JsonValueKind.Null ? null : (AudioFlow)value.GetInt32());
            }
        }
        switch (command)
        {
            case "mic-state":
                microphoneMute.Refresh();
                return JsonSerializer.Serialize(new { microphoneMute = microphoneMute.State.ToString(), endpoints = microphoneMute.Snapshot(), error = microphoneMute.Error });
            case "mic-toggle":
                var result = ToggleMicrophoneMute();
                return JsonSerializer.Serialize(new { success = result, microphoneMute = microphoneMute.State.ToString(), error = result ? null : microphoneMute.Error ?? "Could not change microphone mute." });
            case "show": ShowInterface(); break;
            case "quit": _ = Task.Delay(100).ContinueWith(_ => Post(ExitThread)); break;
            case "reload": settings = store.Load(); feedback.SetTheme(settings.Theme); RegisterKeys(); Refresh(); break;
            case "profile-next": CycleProfiles(); break;
            case "resume": RegisterKeys(); break;
            case "close-interface": if (ui != null && !ui.HasExited) { ui.Refresh(); Native.PostMessage(ui.MainWindowHandle, 0x8003, IntPtr.Zero, IntPtr.Zero); } break;
            case "test-switch-stress-long": if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) _ = Task.Delay(100).ContinueWith(_ => Post(() => File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "stress-report.json"), SwitchAllocationProbe(10000)))); break;
            case "test-switch-stress": if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) _ = Task.Delay(100).ContinueWith(_ => Post(() => File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "stress-report.json"), SwitchAllocationProbe()))); break;
            case "test-notification": if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) feedback.Present("Output switched", "Diagnostic speaker", AudioFlow.Output); break;
        }
        return JsonSerializer.Serialize(new { pid = Environment.ProcessId, interfaceRunning = ui != null && !ui.HasExited, interfacePid = ui != null && !ui.HasExited ? ui.Id : 0, conflicts, refreshes = RefreshCount, testHotkeyWindow = Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null ? keys.Handle.ToInt64() : 0, theme = settings.Theme, wpfLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "PresentationFramework"), audioTestsLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("NAudio", StringComparison.Ordinal) == true), activeProfileId, profileCycleEnabled = settings.ProfileCycleHotkeyEnabled, microphoneMute = microphoneMute.State.ToString(), microphoneMuteHotkeyEnabled = settings.MicrophoneMuteHotkeyEnabled, eventDriven = notifications != null });
    }
    // Synthetic allocation probe: never changes Windows defaults or reads user settings.
    // Available only to an isolated test instance, not the normal desktop host.
    private string SwitchAllocationProbe(int measuredSwitches = 2000)
    {
        var priorOutputs = outputs; var priorInputs = inputs;
        var first = new[] { new AudioDevice("probe-a", "Probe speaker A", AudioFlow.Output, true, false), new AudioDevice("probe-b", "Probe speaker B", AudioFlow.Output, false, false) };
        var second = new[] { first[0] with { IsDefault = false }, first[1] with { IsDefault = true } };
        var checkpoints = new List<object>();
        object Snapshot(int switches) { using var process = Process.GetCurrentProcess(); return new { switches, allocatedBytes = GC.GetTotalAllocatedBytes(false), managedHeapBytes = GC.GetTotalMemory(false), workingSetBytes = process.WorkingSet64, privateCommittedBytes = process.PrivateMemorySize64, handles = process.HandleCount, gdiObjects = Native.GetGuiResources(process.Handle, 0), userObjects = Native.GetGuiResources(process.Handle, 1), gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2), notificationHandle = feedback.NotificationHandle.ToInt64(), activationCount = feedback.ActivationCount, trayChecksCorrect = outputs.All(d => trayDevices.TryGetValue((d.Flow, d.Id), out var item) && item.Checked == d.IsDefault) }; }
        try
        {
            inputs = Array.Empty<AudioDevice>();
            for (var index = 0; index < measuredSwitches + 200; index++)
            {
                outputs = index % 2 == 0 ? first : second; UpdateTray();
                feedback.Present("Output switched", outputs[index % 2].Name, AudioFlow.Output);
                Forms.Application.DoEvents(); Thread.Sleep(5);
                if (index == 199 || (index >= 200 && (index + 1) % 200 == 0)) checkpoints.Add(Snapshot(index + 1 - 200));
            }
            feedback.Dismiss();
            return JsonSerializer.Serialize(new { scenario = $"200 warmup plus {measuredSwitches} synthetic default/notification changes; no audio default writes, no sound playback, no forced GC", checkpoints });
        }
        finally { outputs = priorOutputs; inputs = priorInputs; UpdateTray(); }
    }
    private void ShowInterface()
    {
        if (ui != null && !ui.HasExited)
        {
            // Supported local window message; does not poll a file to find a second launch.
            ui.Refresh(); Native.PostMessage(ui.MainWindowHandle, 0x8001, IntPtr.Zero, IntPtr.Zero); return;
        }
        keys.Clear(); retry.Stop();
        var launch = ProcessLauncher.ForInterface();
        ui?.Dispose();
        try
        {
            ui = Process.Start(launch) ?? throw new IOException("Could not open settings.");
            ui.EnableRaisingEvents = true;
            ui.Exited += (_, _) => Post(QueueSettingsReload);
        }
        catch { ui = null; RegisterKeys(); throw; }
    }
    private void Refresh()
    {
        try
        {
            RefreshCount++;
            microphoneMute.Refresh();
            outputs = audio.Enumerate(AudioFlow.Output); inputs = audio.Enumerate(AudioFlow.Input);
            UpdateTray();
        }
        catch (Exception ex) { tray.Text = ("SignalShift: " + ex.Message)[..Math.Min(63, ("SignalShift: " + ex.Message).Length)]; }
    }
    private void RegisterKeys()
    {
        keys.Clear(); conflicts.Clear(); retry.Stop();
        if (ui != null && !ui.HasExited) return;
        if (settings.HotkeysEnabled)
        {
            void Add(int id, bool enabled, Shortcut shortcut, string name, Action action) { if (enabled && !keys.Register(id, shortcut, action)) conflicts.Add(name); }
            Add(1, settings.OutputHotkeyEnabled, settings.OutputShortcut, "output", () => Cycle(AudioFlow.Output, true));
            Add(2, settings.InputHotkeyEnabled, settings.InputShortcut, "input", () => Cycle(AudioFlow.Input, true));
            Add(4, settings.ProfileCycleHotkeyEnabled, settings.ProfileCycleShortcut, "profile cycle", CycleProfiles);
            Add(5, settings.MicrophoneMuteHotkeyEnabled, settings.MicrophoneMuteShortcut, "microphone mute", () => ToggleMicrophoneMute());
            var id = 10;
            foreach (var profile in settings.Profiles) if (profile.Hotkey != null) Add(id++, true, profile.Hotkey, profile.Name, () => ActivateProfile(profile, true));
        }
        if (conflicts.Count != 0) retry.Start();
        tray.Text = conflicts.Count == 0 ? "SignalShift · Your sound, one shortcut away" : "SignalShift · Shortcut needs attention";
    }
    private void CycleProfiles()
    {
        try
        {
            Refresh();
            var next = ProfileCyclePlanner.Next(settings.Profiles, outputs, inputs,
                audio.GetDefaultId(AudioFlow.Output, 1), audio.GetDefaultId(AudioFlow.Input, 1), activeProfileId);
            if (next == null) { feedback.Present("No available profiles", "Create a profile with connected audio devices first.", null); return; }
            ActivateProfile(next, true);
        }
        catch (Exception ex) { Error(ex); }
    }
    private void UpdateTray()
    {
        UpdateMicrophoneMuteMenu();
        var identity = string.Join("|", outputs.Concat(inputs).Select(d => d.Flow + d.Id + d.Name)) + JsonSerializer.Serialize(settings.Profiles);
        if (identity == menuIdentity)
        {
            foreach (var device in outputs.Concat(inputs))
                if (trayDevices.TryGetValue((device.Flow, device.Id), out var item)) item.Checked = device.IsDefault;
            return;
        }
        menuIdentity = identity;
        trayDevices.Clear();
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Open SignalShift", null, (_, _) => ShowInterface()); menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            var group = new Forms.ToolStripMenuItem(flow == AudioFlow.Output ? "Playback" : "Recording");
            foreach (var device in Devices(flow)) { var item = new Forms.ToolStripMenuItem(device.Title) { Checked = device.IsDefault }; item.Click += (_, _) => Switch(device, false); group.DropDownItems.Add(item); trayDevices[(device.Flow, device.Id)] = item; }
            menu.Items.Add(group);
        }
        menu.Items.Add("Next output", null, (_, _) => Cycle(AudioFlow.Output, false)); menu.Items.Add("Next input", null, (_, _) => Cycle(AudioFlow.Input, false));
        microphoneMuteItem = new Forms.ToolStripMenuItem("Microphone mute");
        microphoneMuteItem.Click += (_, _) => ToggleMicrophoneMute(); menu.Items.Add(microphoneMuteItem);
        menu.Opening += (_, _) => { microphoneMute.Refresh(); UpdateMicrophoneMuteMenu(); };
        UpdateMicrophoneMuteMenu();
        if (settings.Profiles.Count > 0) { var group = new Forms.ToolStripMenuItem("Profiles"); foreach (var profile in settings.Profiles) group.DropDownItems.Add(profile.Name, null, (_, _) => ActivateProfile(profile, false)); menu.Items.Add(group); }
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit SignalShift", null, (_, _) => ExitThread());
        var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
    }
    private IReadOnlyList<AudioDevice> Devices(AudioFlow flow) => flow == AudioFlow.Output ? outputs : inputs;
    private void UpdateMicrophoneMuteMenu()
    {
        if (microphoneMuteItem == null) return;
        microphoneMuteItem.Text = microphoneMute.State == MicrophoneMuteState.Muted ? "Unmute microphone" : "Mute microphone";
        microphoneMuteItem.Checked = microphoneMute.State == MicrophoneMuteState.Muted;
        microphoneMuteItem.Enabled = microphoneMute.State != MicrophoneMuteState.Unavailable;
    }
    private bool ToggleMicrophoneMute()
    {
        try
        {
            var muted = microphoneMute.Toggle() == MicrophoneMuteState.Muted;
            UpdateMicrophoneMuteMenu();
            if (settings.HotkeySounds) feedback.Play(muted ? AudioFlow.Input : AudioFlow.Output);
            if (settings.Notifications) feedback.Present("Microphone", muted ? "Microphone muted" : "Microphone live", AudioFlow.Input);
            return true;
        }
        catch (Exception ex) { feedback.Present("Could not mute microphone", ex.Message, AudioFlow.Input); return false; }
    }
    private void Cycle(AudioFlow flow, bool hotkey)
    {
        try
        {
            feedback.CancelSound(); Refresh();
            var target = CyclePlanner.Next(Devices(flow), flow == AudioFlow.Output ? settings.Outputs : settings.Inputs, audio.GetDefaultId(flow));
            if (target == null) { ShowInterface(); return; }
            Switch(target, hotkey);
        }
        catch (Exception ex) { Error(ex); }
    }
    private void Switch(AudioDevice device, bool hotkey)
    {
        try { audio.SetDefault(device, settings.CommunicationsToo); Refresh(); Confirm(device.Flow == AudioFlow.Output ? "Output switched" : "Input switched", device.Title, device.Flow, hotkey); }
        catch (Exception ex) { Error(ex); }
    }
    private void ActivateProfile(AudioProfile profile, bool hotkey)
    {
        try
        {
            feedback.CancelSound(); Refresh();
            var output = outputs.FirstOrDefault(d => d.Id == profile.OutputId); var input = inputs.FirstOrDefault(d => d.Id == profile.InputId);
            if ((profile.OutputId != null && output == null) || (profile.InputId != null && input == null)) throw new InvalidOperationException("A profile device is disconnected. No devices were changed.");
            var prior = new List<(string? Id, int Role)>();
            foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input }) for (var role = 0; role < 3; role++) prior.Add((audio.GetDefaultId(flow, role), role));
            try { if (output != null) audio.SetDefault(output, settings.CommunicationsToo); if (input != null) audio.SetDefault(input, settings.CommunicationsToo); }
            catch (Exception original)
            {
                var errors = new List<Exception> { original };
                foreach (var entry in prior) if (entry.Id != null) try { audio.SetRoles(entry.Id, new[] { entry.Role }); } catch (Exception ex) { errors.Add(ex); }
                throw new AggregateException("Profile switch failed; previous defaults were restored where possible.", errors);
            }
            activeProfileId = profile.Id; Refresh(); Confirm("Profile activated", profile.Name, output != null ? AudioFlow.Output : AudioFlow.Input, hotkey);
        }
        catch (Exception ex) { Error(ex); }
    }
    private void Confirm(string title, string message, AudioFlow flow, bool hotkey)
    {
        if (hotkey && settings.HotkeySounds) feedback.Play(flow);
        if (settings.Notifications) feedback.Present(title, message, title == "Profile activated" ? null : flow);
    }
    private void Error(Exception ex) { feedback.Present("Could not switch audio", ex.Message, AudioFlow.Output); }
    protected override void ExitThreadCore()
    {
        if (closing) return;
        closing = true; stop.Cancel(); keys.Clear(); notifications?.Dispose(); microphoneMute.Dispose(); files.Dispose(); refresh.Dispose(); retry.Dispose(); fallback.Dispose(); settingsReload.Dispose();
        feedback.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); keys.Destroy(); dispatch.Dispose();
        if (ui != null && !ui.HasExited) Native.PostMessage(ui.MainWindowHandle, 0x8002, IntPtr.Zero, IntPtr.Zero);
        ui?.Dispose(); stop.Dispose(); base.ExitThreadCore();
    }
    private sealed class KeyWindow : Forms.NativeWindow
    {
        private readonly Dictionary<int, Action> actions = new();
        public KeyWindow() => CreateHandle(new Forms.CreateParams { Parent = new IntPtr(-3) });
        public bool Register(int id, Shortcut shortcut, Action action)
        {
            if (shortcut.Key == 0 || shortcut.Key == 0x7B || shortcut.Modifiers == 0 || (shortcut.Modifiers & ~7u) != 0 || !Native.RegisterHotKey(Handle, id, shortcut.Modifiers | 0x4000, shortcut.Key)) return false;
            actions.Add(id, action); return true;
        }
        public void Clear() { foreach (var id in actions.Keys) Native.UnregisterHotKey(Handle, id); actions.Clear(); }
        public void Destroy() { Clear(); DestroyHandle(); }
        protected override void WndProc(ref Forms.Message message) { if (message.Msg == 0x312 && actions.TryGetValue(message.WParam.ToInt32(), out var action)) action(); base.WndProc(ref message); }
    }
    private static class Native
    {
        [DllImport("user32.dll")] internal static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}
