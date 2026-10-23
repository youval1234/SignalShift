using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SonarShift;

// Core Audio runs on the application's STA dispatcher. COM objects never cross threads.
public sealed class AudioService
{
    private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumerator();
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public IReadOnlyList<AudioDevice> Enumerate(AudioFlow flow)
    {
        var enumerator = CreateEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            var standard = DefaultId(enumerator, flow, 1);
            var communications = DefaultId(enumerator, flow, 2);
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints((int)flow, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var devices = new List<AudioDevice>();
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                IPropertyStore? properties = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out var value));
                    string name;
                    try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "Audio device" : "Audio device"; }
                    finally { PropVariantClear(ref value); }
                    devices.Add(new(id, name, flow, id == standard, id == communications));
                }
                finally { Release(properties); Release(device); }
            }
            return devices.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally { Release(collection); Release(enumerator); }
    }
    private static string? DefaultId(IMMDeviceEnumerator enumerator, AudioFlow flow, int role)
    {
        var hr = enumerator.GetDefaultAudioEndpoint((int)flow, role, out var device);
        if (hr < 0) { Release(device); return null; }
        try { Marshal.ThrowExceptionForHR(device.GetId(out var id)); return id; }
        finally { Release(device); }
    }
    public string? GetDefaultId(AudioFlow flow, int role = 1)
    {
        var enumerator = CreateEnumerator();
        try { return DefaultId(enumerator, flow, role); }
        finally { Release(enumerator); }
    }
    public void SetDefault(AudioDevice device, bool communicationsToo)
    {
        if (!Enumerate(device.Flow).Any(d => d.Id == device.Id)) throw new InvalidOperationException("This device was disconnected. Reconnect it and try again.");
        var roles = communicationsToo ? new[] { 0, 1, 2 } : new[] { 0, 1 };
        var previous = roles.Select(role => (Role: role, Id: GetDefaultId(device.Flow, role))).ToArray();
        try
        {
            SetRoles(device.Id, roles);
            foreach (var role in roles) if (GetDefaultId(device.Flow, role) != device.Id) throw new InvalidOperationException("Windows did not apply the device change.");
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            foreach (var entry in previous) if (entry.Id != null) { try { SetRoles(entry.Id, new[] { entry.Role }); } catch (Exception restoreError) { failures.Add(restoreError); } }
            if (failures.Count > 1) throw new AggregateException("Audio switching failed and Windows could not restore every previous default.", failures);
            throw;
        }
    }
    public void SetRoles(string id, int[] roles)
    {
        var policy = (IPolicyConfig)new PolicyConfig();
        try { foreach (var role in roles) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role)); }
        finally { Release(policy); }
    }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}

[StructLayout(LayoutKind.Sequential)] public struct PropertyKey { public Guid FormatId; public uint PropertyId; }
[StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection collection);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid id, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}
// Windows' private endpoint policy interface. Keep every preceding vtable slot in order.
// Contract reference: https://github.com/tartakynov/audioswitch/blob/master/IPolicyConfig.h
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] internal class PolicyConfig { }
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
    [PreserveSig] int GetDeviceFormat(IntPtr id, int @default, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(IntPtr id);
    [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpoint, IntPtr mix);
    [PreserveSig] int GetProcessingPeriod(IntPtr id, int @default, IntPtr period, IntPtr minimum);
    [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
    [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int GetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
}
