using System.Collections.Generic;

namespace SonarShift;

internal static class ProfileSelection
{
    // Match actual defaults, not a sticky "last clicked" flag. Unspecified sides
    // leave that device unchanged, while an empty profile can never be active.
    internal static string? Current(IEnumerable<AudioProfile> profiles, string? output, string? input, string? preferred = null)
    {
        AudioProfile? best = null; var specificity = -1;
        foreach (var profile in profiles)
        {
            var count = (profile.OutputId != null ? 1 : 0) + (profile.InputId != null ? 1 : 0);
            if (count == 0 || (profile.OutputId != null && profile.OutputId != output) || (profile.InputId != null && profile.InputId != input)) continue;
            if (profile.Id == preferred) return profile.Id;
            if (count > specificity) { best = profile; specificity = count; }
        }
        return best?.Id;
    }
}
