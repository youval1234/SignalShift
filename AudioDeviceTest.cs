using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SonarShift;

// Only used by the settings UI on explicit request. Nothing is allocated or
// captured by the tray host, and no test changes Windows defaults or volume.
internal static class AudioDeviceTest
{
    internal static readonly WaveFormat Format = new(48000, 16, 1);
    internal const int RecordingSeconds = 5;
    internal const int MaximumBytes = 48000 * 2 * RecordingSeconds;

    internal sealed class Clip : IDisposable
    {
        private readonly byte[] bytes = new byte[MaximumBytes];
        private readonly object gate = new();
        private bool disposed;
        public int Length { get; private set; }
        private int peak;
        public int Peak => Volatile.Read(ref peak);
        public void Append(byte[] source, int count)
        {
            lock (gate)
            {
                if (disposed) return;
                var size = Math.Min(count, bytes.Length - Length) & ~1;
                if (size <= 0) return;
                Buffer.BlockCopy(source, 0, bytes, Length, size); Length += size;
                var maximum = 0;
                for (var i = 0; i < size; i += 2)
                    maximum = Math.Max(maximum, Math.Abs((int)(short)(source[i] | source[i + 1] << 8)));
                Volatile.Write(ref peak, Math.Min(100, maximum * 100 / 32768));
            }
        }
        public WaveStream Open()
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return new RawSourceWaveStream(new MemoryStream(bytes, 0, Length, false), Format);
            }
        }
        public void Dispose() { lock (gate) { disposed = true; Array.Clear(bytes); Length = 0; Volatile.Write(ref peak, 0); } }
    }

    internal static async Task RecordAsync(string id, Clip clip, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using var device = enumerator.GetDevice(id);
        if (device.DataFlow != DataFlow.Capture || device.State != DeviceState.Active)
            throw new InvalidOperationException("This microphone is no longer connected.");
        using var capture = new WasapiCapture(device) { WaveFormat = Format };
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, e) => clip.Append(e.Buffer, e.BytesRecorded);
        capture.RecordingStopped += (_, e) => stopped.TrySetResult(e.Exception);
        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        capture.StartRecording();
        try
        {
            await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(RecordingSeconds), duration.Token));
            capture.StopRecording();
            var error = await stopped.Task;
            cancellation.ThrowIfCancellationRequested();
            if (error != null) throw new InvalidOperationException("Microphone recording failed: " + error.Message, error);
            if (clip.Length == 0) throw new InvalidOperationException("No audio arrived. Check microphone access and reconnect the device.");
        }
        finally { duration.Cancel(); capture.StopRecording(); }
    }

    internal static async Task PlayAsync(string id, WaveStream source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using var device = enumerator.GetDevice(id);
        if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
            throw new InvalidOperationException("This output is no longer connected.");
        using var player = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += (_, e) => stopped.TrySetResult(e.Exception);
        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        player.Init(source); player.Play();
        try
        {
            var timeout = Task.Delay(source.TotalTime + TimeSpan.FromSeconds(3), duration.Token);
            if (await Task.WhenAny(stopped.Task, timeout) != stopped.Task)
            {
                cancellation.ThrowIfCancellationRequested();
                throw new TimeoutException("The device did not finish playback. Reconnect it and try again.");
            }
            var error = await stopped.Task;
            cancellation.ThrowIfCancellationRequested();
            if (error != null) throw new InvalidOperationException("Playback failed: " + error.Message, error);
        }
        finally { duration.Cancel(); player.Stop(); }
    }

    internal static WaveStream Tone()
    {
        // A quiet two-note chime, with fades and no changes to system volume.
        var samples = 48000;
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var t = i / 48000.0;
            var local = t % 0.5;
            var envelope = Math.Min(1, local / 0.02) * Math.Max(0, Math.Min(1, (0.46 - local) / 0.1));
            var sample = (short)(Math.Sin(2 * Math.PI * (t < 0.5 ? 523.25 : 659.25) * t) * envelope * 0.12 * short.MaxValue);
            bytes[i * 2] = (byte)sample; bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new RawSourceWaveStream(new MemoryStream(bytes, false), Format);
    }
}
