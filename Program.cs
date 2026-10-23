using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace SonarShift;

public static class Program
{
    public static bool IsInterface { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (args.Contains("--export-background-preview"))
            {
                BackgroundFeedback.ExportPreview(args.SkipWhile(a => a != "--export-background-preview").Skip(1).First()); return;
            }
            if (args.Contains("--host-command"))
            {
                var command = args.SkipWhile(a => a != "--host-command").Skip(1).First();
                var result = HostConnection.Send(command);
                var report = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault();
                if (report != null) File.WriteAllText(report, result);
                return;
            }
            if (args.Any(a => a is "--diagnostics" or "--export-preview" or "--export-notification-preview" or "--import-soundswitch" or "--import-settings" or "--export-settings"))
            { RunInterface(args); return; }
            if (args.Contains("--ui"))
            {
                using var uiInstance = new Mutex(true, HostConnection.UiMutex, out var created);
                if (created) { IsInterface = true; RunInterface(Array.Empty<string>()); }
                return;
            }
            using var instance = new Mutex(true, HostConnection.InstanceMutex, out var first);
            if (!first)
            {
                try { HostConnection.Send("show"); }
                catch { Directory.CreateDirectory(SettingsStore.DirectoryPath); File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "show.signal"), DateTime.UtcNow.ToString("O")); }
                return;
            }
            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
            Forms.Application.EnableVisualStyles();
            using var host = new BackgroundHost(args.Contains("--tray"));
            Forms.Application.Run(host);
        }
        catch (Exception ex) { Forms.MessageBox.Show(ex.Message, "SignalShift could not start"); Environment.ExitCode = 1; }
    }
    // Keep WPF types out of the background startup/JIT path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunInterface(string[] args)
    {
        var app = new App(); app.InitializeComponent(); app.Run();
    }
}
