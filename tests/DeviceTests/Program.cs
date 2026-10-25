using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using SonarShift;

// Opt-in hardware playback only; this harness never records a microphone.
if (!args.Contains("--play-output")) throw new ArgumentException("Pass --play-output to play test sounds on the default output.");
string[] Defaults()
{
    using var enumerator = new MMDeviceEnumerator();
    return (from flow in new[] { DataFlow.Render, DataFlow.Capture }
            from role in new[] { Role.Console, Role.Multimedia, Role.Communications }
            select Get(flow, role)).ToArray();
    string Get(DataFlow flow, Role role)
    {
        try { using var device = enumerator.GetDefaultAudioEndpoint(flow, role); return device.ID; }
        catch { return "unavailable"; }
    }
}
var before = Defaults();
if (before[1] == "unavailable") throw new InvalidOperationException("No default output.");
for (var i = 0; i < 3; i++)
{
    using var tone = AudioDeviceTest.Tone();
    await AudioDeviceTest.PlayAsync(before[1], tone, CancellationToken.None);
}
using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
using (var tone = AudioDeviceTest.Tone())
{
    bool cancelled = false;
    try { await AudioDeviceTest.PlayAsync(before[1], tone, stop.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    if (!cancelled) throw new InvalidOperationException("Active playback did not cancel.");
}
bool playbackRejected = false, captureRejected = false;
using (var tone = AudioDeviceTest.Tone())
{
    try { await AudioDeviceTest.PlayAsync("invalid-endpoint", tone, CancellationToken.None); }
    catch { playbackRejected = true; }
}
using (var clip = new AudioDeviceTest.Clip())
{
    try { await AudioDeviceTest.RecordAsync("invalid-endpoint", clip, CancellationToken.None); }
    catch { captureRejected = true; }
}
if (!playbackRejected || !captureRejected) throw new InvalidOperationException("An invalid endpoint was accepted.");
if (!Defaults().SequenceEqual(before)) throw new InvalidOperationException("A device test changed Windows defaults.");
Console.WriteLine("Passed: 3 output playbacks, active cancellation, invalid endpoints rejected, all 6 Windows defaults preserved. No microphone capture performed.");
