using System.Collections.Generic;
using System.Linq;

namespace SonarShift;

public static class ProfileCyclePlanner
{
    public static AudioProfile? Next(IEnumerable<AudioProfile> profiles, IReadOnlyList<AudioDevice> outputs,
        IReadOnlyList<AudioDevice> inputs, string? currentOutput, string? currentInput, string? lastProfileId = null)
    {
        var connectedOutputs = outputs.Select(d => d.Id).ToHashSet();
        var connectedInputs = inputs.Select(d => d.Id).ToHashSet();
        var available = profiles.Where(p => (p.OutputId != null || p.InputId != null)
            && (p.OutputId == null || connectedOutputs.Contains(p.OutputId))
            && (p.InputId == null || connectedInputs.Contains(p.InputId))).ToList();
        if (available.Count == 0) return null;
        bool Matches(AudioProfile p) => (p.OutputId == null || p.OutputId == currentOutput)
            && (p.InputId == null || p.InputId == currentInput);
        var index = available.FindIndex(p => p.Id == lastProfileId && Matches(p));
        if (index < 0) index = available.FindIndex(Matches);
        return available[(index + 1) % available.Count];
    }
}
