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

Close programs using the virtual camera before upgrading. To remove it, quit
SignalShift, run `SignalShift.CameraSetup.exe --uninstall` with administrator permission,
then delete the extracted component files. Settings and audio-device choices remain.

## Resource and privacy boundaries

The audio-only tray does not load the native DLL, enumerate cameras, or capture video.
The virtual device has session lifetime and is removed when the tray quits. Its native
Media Foundation source starts physical capture only when a consumer starts streaming,
and shuts it down when streaming stops. Its sample allocator is bounded to four samples;
there is no recorded video, frame history, or network connection. Switching polls a tiny
selection file at most ten times per second **only while streaming**. An unavailable
camera produces black frames rather than retaining the previous image.

The selection file is in `%PROGRAMDATA%/SignalShift-Camera/<Windows SID>/selection.bin`.
Only that user, SYSTEM and Administrators may write it; Windows LocalService can read
it because Camera Frame Server runs in that account. It stores a selected device ID,
not video or credentials. Old exported audio settings remain importable. Cross-PC
camera IDs may differ; reselect cameras on the destination PC.

The existing Store package identity and pending 1.4 certification remain untouched.
This elevated portable setup is not part of a Microsoft Store update. A Store-compatible
registration/packaging path must be validated before camera support ships there.

## Build

Install Visual Studio 2022 C++ tools, Windows 11 SDK and NuGet CLI, then run
`Camera/build-camera.ps1`. Microsoft sample code and MIT license are documented in
`Native/THIRD-PARTY-NOTICES.md`. Dependencies are pinned in `Native/packages.config`.
