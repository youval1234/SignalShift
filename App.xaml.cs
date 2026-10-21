using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sonora;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--diagnostics")) { Diagnostics.Run(e.Args); Shutdown(); return; }
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
}
