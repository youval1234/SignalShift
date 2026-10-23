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
    private readonly SettingsStore store = new();
    private Settings settings;
    private IReadOnlyList<AudioDevice> outputs = Array.Empty<AudioDevice>(), inputs = Array.Empty<AudioDevice>();
    private readonly CancellationTokenSource stop = new();
    private readonly Forms.Timer refresh = new() { Interval = 100 };
    private readonly Forms.Timer retry = new() { Interval = 5000 };
    private readonly Forms.Timer fallback = new() { Interval = 30000 };
    private readonly FileSystemWatcher files;
    private AudioDeviceNotifications? notifications;
    private Process? ui;
    private readonly BackgroundFeedback feedback = new();
    private readonly List<string> conflicts = new();
    private string menuIdentity = "";
    private bool closing;
    internal int RefreshCount { get; private set; }

    internal BackgroundHost(bool startInTray)
    {
        _ = dispatch.Handle;
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        settings = store.Load();
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") == null) StartupRegistration.InitializeAsync().GetAwaiter().GetResult();
        using var icon = typeof(BackgroundHost).Assembly.GetManifestResourceStream("SonarShift.TrayIcon")!;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(icon), Text = "SonarShift", Visible = true };
        tray.DoubleClick += (_, _) => ShowInterface();
        refresh.Tick += (_, _) => { refresh.Stop(); Refresh(); };
        retry.Tick += (_, _) => RegisterKeys();
        fallback.Tick += (_, _) => Refresh();
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
    private void FileChanged(string? name)
    {
        if (name == "settings.json") Post(() => { settings = store.Load(); if (!settings.Notifications) feedback.Dismiss(); if (!settings.HotkeySounds) feedback.CancelSound(); RegisterKeys(); QueueRefresh(); });
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
                var title = root.GetProperty("title").GetString() ?? "SonarShift";
                var message = root.GetProperty("message").GetString() ?? "";
                var value = root.GetProperty("flow");
                feedback.Present(title[..Math.Min(120, title.Length)], message[..Math.Min(2048, message.Length)], value.ValueKind == JsonValueKind.Null ? null : (AudioFlow)value.GetInt32());
            }
        }
        switch (command)
        {
            case "show": ShowInterface(); break;
            case "quit": _ = Task.Delay(100).ContinueWith(_ => Post(ExitThread)); break;
            case "reload": settings = store.Load(); RegisterKeys(); Refresh(); break;
            case "resume": RegisterKeys(); break;
            case "close-interface": if (ui != null && !ui.HasExited) { ui.Refresh(); Native.PostMessage(ui.MainWindowHandle, 0x8003, IntPtr.Zero, IntPtr.Zero); } break;
            case "test-notification": if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) feedback.Present("Output switched", "Diagnostic speaker", AudioFlow.Output); break;
        }
        return JsonSerializer.Serialize(new { pid = Environment.ProcessId, interfaceRunning = ui != null && !ui.HasExited, interfacePid = ui != null && !ui.HasExited ? ui.Id : 0, conflicts, refreshes = RefreshCount, eventDriven = notifications != null });
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
            ui.Exited += (_, _) => Post(() => { settings = store.Load(); RegisterKeys(); QueueRefresh(); });
        }
        catch { ui = null; RegisterKeys(); throw; }
    }
    private void Refresh()
    {
        try
        {
            RefreshCount++;
            outputs = audio.Enumerate(AudioFlow.Output); inputs = audio.Enumerate(AudioFlow.Input);
            UpdateTray();
        }
        catch (Exception ex) { tray.Text = ("SonarShift: " + ex.Message)[..Math.Min(63, ("SonarShift: " + ex.Message).Length)]; }
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
            var id = 10;
            foreach (var profile in settings.Profiles) if (profile.Hotkey != null) Add(id++, true, profile.Hotkey, profile.Name, () => ActivateProfile(profile, true));
        }
        if (conflicts.Count != 0) retry.Start();
        tray.Text = conflicts.Count == 0 ? "SonarShift · Your sound, one shortcut away" : "SonarShift · Shortcut needs attention";
    }
    private void UpdateTray()
    {
        var identity = string.Join("|", outputs.Concat(inputs).Select(d => d.Id + d.Name + d.IsDefault)) + JsonSerializer.Serialize(settings.Profiles);
        if (identity == menuIdentity) return;
        menuIdentity = identity;
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Open SonarShift", null, (_, _) => ShowInterface()); menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            var group = new Forms.ToolStripMenuItem(flow == AudioFlow.Output ? "Playback" : "Recording");
            foreach (var device in Devices(flow)) { var item = new Forms.ToolStripMenuItem(device.Title) { Checked = device.IsDefault }; item.Click += (_, _) => Switch(device, false); group.DropDownItems.Add(item); }
            menu.Items.Add(group);
        }
        menu.Items.Add("Next output", null, (_, _) => Cycle(AudioFlow.Output, false)); menu.Items.Add("Next input", null, (_, _) => Cycle(AudioFlow.Input, false));
        if (settings.Profiles.Count > 0) { var group = new Forms.ToolStripMenuItem("Profiles"); foreach (var profile in settings.Profiles) group.DropDownItems.Add(profile.Name, null, (_, _) => ActivateProfile(profile, false)); menu.Items.Add(group); }
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit SonarShift", null, (_, _) => ExitThread());
        var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
    }
    private IReadOnlyList<AudioDevice> Devices(AudioFlow flow) => flow == AudioFlow.Output ? outputs : inputs;
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
            Refresh(); Confirm("Profile activated", profile.Name, output != null ? AudioFlow.Output : AudioFlow.Input, hotkey);
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
        closing = true; stop.Cancel(); keys.Clear(); notifications?.Dispose(); files.Dispose(); refresh.Dispose(); retry.Dispose(); fallback.Dispose();
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
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}
