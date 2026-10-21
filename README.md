<p align="center"><img src="Assets/Sonora.png" width="96" alt="Sonora icon" /></p>
<h1 align="center">Sonora</h1>
<p align="center">Your sound. One shortcut away.</p>

Sonora is a native Windows desktop app for switching playback and recording devices with custom global shortcuts. Pick the devices in your cycle, save output/microphone pairs as profiles, and keep everything within reach from the system tray.

![Sonora desktop interface](Assets/Screenshot.png)

## Download

Download **Sonora.exe** from the [latest release](https://github.com/youval1234/sonora/releases/latest). It is a portable, self-contained Windows x64 app: no installer, administrator access, or separate .NET installation required. Windows 10 (1809+) or Windows 11 is required.

The executable is unsigned; Windows may display its standard unknown-publisher warning. Release assets include a SHA-256 checksum. Keep the executable in a permanent folder before enabling “Start with Windows.”

## What it does

- Lists real connected playback and recording endpoints, including virtual devices such as SteelSeries Sonar.
- Cycles only through your selected, currently connected devices. Disconnected choices are remembered for reconnection.
- Records separate, customizable system-wide input and output shortcuts. Conflicts are reported without silently replacing your previous shortcut.
- Switches console and multimedia defaults, with optional communications switching for calls.
- Saves profiles with an output, an input, and an optional dedicated global shortcut.
- Offers direct device selection and profile activation from the tray.
- Refreshes changes to devices and defaults automatically, with an explicit Refresh button too.
- Supports optional notifications, startup with Windows, and closing to the tray.
- Keeps preferences locally. No accounts, network requests, advertising, or telemetry in the app.

## First run

1. Open **Sonora.exe**. Your current default devices are initially selected.
2. In **Devices → Playback**, check the outputs to cycle through. Do the same in **Recording** for microphones.
3. Press **Ctrl + Alt + F11** for the next output, or **Ctrl + Alt + F10** for the next input.
4. Open **Shortcuts** to record your own combination, such as Ctrl + Tab. If SoundSwitch or another app owns that combination, release it there first.
5. Optionally create a **Profile** to switch a device pair together.

Closing the window leaves Sonora in the tray by default; double-click the tray icon to reopen it. Use **Quit Sonora** to exit. A second launch reopens the existing instance.

## Build

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows, then run:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -o dist
```

Or use `./build.ps1`. The project uses only the Windows desktop framework and Windows APIs, with no third-party runtime libraries. `Assets/Sonora.svg` is the original icon design. To regenerate the PNG and multi-resolution ICO, install Pillow and run `python tools/make_icon.py`.

## Validation

```powershell
./build.ps1 -Test
```

Diagnostics validate endpoint enumeration, cycle behavior, Unicode settings roundtrips, corrupt settings recovery, and native global shortcut conflict detection. To also exercise real audio switching and restore every original default role:

```powershell
./build.ps1 -Test -AudioRoundtrip
```

The audio roundtrip temporarily changes playback and recording defaults. It requires at least two connected devices of each type. Original console, multimedia, and communications endpoints are restored in `finally` blocks.

## Data and behavior

Settings are stored in `%LOCALAPPDATA%\Sonora\settings.json` using atomic replacement. Invalid JSON is backed up before recovery. Device names and endpoint IDs stay local. Startup is opt-in and uses the current user's `Software\Microsoft\Windows\CurrentVersion\Run\Sonora` entry. To remove Sonora, disable startup, quit the app, and delete its executable and local settings folder.

Sonora changes the Windows default endpoints. Apps pinned to a specific audio device may retain that device; some apps require their audio stream to restart. Automated per-application rules, mixer controls, and all of SoundSwitch's advanced features are outside this release.

Windows documents endpoint enumeration through [IMMDeviceEnumerator](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator) and global shortcuts through [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey). Switching uses the widely implemented, undocumented `IPolicyConfig` interface ([interface contract](https://github.com/tartakynov/audioswitch/blob/master/IPolicyConfig.h)); future Windows changes could require compatibility updates. Sonora is an independent implementation and is not affiliated with SoundSwitch.

## License

[MIT](LICENSE)
