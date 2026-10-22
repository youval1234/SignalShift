using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace Sonora;

// A short delay lets Windows finish routing audio before opening the confirmation stream.
// All calls are made on the application's dispatcher; playback itself is asynchronous.
public sealed class SoundCueService : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Dictionary<AudioFlow, (SoundPlayer Player, Stream Stream)> players = new();
    private AudioFlow pending;
    private bool disposed;
    public event Action<Exception>? PlaybackFailed;
    public SoundCueService()
    {
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { GetPlayer(pending).Play(); }
            catch (Exception ex) { PlaybackFailed?.Invoke(ex); }
        };
    }
    public void Preload() { GetPlayer(AudioFlow.Output); GetPlayer(AudioFlow.Input); }
    private SoundPlayer GetPlayer(AudioFlow flow)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (players.TryGetValue(flow, out var loaded)) return loaded.Player;
        var filename = flow == AudioFlow.Output ? "SwitchOutput.wav" : "SwitchInput.wav";
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + filename)) ?? throw new FileNotFoundException("Confirmation sound is missing.", filename);
        var stream = resource.Stream; var player = new SoundPlayer(stream);
        try { player.Load(); players.Add(flow, (player, stream)); return player; }
        catch { player.Dispose(); stream.Dispose(); throw; }
    }
    public void Play(AudioFlow flow)
    {
        if (disposed) return;
        timer.Stop(); pending = flow; timer.Start();
    }
    public void Cancel()
    {
        timer.Stop();
        foreach (var item in players.Values) item.Player.Stop();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop();
        foreach (var item in players.Values) { item.Player.Stop(); item.Player.Dispose(); item.Stream.Dispose(); }
        players.Clear();
    }
}
