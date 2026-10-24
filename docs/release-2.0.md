# SignalShift 2.0 preview

SignalShift replaces the SonarShift name and adds dark/light appearance, a configurable
appearance shortcut, and optional webcam routing while keeping the existing audio
switching, profiles, import/export and memory optimizations.

## Downloads

- **SignalShift.exe**: self-contained Windows x64 audio and appearance app.
- **SignalShift-Camera-2.0.0-preview.zip**: optional experimental Windows 11 x64 camera
  component. Extract its `Camera` folder beside the EXE; follow its README.
- **SHA256SUMS.txt**: checksum for the EXE.
- **CAMERA-SHA256SUMS.txt**: checksum for the optional camera archive; the archive
  also includes checksums for its DLL and installer.

Quit any older version before starting the new EXE. Existing preferences in
`%LOCALAPPDATA%\Sonora` are preserved. New appearance/camera shortcuts and camera
capture are disabled by default when upgrading. Camera installation requires normal
administrator approval and is separate from running the app.

## Validation and limits

Automated checks cover settings migration, imports/exports, device-cycle planning,
shortcut conflicts, live colors and all four pages in both themes, tray theme-hotkey
dispatch without WPF, repeated synthetic switch allocations and native resource
reuse, camera frame bounds, asynchronous read lifetime and pacing, IPC collision
recovery, and camera control validation/shutdown. They do not establish compatibility
with every audio driver or with live two-camera switching in calling apps/OBS.

The optional camera remains **experimental**, fixed at 1280×720 NV12 and nominal
30 fps. Real two-webcam calling apps/OBS tests are outstanding. It is not advertised as
production-ready. Existing performance numbers are synthetic/historical; no measured
camera-active RAM reduction is claimed.

This preview also prevents busy retries on an occupied IPC channel and rejects
invalid or post-shutdown camera control requests. These fixes do not constitute
a security certification.

## Windows trust and distribution

This is an **unsigned prerelease**, distributed through GitHub. The Microsoft Store
submission and product entry were removed; Store publication is no longer planned.
Windows may still block the EXE or camera installer. See the
[Smart App Control instructions and responsibility notice](windows-trust.md).
Users are responsible for any security-setting changes; the software remains
provided without warranty, subject to the MIT license and applicable law.
