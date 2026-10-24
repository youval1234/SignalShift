# SignalShift Camera — development preview

Optional Windows 11 x64 virtual-camera component for calling and streaming apps.
Select **SignalShift Camera** once in each app, then cycle the checked physical
webcams using SignalShift's configurable shortcut. Current output: NV12 1280×720,
nominal 30 fps. No microphone processing or video recording is included.

## Status

Development preview: compilation and automated non-hardware checks do not establish
compatibility with Teams, Zoom, Discord, Brave/WebRTC, or OBS. Hardware/app validation
is required before this component is advertised as production-ready. Windows 10
retains all audio and appearance features, without the virtual camera component.

## Optional portable installation

Extract the component into `Camera` beside `SignalShift.exe`. Choose **Cameras →
Install camera component**. The setup executable asks for normal Windows administrator
permission and registers the COM source under Program Files. It never changes Smart
App Control, Defender, passkey access, certificate trust, or camera privacy permissions.
Unsigned previews can be blocked by Windows; a trusted signed release is still needed
on machines enforcing application control. Open Cameras again, enable the virtual
camera, check physical webcams, and enable/record its shortcut under Shortcuts.

Quit SignalShift and close programs using the virtual camera before upgrading. To remove it, quit
SignalShift, run `SignalShift.CameraSetup.exe --uninstall` with administrator permission,
then delete the extracted component files. Settings and audio-device choices remain.

## Resource and privacy boundaries

The audio-only tray does not load the native DLL, enumerate cameras, or capture video.
The virtual device has session lifetime and is removed when the tray quits. Its native
Media Foundation source starts physical capture only when a consumer starts streaming,
and shuts it down when streaming stops. Its sample allocator is bounded to four samples;
there is no recorded video, frame history, or network connection. Physical reads are
asynchronous, with one outstanding read and at most one completed sample. A read
pending for 1.5 seconds is closed and retried after a two-second backoff; completed
samples older than 500 ms and callbacks from a closed source are discarded. Output
is paced at nominal 30 fps, including while the selected camera is unavailable.
Media type/stride are validated once per source negotiation rather than queried on
every frame. Camera driver activation/format negotiation and shutdown are still
external driver operations and are not guaranteed to finish within the read timeout.
Switching polls a tiny
selection file at most ten times per second **only while streaming**. An unavailable
camera produces black frames rather than retaining the previous image.

The selection file is in `%PROGRAMDATA%/SignalShift-Camera/<Windows SID>/selection.bin`.
Only that user, SYSTEM and Administrators may write it; Windows LocalService can read
it because Camera Frame Server runs in that account. It stores a selected device ID,
not video or credentials. Old exported audio settings remain importable. Cross-PC
camera IDs may differ; reselect cameras on the destination PC.

Distribution is through GitHub. The Microsoft Store submission and product entry
have been removed; no Store camera package is planned. See the
[Windows trust guide](../docs/windows-trust.md) for unsigned-build limitations.

## Build

Install Visual Studio 2022 C++ tools, Windows 11 SDK and NuGet CLI, then run
`Camera/build-camera.ps1`. Microsoft sample code and MIT license are documented in
`Native/THIRD-PARTY-NOTICES.md`. Dependencies are pinned in `Native/packages.config`.

## Automated camera checks

Native tests cover black frames and buffer boundaries, oversized stride rejection,
asynchronous read backpressure, timeout, late/stale samples, error/format transitions,
10,000 state-machine cycles and callback lifetime release. A generated silent PCM
fixture exercises the actual asynchronous Media Foundation callback wiring without
opening cameras or playing audio. Frame pacing and timer cleanup are tested too.
These checks do not establish webcam negotiation or Discord/OBS compatibility.
Control-request checks also verify invalid pointers/buffer sizes are rejected and
requests after shutdown return an error without accessing released streams.
