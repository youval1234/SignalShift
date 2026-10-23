<p align="center"><img src="Assets/SonarShift.png" width="96" alt="SignalShift icon" /></p>
<h1 align="center">SignalShift</h1>
<p align="center">Your devices. Your shortcuts. Your flow.</p>

SignalShift is the next version of SonarShift, a native Windows app for switching
playback and recording devices with global shortcuts, profiles and instant feedback.
Version 2.0 adds dark/light appearance and an optional Windows 11 virtual camera.

**2.0 is in development, not a production release.** Real webcam, calling-app and
OBS testing is still required. The existing desktop app and pending Store 1.4
submission have not been replaced. [Review the update](https://github.com/youval1234/SignalShift/pull/4).

## New in 2.0

- **Dark and light mode:** choose a look in Settings, or record a global appearance
  shortcut under Shortcuts. It also works while the app is in the tray.
- **Webcam cycle:** select SignalShift Camera once in a call or streaming app, then
  change the physical webcam with a configurable shortcut. Optional portable
  component, Windows 11 x64, current output 720p NV12 at nominal 30 fps.
- **Existing optimizations retained:** separate settings process, event-driven
  audio refresh, reused native popup and tray entries, bounded sound buffers, and
  uncompressed file-backed assemblies. Native camera code stays unloaded when off.
- **Migration:** audio choices, profiles, shortcuts and Notepad exports remain
  compatible. New shortcuts and camera support default to disabled during upgrades.

See [camera installation, privacy, resource boundaries and current limitations](Camera/README.md).
The elevated portable camera installer is not part of the Store build; Store camera
registration must be validated separately before submission.

## Audio features

Choose playback and recording devices to include in each cycle. Disconnected choices
are remembered and skipped until available again. Profiles switch an output and
microphone together, with an optional dedicated shortcut. Console and multimedia
roles are changed, with optional communications switching for calls.

Default shortcuts are **Ctrl + Alt + F11** for output and **Ctrl + Alt + F10** for
input. Record alternatives under Shortcuts; conflicts are reported without silently
replacing an existing key. Apps pinned to a particular audio device can retain it.

A short confirmation pluck follows successful audio hotkeys. The reusable popup
updates immediately with the latest device, without notification queues or taking
focus. Sounds and notifications can be enabled independently. Closing settings
releases its process; the tray continues handling shortcuts. Choose Quit to exit.

## Downloads and Windows trust

The previous [SonarShift 1.5.1 preview](https://github.com/youval1234/SignalShift/releases/tag/preview-1.5.1)
remains available with its original **SonarShift.exe** filename. New 2.0 builds produce
**SignalShift.exe**. Historical releases are unchanged.

Portable previews are self-contained and unsigned. Windows Smart App Control can
block them; downloading from GitHub or checking a hash does not establish a trusted
publisher. [Trusted release signing](CODE_SIGNING.md) still needs an approved provider.
Neither the app nor the optional camera setup changes Windows security settings.
Audio and appearance support Windows 10; the virtual camera requires Windows 11 x64.

SonarShift 1.4 was submitted for Microsoft Store certification on 23 October 2026,
with a manual publishing hold. This development branch does not update or cancel it.

## Local settings and backups

Preferences remain in `%LOCALAPPDATA%\Sonora\settings.json` so upgrades preserve
existing choices. Settings → Export writes a UTF-8 Notepad `.txt` backup. Import
accepts SignalShift, SonarShift and Sonora exports, raw settings, and SoundSwitch
configurations; a restorable backup is made before replacement. SoundSwitch can also
be imported directly from its current local configuration.

Exports include device names and IDs. Camera IDs can differ between PCs, so reselect
webcams on the destination machine. The app has no account, telemetry, advertising,
network service or recording feature. Portable startup is opt-in, with legacy startup
entries migrated to the new executable. Quit an older running version before updating.

## Build and validation

Install the .NET 10 SDK on Windows, then run:

```powershell
./build.ps1
./build.ps1 -Test
```

The default portable build favors lower private memory. `./build.ps1 -Compact` makes
a smaller EXE at the cost of higher private RAM. Native camera builds additionally
need Visual Studio C++ tools, the Windows 11 SDK and NuGet CLI:

```powershell
./Camera/build-camera.ps1
```

CI builds managed and native components, checks settings migration, imports, shortcut
conflicts and live theme colors, renders all pages in both modes, tests theme hotkey
dispatch without loading WPF in the tray, and exercises bounded black-frame output.
It also reruns the 2,000/10,000 synthetic audio popup/tray allocation regressions.
These checks do not replace real camera, call or OBS compatibility testing.

Previous [memory measurements](docs/performance-1.5.md) and [switch allocation results](docs/performance-switching.md)
remain historical benchmarks, not measurements of camera-active 2.0 operation.
The icon and original sounds remain repository-generated assets. Microsoft camera
sample code and build dependencies retain their [third-party notices](Camera/Native/THIRD-PARTY-NOTICES.md).
