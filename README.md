<p align="center"><img src="Assets/SonarShift.png" width="96" alt="SignalShift icon" /></p>
<h1 align="center">SignalShift</h1>
<p align="center">Your devices. Your shortcuts. Your flow.</p>

SignalShift is a native Windows audio switcher with global shortcuts, saved audio
profiles, dark/light themes and a lightweight background tray.

**[Download SignalShift 2.1 preview](https://github.com/youval1234/SignalShift/releases/tag/preview-2.1.0)**
for Windows x64. This self-contained EXE is unsigned; see the
[Windows trust guide and responsibility notice](docs/windows-trust.md).

| Dark | Light |
| --- | --- |
| ![Dark appearance](Assets/SignalShift-Dark.png) | ![Light appearance](Assets/SignalShift-Light.png) |

## New in 2.1

- **One shortcut for all profiles:** enable **Cycle audio profiles** under Shortcuts.
  Its suggested shortcut is **Ctrl + Alt + P**, and you can record a different one.
  It applies the next profile's output and microphone together, skips disconnected
  setups, and wraps to the first profile. Profiles cycle in their displayed order.
- **Dark and light mode:** choose your theme in Settings, or enable a separate
  configurable appearance shortcut. Both shortcut actions work from the tray.
- **More resilient settings reload:** brief file locks are retried while retaining
  the current settings, rather than crashing the background app.
- **Audio-only scope:** the experimental camera feature has been removed completely.
  There is no camera page, webcam hotkey, native camera DLL or camera installer in
  this version. If you installed the retired component, see the removal note below.

New shortcuts default to disabled when upgrading so they do not unexpectedly claim
keys. Existing individual profile shortcuts, audio choices and exports remain usable.
[Full release notes](docs/release-2.1.md).

## Audio and profiles

Choose playback and recording devices to include in each cycle. Disconnected choices
are remembered and skipped until available again. Profiles apply an output and
microphone together; a device set to **Leave unchanged** keeps its current selection.
Use the profile-cycle shortcut or assign a dedicated shortcut to any profile.
When no profile is available, cycling makes no audio changes.

Default device shortcuts are **Ctrl + Alt + F11** for output and **Ctrl + Alt + F10**
for input. Configure them under Shortcuts. Conflicts are reported without silently
replacing another action. Console and multimedia roles are changed, with optional
communications switching for calls. Apps pinned to a specific device can retain it.

A soft confirmation pluck follows successful audio hotkeys. The reusable popup shows
the latest device or profile immediately, without notification queues or stealing
focus. Sounds and notifications can be enabled independently. Closing Settings
releases its process; the tray keeps handling shortcuts. Choose Quit to exit.

## Downloads and Windows trust

Download **SignalShift.exe** and **SHA256SUMS.txt** from the
[2.1 preview release](https://github.com/youval1234/SignalShift/releases/tag/preview-2.1.0).
Quit older Sonora/SonarShift/SignalShift processes before replacing the EXE.
Audio switching and themes support Windows 10/11 x64.

GitHub previews are unsigned. Smart App Control may block them; GitHub hosting or a
matching checksum does not establish a trusted publisher. The
[trust guide](docs/windows-trust.md) explains optional Windows settings and their
risks. The app never changes Windows security protections. See [signing status](CODE_SIGNING.md).

Distribution is through GitHub. The Microsoft Store submission and product entry
were removed on 24 October 2026; Store publication is no longer planned.

### Removing the retired camera component

If you installed the optional 2.0 component, close apps using SignalShift Camera and
run the original **SignalShift.CameraSetup.exe --uninstall** with administrator
approval, then remove its extracted files. That installer belongs to the retired
2.0 component and is not shipped with 2.1. Select your normal webcam again in apps
that used the virtual device. Your physical webcams and phone-camera software are
separate and do not need removal.

## Settings and privacy

Preferences remain in `%LOCALAPPDATA%\Sonora\settings.json` to preserve upgrades.
Settings → Export writes a UTF-8 Notepad `.txt` backup. Import accepts SignalShift,
SonarShift and Sonora exports, raw settings and SoundSwitch configurations; a
restorable backup is made before replacement. Retired camera fields are ignored
when importing old settings and are not exported by 2.1.

Exports contain audio-device names and IDs, so share them intentionally. The app
has no account, telemetry, advertising, network service or recording feature.
Startup is opt-in. [Privacy policy](PRIVACY.md).

## Build and validation

Install the .NET 10 SDK on Windows, then run:

```powershell
./build.ps1
./build.ps1 -Test
```

The default portable build favors lower private memory. `./build.ps1 -Compact`
makes a smaller EXE at the cost of higher private RAM. No native camera build tools
or camera packages are required.

Checks cover settings migration/import/export, profile-cycle planning and shortcut
conflicts, live theme colors and page rendering, tray appearance dispatch, locked
settings recovery, IPC collision handling, and repeated synthetic switch resource
use. These do not establish compatibility with every Windows audio driver.

The separate settings process, event-driven device refresh, reusable popup/tray,
bounded sound buffers and file-backed assemblies retain previous optimizations.
[Memory measurements](docs/performance-1.5.md) and
[switch allocation results](docs/performance-switching.md) remain historical
benchmarks, not new measurements of every 2.1 workload.
