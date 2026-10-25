using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SonarShift;

internal static class StartupTrace
{
    // Opt-in, isolated performance tests only; ordinary launches write nothing.
    internal static void Mark(string phase)
    {
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") is not string directory ||
            !long.TryParse(Environment.GetEnvironmentVariable("SIGNALSHIFT_STARTUP_TICKS"), out var start)) return;
        Directory.CreateDirectory(directory);
        File.AppendAllText(Path.Combine(directory, "startup-" + Environment.ProcessId + ".jsonl"), JsonSerializer.Serialize(new { phase, pid = Environment.ProcessId, elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds }) + Environment.NewLine);
    }
}
