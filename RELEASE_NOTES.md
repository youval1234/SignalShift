Sonora 1.1 adds settings portability and SoundSwitch migration.

- Export and import readable Notepad `.txt` settings files from Settings.
- Import existing Sonora JSON settings or migrate directly from SoundSwitch.
- Preserve individual playback and recording shortcut enabled states.
- Match changed device IDs by exact name and audio direction; keep disconnected choices.
- Save an automatic, restorable text backup before each import; reject invalid settings before replacing preferences.
- Retry shortcut registration automatically when another app releases an imported key combination.

Download **Sonora.exe** and launch it. The x64 executable includes its .NET runtime and needs no installer or administrator access. It is unsigned. Keep it in a permanent folder before enabling startup.

Default keys: **Ctrl + Alt + F11** for output, **Ctrl + Alt + F10** for input. Change them on the Shortcuts page. Release a shortcut in SoundSwitch first if you want to reuse it in Sonora.

Validation covers text roundtrips with Unicode names and profiles, startup choice, changed/ambiguous/disconnected device mapping, SoundSwitch shortcuts and flags, invalid/future-version/duplicate-shortcut rejection, and backup restoration, alongside the existing endpoint, cycle, settings recovery and hotkey diagnostics.

`SHA256SUMS.txt` contains the SHA-256 of Sonora.exe. The full source, icon assets, build script, and Windows build workflow are in this repository.

SoundSwitch migration reads its configuration without changing it. It imports supported device cycling settings; SoundSwitch's microphone-mute shortcut, profiles/automation, per-app rules, and banner styling are not transferred. Existing Sonora profiles are preserved.
