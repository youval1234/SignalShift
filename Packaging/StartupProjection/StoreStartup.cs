using System;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Windows.ApplicationModel;
using Windows.Foundation;

namespace SonarShift.StoreIntegration;

// Only primitives and Tasks cross this boundary; embedded WinRT types remain private.
[SupportedOSPlatform("windows10.0.19041.0")]
public static class StoreStartup
{
    private static StartupTask? startupTask;
    public static async Task InitializeAsync() => startupTask = await CompleteAsync(StartupTask.GetAsync("SonarShiftStartup"));
    public static bool IsEnabled() => startupTask?.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    public static async Task SetAsync(bool enabled)
    {
        var task = startupTask ?? throw new InvalidOperationException("SonarShift's Windows startup task is not available.");
        if (!enabled) { task.Disable(); return; }
        var state = await CompleteAsync(task.RequestEnableAsync());
        if (state is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
            throw new InvalidOperationException("Windows has disabled SonarShift's startup task. Enable SonarShift in Windows Settings > Apps > Startup.");
    }
    private static Task<T> CompleteAsync<T>(IAsyncOperation<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Completed = (finished, status) =>
        {
            try
            {
                if (status == AsyncStatus.Completed) completion.TrySetResult(finished.GetResults());
                else if (status == AsyncStatus.Canceled) completion.TrySetCanceled();
                else completion.TrySetException(finished.ErrorCode ?? new InvalidOperationException("Windows startup request failed."));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { finished.Close(); }
        };
        return completion.Task;
    }
}
