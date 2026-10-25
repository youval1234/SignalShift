# SignalShift 2.2 preview

## Appearance by button

Click the sidebar's **Light mode / Dark mode** button from any page. The theme
changes immediately and is remembered. Settings retains explicit Dark and Light
buttons. The appearance hotkey and its shortcut editor have been removed from
both the settings UI and background host. Old exports still import, ignoring the
retired theme-shortcut fields. Audio and whole-profile hotkeys remain available.

## Hear your devices

On Devices, **Test** is available next to each playback and recording endpoint.

- Output: click **Play test sound** for a gentle one-second chime on that device.
- Microphone: select the output for playback and click **Record & hear (5 s)**.
  Speak while the level meter moves. The clip plays after recording, with a
  **Play again** button for replay. **Stop** cancels the current test.

Tests open the exact endpoint in shared mode without switching defaults or changing
system volume. Check volume, mute, connections and Windows microphone permissions
if nothing is audible. A disconnected, inaccessible or exclusively used endpoint
reports an error in the test window. Headphones are recommended for microphone tests.

No recording happens when opening the dialog. Microphone clips are held in memory
up to five seconds / 480,000 bytes, cleared when replaced or closed, and never saved
or uploaded. Closing an active test first cancels it and releases the endpoint.

## Performance and validation

The separate lightweight tray, event-driven device refresh, reused notifications,
bounded feedback sounds and file-backed assemblies remain. Device-test libraries
load only in the settings process when needed; the tray never captures audio.
The EXE now includes the small NAudio.Core / NAudio.Wasapi dependencies and their
MIT notices (Settings → Third-party licenses).

Automated checks cover migration, both theme-button directions and persistence,
bounded/aligned recording buffers and erasure, test-tone amplitude, cancellation,
retired-hotkey dispatch, settings lock recovery and existing audio/profile behavior.
These are not a claim of subjective microphone quality or every-driver compatibility.

This is an unsigned Windows x64 preview. See the [Windows trust guide](windows-trust.md).
