using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

internal static class Program
{
    // This probes activation/error handling outside a package. It cannot test startup consent.
    private static async Task Main(string[] args)
    {
        try
        {
#if FULL_SDK
            await Windows.ApplicationModel.StartupTask.GetAsync("SonarShiftStartup");
#else
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) throw new PlatformNotSupportedException();
            await SonarShift.StoreIntegration.StoreStartup.InitializeAsync();
#endif
            File.WriteAllText(args[0], JsonSerializer.Serialize(new { completed = true, hresult = 0 }));
        }
        catch (Exception ex) { File.WriteAllText(args[0], JsonSerializer.Serialize(new { completed = false, hresult = ex.HResult, errorType = ex.GetType().Name, message = ex.Message })); }
    }
}
