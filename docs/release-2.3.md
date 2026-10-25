# SignalShift 2.3

Microphone mute is available from Devices, the tray menu, a configurable hotkey
(suggested Ctrl + Alt + M) and `SignalShift.exe --toggle-microphone`.

The action controls all distinct Windows default recording endpoints for console,
multimedia and calls. Mixed states are muted first. Speaker mute/volume, microphone
gain and default devices are preserved. The display reads actual endpoint state
and follows external changes through notifications. An app pinned to another
microphone still needs its own mute control.

The command starts the tray quietly if necessary and sends the toggle once after
readiness. It works independently of keyboard-shortcut enable settings. It does
not record audio or open Settings.

## 2.3.1

Feedback uses **Microphone muted** and **Microphone live** as the main popup text.
Screenshots use sample devices and profiles. Documentation covers the audio app's
features, settings, privacy, build and verification.

The separate tray/settings processes, event-driven device refresh, reusable
notifications and file-backed assemblies retain previous optimizations. Builds
remain unsigned previews; see the [Windows trust guide](windows-trust.md).
