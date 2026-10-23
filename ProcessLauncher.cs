using System;
using System.Diagnostics;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace SonarShift;

internal static class ProcessLauncher
{
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location is read only when launched by dotnet with a separate entry DLL, never for the bundled executable.")]
    internal static ProcessStartInfo ForInterface()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        // Framework-dependent deployments can also be started with `dotnet SonarShift.dll`.
        if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        start.ArgumentList.Add("--ui");
        return start;
    }
}
