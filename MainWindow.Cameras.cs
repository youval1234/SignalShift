using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SonarShift;

public sealed partial class MainWindow
{
    private readonly CameraRuntime cameraList = new();
    private int cameraPageRevision;
    private void BuildCameras()
    {
        var revision = ++cameraPageRevision;
        Heading("A different point of view", "One camera. Every angle.", "Select SignalShift Camera once in your call or streaming app. Your shortcut then changes its physical webcam source.");
        var setup = new StackPanel();
        setup.Children.Add(Text("SIGNALSHIFT CAMERA", 11, Purple, FontWeights.SemiBold));
        setup.Children.Add(new TextBlock { Text = "For calls and streaming", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
        setup.Children.Add(new TextBlock { Text = "The optional Windows 11 camera component supplies a 720p virtual output. It captures only while an app is using it. Apps must choose SignalShift Camera rather than the physical webcam.", TextWrapping = TextWrapping.Wrap, Foreground = Muted });
#if SONARSHIFT_STORE
        setup.Children.Add(new TextBlock { Text = "Camera support is not included in this Store build. The portable camera component is under development.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 8) });
#else
        var enabled = new CheckBox { Content = "Enable SignalShift Camera", IsChecked = Preferences.CameraEnabled, IsEnabled = Preferences.CameraEnabled || (CameraRuntime.Supported && CameraRuntime.InstalledLibrary != null), Margin = new Thickness(0, 18, 0, 8) };
        enabled.Click += (_, _) => { Preferences.CameraEnabled = enabled.IsChecked == true; Save(); RegisterShortcuts(); Navigate("Cameras"); };
        setup.Children.Add(enabled);
        if (!CameraRuntime.Supported) setup.Children.Add(Text("Audio switching remains available. Cameras require 64-bit Windows 11.", 12, Muted));
        else if (CameraRuntime.InstalledLibrary == null)
        {
            setup.Children.Add(new TextBlock { Text = "Install the camera component to get started. Windows asks for administrator permission to register the camera source.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
            setup.Children.Add(Button("Install camera component", InstallCamera));
        }
#endif
        var card = Card(setup, 24); card.Margin = new Thickness(0, 0, 0, 18); page.Children.Add(card);
        var devices = new StackPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        actions.Children.Add(Button("Refresh cameras", () => Navigate("Cameras")));
        var next = Button("Next camera", NextCamera, true); next.Margin = new Thickness(10, 0, 0, 0); actions.Children.Add(next); page.Children.Add(actions);
        page.Children.Add(devices);
        devices.Children.Add(Text(CameraRuntime.InstalledLibrary == null ? "Your webcam list will appear after installing the component." : "Looking for webcams…", 13, Muted));
        if (CameraRuntime.Supported && CameraRuntime.InstalledLibrary != null) _ = LoadCameras(devices, revision);
        page.Children.Add(new TextBlock { Text = "Camera access may need approval in Windows Privacy & security → Camera. A busy, unplugged, or unavailable webcam produces black video. Compatibility testing with real calling and streaming apps is still in progress.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 24, 0, 0) });
    }
    private async System.Threading.Tasks.Task LoadCameras(StackPanel target, int revision)
    {
        try
        {
            var found = await cameraList.DevicesAsync();
            if (currentPage != "Cameras" || revision != cameraPageRevision || !IsLoaded) return;
            target.Children.Clear();
            if (found.Count == 0) { target.Children.Add(Card(Text("No webcams found. Connect one and refresh.", 13, Muted), 24)); return; }
            foreach (var device in found)
            {
                var row = new DockPanel();
                var use = Button(device.Id == Preferences.CurrentCameraId ? "Selected" : "Use camera", () => { Preferences.CurrentCameraId = device.Id; Save(); Navigate("Cameras"); });
                DockPanel.SetDock(use, Dock.Right); row.Children.Add(use);
                var check = new CheckBox { Content = device.Name, IsChecked = Preferences.Cameras.Contains(device.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
                check.Click += (_, _) => { if (check.IsChecked == true) { Preferences.Cameras.Add(device.Id); Preferences.CurrentCameraId ??= device.Id; } else Preferences.Cameras.Remove(device.Id); Save(); };
                row.Children.Add(check); var card = Card(row, 16); card.Margin = new Thickness(0, 0, 0, 8); target.Children.Add(card);
            }
        }
        catch (Exception ex) { if (currentPage == "Cameras" && revision == cameraPageRevision) { target.Children.Clear(); target.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = Muted }); } }
    }
    private bool changingCamera;
    private int pendingCameraSteps;
    private async void NextCamera()
    {
        if (!Preferences.CameraEnabled) { SetStatus("Enable SignalShift Camera first.", true); return; }
        pendingCameraSteps++;
        if (changingCamera) return;
        changingCamera = true;
        try
        {
            while (pendingCameraSteps > 0)
            {
                var devices = await cameraList.DevicesAsync();
                var steps = pendingCameraSteps; pendingCameraSteps = 0;
                CameraDevice? selected = null;
                for (var i=0; i<steps; i++)
                {
                    selected = CameraSelection.Next(devices, Preferences);
                    if (selected == null) throw new InvalidOperationException("Check at least one connected webcam on the Cameras page.");
                    Preferences.CurrentCameraId = selected.Id;
                }
                Save();
                if (selected != null) { Notify("Camera selected", selected.Name); SetStatus("Camera selected · " + selected.Name); }
                if (currentPage == "Cameras") Navigate("Cameras");
            }
        }
        catch (Exception ex) { pendingCameraSteps = 0; SetStatus(ex.Message, true); }
        finally { changingCamera = false; }
    }
    private void InstallCamera()
    {
        try
        {
            var setup = Path.Combine(AppContext.BaseDirectory, "Camera", "SignalShift.CameraSetup.exe");
            if (!File.Exists(setup)) throw new FileNotFoundException("Download and extract the camera component beside SignalShift.exe, then try again.");
            var process = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(setup)! });
            if (process != null) { process.EnableRaisingEvents = true; process.Exited += (_, _) => { process.Dispose(); Dispatcher.BeginInvoke(new Action(() => { if (currentPage == "Cameras") Navigate("Cameras"); })); }; }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }
    private void BuildCameraShortcut()
    {
        var content = new StackPanel(); content.Children.Add(Text("CYCLE WEBCAMS", 11, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "Next camera source", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        content.Children.Add(new TextBlock { Text = "Cycles through checked webcams. Your call or stream stays on SignalShift Camera.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        line.Children.Add(Card(Text(Preferences.CameraShortcut.Label, 17, Purple, FontWeights.SemiBold), 13));
        var edit = Button("Record shortcut", () => ChangeCameraShortcut()); edit.Margin = new Thickness(14, 0, 0, 0); line.Children.Add(edit); content.Children.Add(line);
        var enabled = new CheckBox { Content = "Enable this shortcut", IsChecked = Preferences.CameraHotkeyEnabled, Margin = new Thickness(0, 16, 0, 0) };
        enabled.Click += (_, _) =>
        {
            var prior = Preferences.CameraHotkeyEnabled; Preferences.CameraHotkeyEnabled = enabled.IsChecked == true;
            try { SettingsTransfer.Validate(Preferences); RegisterShortcuts(); Save(); }
            catch (Exception ex) { Preferences.CameraHotkeyEnabled = prior; enabled.IsChecked = prior; RegisterShortcuts(); SetStatus(ex.Message, true); }
        }; content.Children.Add(enabled);
        var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
    }
    private void ChangeCameraShortcut()
    {
        var shortcut = CaptureShortcut(Preferences.CameraShortcut); if (shortcut == null) return;
        if (IsDuplicateShortcut(shortcut, "camera", null)) { SetStatus("That shortcut is already assigned in SignalShift.", true); return; }
        hotkeys?.Clear();
        if (hotkeys != null && !hotkeys.Register(500, shortcut, () => { })) { RegisterShortcuts(); SetStatus("Another app uses that shortcut. Your previous shortcut is kept.", true); return; }
        hotkeys?.Clear(); Preferences.CameraShortcut = shortcut; RegisterShortcuts(); Save(); Navigate("Shortcuts");
    }
}
