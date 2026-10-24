# SignalShift 2.1 preview — audio profiles, one shortcut

This update removes the experimental camera feature and focuses on audio switching,
profiles and dark/light appearance. The app name remains SignalShift.

- Configurable profile-cycle hotkey (suggested Ctrl + Alt + P): applies the next
  output/microphone setup in profile order, skips disconnected setups, and wraps.
- Individual profile shortcuts and the separate appearance hotkey remain available.
- Settings reload retries brief locks without losing the current preferences.
- Camera UI, shortcuts, native code, installer, dependencies and release artifacts
  are removed from this version. Live-camera development has been discontinued.
- Existing audio preferences and legacy exports remain compatible. New shortcuts
  stay disabled on upgrade until you enable them in Shortcuts.

Quit the old app and replace its EXE with SignalShift.exe. Export a settings backup
first if desired. If you installed the retired 2.0 camera component, uninstall it
with its original setup executable using --uninstall and administrator approval.
Do not remove your physical webcam or phone-camera drivers.

Downloads contain the self-contained Windows x64 EXE and its SHA-256 checksum.
This is an unsigned preview; Windows can still block it. See the
[Windows trust instructions and responsibility notice](windows-trust.md).
Security-setting changes remain the user's decision and responsibility, subject
to the MIT license and applicable law. The app does not automate those changes.
