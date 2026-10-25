using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace SonarShift;

internal static class MicrophoneCommand
{
    // Probe readiness before sending the toggle exactly once. Retrying a timed-out
    // toggle could undo the requested mute if the first command already ran.
    internal static string Run()
    {
        bool Ready()
        {
            try
            {
                using var state = JsonDocument.Parse(HostConnection.Send("status", 350));
                if (!state.RootElement.TryGetProperty("microphoneMute", out _))
                    throw new InvalidOperationException("Quit the older SignalShift instance and start the updated app before using microphone control.");
                return true;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException) { return false; }
        }
        if (!Ready())
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("The application location is unavailable.");
            using var child = Process.Start(new ProcessStartInfo(path, "--tray") { UseShellExecute = false, CreateNoWindow = true });
            var deadline = Stopwatch.StartNew();
            while (!Ready())
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(6)) throw new TimeoutException("SignalShift did not become ready. Start the app and try again.");
                Thread.Sleep(50);
            }
        }
        var response = HostConnection.Send("mic-toggle");
        using var result = JsonDocument.Parse(response);
        if (!result.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
            throw new InvalidOperationException(result.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "Microphone control failed.");
        return response;
    }
}
