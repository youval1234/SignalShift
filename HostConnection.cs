using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SonarShift;

internal static class HostConnection
{
    private static string Suffix => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SettingsStore.DirectoryPath)))[..16];
    internal static string InstanceMutex => Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") == null ? "Local\\Sonora-Desktop-v1" : "Local\\SonarShift-test-" + Suffix;
    internal static string UiMutex => "Local\\SonarShift-interface-" + Suffix;
    private static string PipeName => "SonarShift-" + Suffix;
    internal static void Notify(string title, string message, AudioFlow? flow) => Send(JsonSerializer.Serialize(new { action = "notification", title = title[..Math.Min(120, title.Length)], message = message[..Math.Min(2048, message.Length)], flow }));
    internal static string Send(string command)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(3000);
        pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        writer.WriteLineAsync(command.AsMemory(), timeout.Token).GetAwaiter().GetResult();
        return reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() ?? throw new IOException("Background process disconnected.");
    }
    internal static async Task ListenAsync(Func<string, Task<string>> dispatch, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 1024, 4096);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                requestTimeout.CancelAfter(3000);
                // Bounded local protocol: commands/display text only, never paths or arbitrary code.
                var data = new byte[16384]; var count = 0;
                while (count < data.Length)
                {
                    var read = await pipe.ReadAsync(data.AsMemory(count, 1), requestTimeout.Token).ConfigureAwait(false);
                    if (read == 0 || data[count] == 10) break;
                    count++;
                }
                var command = Encoding.UTF8.GetString(data, 0, count).TrimEnd('\r');
                var response = count == data.Length ? "{}" : await dispatch(command).WaitAsync(requestTimeout.Token).ConfigureAwait(false);
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"), requestTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        }
    }
    internal static void TrySend(string command) { try { Send(command); } catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { } }
}
