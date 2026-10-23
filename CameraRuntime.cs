using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SonarShift;

public sealed record CameraDevice(string Id, string Name);
internal sealed class CameraRuntime : IDisposable
{
    internal const string ClassId = "{D25FA9DE-541A-4F52-A436-57A8FAEE97AE}";
    private readonly object gate = new();
    private IntPtr library, session;
    private Start? start;
    private Stop? stop;
    private Enumerate? enumerate;
    private bool disposed;
    private int revision;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Start([MarshalAs(UnmanagedType.LPWStr)] string config, out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Stop(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DeviceCallback(IntPtr id, IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate(DeviceCallback callback);
    internal static bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && Environment.Is64BitProcess;
    internal static string? InstalledLibrary
    {
        get { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + ClassId + @"\InprocServer32"); return key?.GetValue(null) as string; }
    }
    private void Load()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Supported) throw new PlatformNotSupportedException("Virtual camera switching requires Windows 11, 64-bit.");
        if (library != IntPtr.Zero) return;
        var path = InstalledLibrary;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidOperationException("Install the optional SignalShift camera component first.");
        library = NativeLibrary.Load(path);
        try {
            start = Marshal.GetDelegateForFunctionPointer<Start>(NativeLibrary.GetExport(library, "CameraStart"));
            stop = Marshal.GetDelegateForFunctionPointer<Stop>(NativeLibrary.GetExport(library, "CameraStop"));
            enumerate = Marshal.GetDelegateForFunctionPointer<Enumerate>(NativeLibrary.GetExport(library, "CameraEnumerate"));
        } catch { NativeLibrary.Free(library); library = IntPtr.Zero; throw; }
    }
    internal Task<IReadOnlyList<CameraDevice>> DevicesAsync() => Task.Run<IReadOnlyList<CameraDevice>>(() =>
    {
        lock (gate) {
            Load(); var result = new List<CameraDevice>();
            DeviceCallback callback = (id, name) => result.Add(new(Marshal.PtrToStringUni(id)!, Marshal.PtrToStringUni(name)!));
            Marshal.ThrowExceptionForHR(enumerate!(callback)); GC.KeepAlive(callback);
            return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    });
    internal Task ApplyAsync(Settings settings)
    {
        // Snapshot before background work; callers can continue editing preferences.
        var enabled = settings.CameraEnabled; var selected = settings.CurrentCameraId;
        var requested = System.Threading.Interlocked.Increment(ref revision);
        return Task.Run(() => {
            lock (gate) {
                if (disposed || requested != System.Threading.Volatile.Read(ref revision)) return;
                if (!enabled) { StopSession(); return; }
                Load();
                var config = CameraSelection.Write(selected);
                if (session == IntPtr.Zero) Marshal.ThrowExceptionForHR(start!(config, out session));
            }
        });
    }
    private void StopSession() { if (session != IntPtr.Zero) { stop!(session); session = IntPtr.Zero; } }
    public void Dispose()
    {
        lock (gate) {
            if (disposed) return;
            disposed = true; StopSession();
            if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
        }
    }
}
internal static class CameraSelection
{
    internal static string Write(string? id)
    {
        if (id?.Length > 4000 || id?.Contains('\0') == true) throw new InvalidDataException("Invalid camera identifier.");
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user identity unavailable.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SignalShift-Camera");
        Directory.CreateDirectory(root);
        if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("The camera settings folder must not be a link.");
        var directory = new DirectoryInfo(Path.Combine(root, sid.Value));
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(sid);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var owner in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (!directory.Exists) directory.Create(security);
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("The camera settings folder must not be a link.");
        directory.SetAccessControl(security);
        var path = Path.Combine(directory.FullName, "selection.bin");
        var temporary = Path.Combine(directory.FullName, Guid.NewGuid().ToString("N") + ".tmp");
        try {
            using (var writer = new BinaryWriter(File.Create(temporary), Encoding.Unicode)) {
                writer.Write((ushort)0x5353); writer.Write((ushort)1);
                writer.Write(Encoding.Unicode.GetBytes((id ?? "") + "\0"));
            }
            File.Move(temporary, path, true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
    internal static CameraDevice? Next(IReadOnlyList<CameraDevice> devices, Settings settings)
    {
        var available = devices.Where(d => settings.Cameras.Contains(d.Id)).ToArray();
        if (available.Length == 0) return null;
        return available[(Array.FindIndex(available, d => d.Id == settings.CurrentCameraId) + 1) % available.Length];
    }
}
