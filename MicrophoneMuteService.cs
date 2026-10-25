using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace SonarShift;

internal enum MicrophoneMuteState { Unavailable, Live, Muted, Mixed }
internal sealed record MicrophoneMuteSnapshot(string Id, bool Muted);

// Uses endpoint controls only: never captures audio or changes speaker volume.
// Created and used on the host/UI dispatcher. Native callbacks only queue work.
public sealed class MicrophoneMuteService : IDisposable
{
    private readonly AudioService audio = new();
    private readonly Dictionary<string, IEndpointMuteVolume> endpoints = new(StringComparer.Ordinal);
    private readonly VolumeCallback callback;
    private IntPtr callbackPointer;
    private bool disposed;
    internal MicrophoneMuteState State { get; private set; }
    internal string? Error { get; private set; }
    internal int EndpointCount => endpoints.Count;
    internal MicrophoneMuteService(Action changed) => callback = new(changed);

    internal void Refresh()
    {
        if (disposed) return;
        try
        {
            var ids = new HashSet<string>();
            var roles = audio.GetRoleDefaults(AudioFlow.Input);
            if (roles.Console != null) ids.Add(roles.Console);
            if (roles.Multimedia != null) ids.Add(roles.Multimedia);
            if (roles.Communications != null) ids.Add(roles.Communications);
            foreach (var id in endpoints.Keys.Where(id => !ids.Contains(id)).ToArray()) Remove(id);
            foreach (var id in ids)
            {
                if (endpoints.ContainsKey(id)) continue;
                var volume = Open(id);
                try
                {
                    if (callbackPointer == IntPtr.Zero) callbackPointer = Marshal.GetComInterfaceForObject(callback, typeof(IEndpointMuteCallback));
                    Marshal.ThrowExceptionForHR(volume.RegisterControlChangeNotify(callbackPointer));
                    endpoints.Add(id, volume);
                }
                catch { Marshal.ReleaseComObject(volume); throw; }
            }
            State = Classify(Snapshot()); Error = null;
        }
        catch (Exception ex) { State = MicrophoneMuteState.Unavailable; Error = ex.Message; }
    }
    internal static MicrophoneMuteState Classify(IReadOnlyList<MicrophoneMuteSnapshot> values) => values.Count == 0
        ? MicrophoneMuteState.Unavailable : values.All(v => v.Muted) ? MicrophoneMuteState.Muted
        : values.All(v => !v.Muted) ? MicrophoneMuteState.Live : MicrophoneMuteState.Mixed;
    internal bool RefreshState()
    {
        if (disposed) return false;
        try { State = Classify(Snapshot()); Error = null; return true; }
        catch (Exception ex) { State = MicrophoneMuteState.Unavailable; Error = ex.Message; return false; }
    }
    internal IReadOnlyList<MicrophoneMuteSnapshot> Snapshot()
    {
        var result = new List<MicrophoneMuteSnapshot>(endpoints.Count);
        foreach (var entry in endpoints)
        {
            Marshal.ThrowExceptionForHR(entry.Value.GetMute(out var muted));
            result.Add(new(entry.Key, muted));
        }
        return result;
    }
    internal MicrophoneMuteState Toggle()
    {
        Refresh();
        if (State == MicrophoneMuteState.Unavailable) throw new InvalidOperationException(Error ?? "No default microphone is connected.");
        // A mixed setup is muted first, rather than accidentally opening a mic.
        var target = State != MicrophoneMuteState.Muted;
        var previous = Snapshot();
        try
        {
            foreach (var endpoint in endpoints.Values) Set(endpoint, target);
            if (Snapshot().Any(value => value.Muted != target)) throw new InvalidOperationException("Windows did not apply microphone mute to every default input.");
            State = target ? MicrophoneMuteState.Muted : MicrophoneMuteState.Live;
            return State;
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            foreach (var value in previous)
                try { Set(endpoints[value.Id], value.Muted); } catch (Exception ex) { errors.Add(ex); }
            Refresh();
            if (errors.Count > 1) throw new AggregateException("Microphone mute failed; some previous states could not be restored.", errors);
            throw;
        }
    }
    internal static (bool Muted, float Volume) ReadEndpoint(string id)
    {
        var volume = Open(id);
        try
        {
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var scalar));
            return (muted, scalar);
        }
        finally { Marshal.ReleaseComObject(volume); }
    }
    // Also used to restore exact snapshots after opt-in hardware validation.
    internal static void RestoreEndpoint(string id, bool muted)
    {
        var volume = Open(id);
        try { Set(volume, muted); }
        finally { Marshal.ReleaseComObject(volume); }
    }
    private static void Set(IEndpointMuteVolume volume, bool muted)
    {
        var context = new Guid("FA6926A5-B3CD-4419-B1D8-BB1AB46A05AA");
        Marshal.ThrowExceptionForHR(volume.SetMute(muted, ref context));
    }
    private static IEndpointMuteVolume Open(string id)
    {
        var enumerator = AudioService.CreateEnumerator(); IMMDevice? device = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(id, out device));
            var iid = typeof(IEndpointMuteVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out var instance));
            return (IEndpointMuteVolume)instance;
        }
        finally { if (device != null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator); }
    }
    private void Remove(string id)
    {
        var volume = endpoints[id]; endpoints.Remove(id);
        try { if (callbackPointer != IntPtr.Zero) volume.UnregisterControlChangeNotify(callbackPointer); }
        finally { Marshal.ReleaseComObject(volume); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; callback.Stop();
        foreach (var id in endpoints.Keys.ToArray()) Remove(id);
        if (callbackPointer != IntPtr.Zero) { Marshal.Release(callbackPointer); callbackPointer = IntPtr.Zero; }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class VolumeCallback(Action changed) : IEndpointMuteCallback
    {
        private Action? changed = changed;
        public int OnNotify(IntPtr data) { try { Volatile.Read(ref changed)?.Invoke(); } catch { } return 0; }
        public void Stop() => Interlocked.Exchange(ref changed, null);
    }
}

[ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEndpointMuteCallback { [PreserveSig] int OnNotify(IntPtr data); }

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEndpointMuteVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float value, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float value);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}
