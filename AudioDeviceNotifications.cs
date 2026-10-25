using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SonarShift;

// Core Audio invokes these on its own threads. The subscriber must only enqueue work.
public sealed class AudioDeviceNotifications : IDisposable
{
    private IMMDeviceEnumerator? enumerator;
    private readonly Callback callback;
    private IntPtr callbackPointer;
    public AudioDeviceNotifications(Action changed) : this(_ => changed()) { }
    public AudioDeviceNotifications(Action<AudioChangeKind> changed)
    {
        callback = new Callback(changed);
        try
        {
            enumerator = AudioService.CreateEnumerator();
            callbackPointer = Marshal.GetComInterfaceForObject(callback, typeof(IEndpointNotifications));
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(callbackPointer));
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        callback.Stop();
        if (enumerator != null)
        {
            if (callbackPointer != IntPtr.Zero) enumerator.UnregisterEndpointNotificationCallback(callbackPointer);
            Marshal.ReleaseComObject(enumerator); enumerator = null;
        }
        if (callbackPointer != IntPtr.Zero) { Marshal.Release(callbackPointer); callbackPointer = IntPtr.Zero; }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Callback : IEndpointNotifications
    {
        private Action<AudioChangeKind>? notify;
        public Callback(Action changed) : this(_ => changed()) { }
        public Callback(Action<AudioChangeKind> changed) => notify = changed;
        public void Stop() => Interlocked.Exchange(ref notify, null);
        private int Signal(AudioChangeKind kind) { try { Volatile.Read(ref notify)?.Invoke(kind); } catch { /* Never throw across a native callback. */ } return 0; }
        public int OnDeviceStateChanged(string id, uint state) => Signal(AudioChangeKind.Topology);
        public int OnDeviceAdded(string id) => Signal(AudioChangeKind.Topology);
        public int OnDeviceRemoved(string id) => Signal(AudioChangeKind.Topology);
        public int OnDefaultDeviceChanged(int flow, int role, string? id) => Signal(flow is 0 or 1 && role is >= 0 and <= 2 ? AudioChangeKind.Defaults : AudioChangeKind.Topology);
        public int OnPropertyValueChanged(string id, PropertyKey key) => Signal(AudioChangeKind.Topology);
    }
}

[ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEndpointNotifications
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
}
