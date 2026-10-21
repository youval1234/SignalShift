Sonora 1.0 is a portable Windows audio switcher with a modern dark interface and an original violet/mint app icon.

- Select custom playback and recording cycles, including virtual devices.
- Record independent global shortcuts, with conflict detection.
- Save device-pair profiles with optional shortcuts.
- Switch directly from the system tray.
- Optional notifications, communications-device switching, and startup with Windows.

Download **Sonora.exe** and launch it. The x64 executable includes its .NET runtime and needs no installer or administrator access. It is unsigned. Keep it in a permanent folder before enabling startup.

Default keys: **Ctrl + Alt + F11** for output, **Ctrl + Alt + F10** for input. Change them on the Shortcuts page. Release a shortcut in SoundSwitch first if you want to reuse it in Sonora.

Validation on Windows: six packaged-binary diagnostic checks passed, including real playback and recording endpoint switching with restoration of original defaults, cycle edge cases, settings recovery, and native global-hotkey conflicts. UI checks also covered device selection, a real output shortcut, custom shortcut recording, profile creation, and profile activation.

`SHA256SUMS.txt` contains the SHA-256 of Sonora.exe. The full source, icon assets, build script, and Windows build workflow are in this repository.

This release covers the core switching workflow; automated per-app rules and mixer controls are not included.
