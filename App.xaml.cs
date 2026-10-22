using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;

namespace Sonora;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--diagnostics")) { Diagnostics.Run(e.Args); Shutdown(); return; }
        if (e.Args.Contains("--export-notification-preview"))
        {
            var path = e.Args.SkipWhile(a => a != "--export-notification-preview").Skip(1).First();
            using var preview = new SwitchNotification();
            preview.SetContent("Output switched", "SteelSeries Sonar — Aux", AudioFlow.Output);
            var surface = (FrameworkElement)preview.Content;
            surface.Measure(new Size(preview.Width, preview.Height));
            surface.Arrange(new Rect(0, 0, preview.Width, preview.Height)); surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)preview.Width, (int)preview.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var file = File.Create(path)) encoder.Save(file);
            Shutdown(); return;
        }
        if (e.Args.Contains("--export-preview"))
        {
            var path = e.Args.SkipWhile(a => a != "--export-preview").Skip(1).First();
            var preview = new MainWindow(previewMode: true); preview.Show();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                preview.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(preview); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var file = File.Create(path)) encoder.Save(file);
                Shutdown();
            }));
            return;
        }
        instance = new Mutex(true, "Local\\Sonora-Desktop-v1", out var created);
        if (!created)
        {
            // A second launch asks the existing tray instance to show its window.
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "show.signal"), DateTime.UtcNow.ToString("O"));
            Shutdown(); return;
        }
        if (e.Args.Contains("--import-soundswitch") || e.Args.Contains("--import-settings") || e.Args.Contains("--export-settings"))
        {
            Shutdown(RunSettingsCommand(e.Args)); return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.AppendAllText(Path.Combine(SettingsStore.DirectoryPath, "errors.log"), DateTime.Now + " " + args.Exception + Environment.NewLine);
            MessageBox.Show("Sonora couldn't complete that action. Your settings are saved.\n\n" + args.Exception.Message, "Sonora", MessageBoxButton.OK, MessageBoxImage.Information);
            args.Handled = true;
        };
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            if (e.Args.Contains("--tray") || window.Preferences.StartMinimized) window.Hide();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Sonora could not start"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    private static int RunSettingsCommand(string[] args)
    {
        var reportPath = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(SettingsStore.DirectoryPath, "settings-transfer.json");
        string Argument(string flag) => args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault() is string value && !value.StartsWith("--", StringComparison.Ordinal) ? value : throw new InvalidDataException("Missing file path after " + flag);
        object report; var exitCode = 0;
        try
        {
            var store = new SettingsStore(); var settings = store.Load(); var audio = new AudioService();
            var devices = audio.Enumerate(AudioFlow.Output).Concat(audio.Enumerate(AudioFlow.Input)).ToList();
            SettingsImport? imported = null; string? backup = null;
            if (args.Contains("--import-soundswitch") || args.Contains("--import-settings"))
            {
                var path = args.Contains("--import-soundswitch") ? SettingsTransfer.FindSoundSwitchConfiguration() ?? throw new FileNotFoundException("SoundSwitch settings were not found.") : Argument("--import-settings");
                imported = SettingsTransfer.ReadFile(path, settings, devices); backup = SettingsTransfer.Apply(store, imported, devices); settings = imported.Settings;
            }
            if (args.Contains("--export-settings"))
            {
                var destination = Argument("--export-settings");
                if (Path.GetFullPath(destination).Equals(Path.GetFullPath(store.FilePath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a separate export file instead of the live settings file.");
                SettingsTransfer.ExportFile(destination, settings, devices, StartupRegistration.IsEnabled());
            }
            report = new { success = true, source = imported?.Source, backup, outputs = settings.Outputs.Count, inputs = settings.Inputs.Count,
                connectedOutputs = devices.Count(d => d.Flow == AudioFlow.Output && settings.Outputs.Contains(d.Id)), connectedInputs = devices.Count(d => d.Flow == AudioFlow.Input && settings.Inputs.Contains(d.Id)),
                outputShortcut = settings.OutputShortcut.Label, inputShortcut = settings.InputShortcut.Label, warnings = imported?.Warnings };
        }
        catch (Exception ex) { report = new { success = false, error = ex.Message }; exitCode = 1; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); return exitCode;
    }
}
