Sonora 1.2 adds subtle confirmation sounds for hotkey switching.

- Successful playback, recording and profile hotkeys now play a soft 148 ms chime.
- Input and output actions have distinct sounds, played after a short delay to let Windows finish switching audio routing.
- Sound feedback is enabled by default and can be disabled in Settings, independently of visual notifications. Preview buttons let you hear both sounds.
- Failed switches and empty cycles do not play a success sound. A shortcut with one selected device still confirms that the device is selected.
- The preference survives Notepad export/import; older settings files enable sounds by default.
- Sounds are original generated PCM assets embedded in the portable executable, with no extra downloads or dependencies.

Download **Sonora.exe** and launch it. The x64 executable includes its .NET runtime and needs no installer or administrator access. It is unsigned. Keep it in a permanent folder before enabling startup.

Default keys: **Ctrl + Alt + F11** for output, **Ctrl + Alt + F10** for input. Change them on the Shortcuts page. Release a shortcut in SoundSwitch first if you want to reuse it in Sonora.

Validation includes decoding both embedded audio assets, checking their duration and quiet peak levels, and preserving the sound preference through settings export/import and older files, alongside the existing diagnostics.

`SHA256SUMS.txt` contains the SHA-256 of Sonora.exe. The full source, icon assets, build script, and Windows build workflow are in this repository.

SoundSwitch migration reads its configuration without changing it. It imports supported device cycling settings; SoundSwitch's microphone-mute shortcut, profiles/automation, per-app rules, and banner styling are not transferred. Existing Sonora profiles are preserved.
