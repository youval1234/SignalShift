using System;

namespace SonarShift;

// Only an explicitly opted-in Windows sign-in launch may make the microphone live.
// Finish once, expire after 30 seconds, and never override a later user mute.
internal sealed class MicrophoneStartupPolicy(bool requested)
{
    internal bool Pending { get; private set; } = requested;
    internal void Cancel() => Pending = false;
    internal bool TryApply(MicrophoneMuteState state, TimeSpan elapsed, Action makeLive)
    {
        if (!Pending) return false;
        if (elapsed >= TimeSpan.FromSeconds(30)) { Cancel(); return false; }
        if (state == MicrophoneMuteState.Unavailable) return false;
        Cancel();
        if (state != MicrophoneMuteState.Live) makeLive();
        return true;
    }
}
