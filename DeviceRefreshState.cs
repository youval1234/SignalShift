using System;
using System.Collections.Generic;
using System.Linq;

namespace SonarShift;

[Flags]
public enum AudioChangeKind { None = 0, Defaults = 1, Topology = 2, Mute = 4 }

internal static class DeviceDefaults
{
    internal static bool TryApply(IReadOnlyList<AudioDevice> devices, string? standard, string? communications, out IReadOnlyList<AudioDevice> next)
    {
        next = devices;
        // A default not in the snapshot means a device appeared or changed while
        // callbacks were queued. Never manufacture a device or retain stale names.
        var knownStandard = standard == null; var knownCommunications = communications == null;
        for (var i = 0; i < devices.Count; i++) { knownStandard |= devices[i].Id == standard; knownCommunications |= devices[i].Id == communications; }
        if (!knownStandard || !knownCommunications) return false;
        AudioDevice[]? changed = null;
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i]; var normal = device.Id == standard; var calls = device.Id == communications;
            if (device.IsDefault == normal && device.IsCommunications == calls) continue;
            changed ??= devices.ToArray(); changed[i] = device with { IsDefault = normal, IsCommunications = calls };
        }
        next = changed ?? devices; return true;
    }
}

internal sealed class TrayLayoutCache
{
    private IReadOnlyList<AudioDevice>? outputs, inputs;
    private ProfileShape[]? profiles;
    private readonly record struct ProfileShape(string Id, string Name, string? Output, string? Input);
    private static bool Same(IReadOnlyList<AudioDevice>? a, IReadOnlyList<AudioDevice> b)
    {
        if (a == null || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (a[i].Id != b[i].Id || a[i].Name != b[i].Name || a[i].Flow != b[i].Flow) return false;
        return true;
    }
    internal bool Update(IReadOnlyList<AudioDevice> nextOutputs, IReadOnlyList<AudioDevice> nextInputs, IReadOnlyList<AudioProfile> nextProfiles)
    {
        var same = Same(outputs, nextOutputs) && Same(inputs, nextInputs) && profiles?.Length == nextProfiles.Count;
        if (same) for (var i = 0; i < nextProfiles.Count; i++)
        {
            var p = nextProfiles[i];
            if (profiles![i] != new ProfileShape(p.Id, p.Name, p.OutputId, p.InputId)) { same = false; break; }
        }
        if (same) return false;
        outputs = nextOutputs.ToArray(); inputs = nextInputs.ToArray();
        profiles = new ProfileShape[nextProfiles.Count];
        for (var i = 0; i < nextProfiles.Count; i++) { var p = nextProfiles[i]; profiles[i] = new(p.Id, p.Name, p.OutputId, p.InputId); }
        return true;
    }
}
