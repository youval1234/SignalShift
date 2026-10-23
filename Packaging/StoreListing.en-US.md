# SonarShift — English (United States) Store listing

Submitted for Microsoft Store certification on 23 October 2026. Publishing is held for a separate manual release after certification. The first-submission release-notes field was left blank; the proposed release text below is retained for future use.

## Short description

Switch speakers, headphones and microphones with custom shortcuts and a clean desktop interface.

## Description

Your sound, one shortcut away.

SonarShift makes it easy to move between speakers, headphones and microphones. Choose the devices you want in each cycle, assign your own global shortcuts, and switch without leaving what you are doing.

Save an output and microphone together as a profile for your desk, calls or headphones. A short sound and a compact notification confirm each switch. Repeated switches update the same popup immediately, so feedback stays in step with you.

Keep SonarShift in the system tray, choose whether it starts with Windows, and carry your setup between computers with readable text settings files. Existing SoundSwitch users can import supported device selections and shortcuts from their local configuration.

SonarShift runs locally with no account, ads or telemetry. Audio device names, preferences and profiles stay on your computer. Exported settings include device information; share them only intentionally.

This app changes Windows default audio devices. Applications assigned to a specific device may keep using that device until you change their own settings or restart their audio stream.

## Features

- Cycle selected playback and recording devices using custom global shortcuts.
- Save speaker and microphone pairs as profiles with optional shortcuts.
- Hear a short confirmation sound and see an immediately updating switch popup.
- Control the app from the system tray with optional Windows startup.
- Export and import settings as readable text files that open in Notepad.
- Import supported device selections and shortcuts from a local SoundSwitch configuration.
- Use a native Windows interface with no account, ads or telemetry.

## Release notes

First Microsoft Store release of SonarShift 1.4. Renamed from Sonora, with preserved settings compatibility, improved switch sounds, and an immediately updating notification popup.

## Public links and publisher

- Publisher display name: youval1234
- Website: https://github.com/youval1234/SonarShift
- Support: https://github.com/youval1234/SonarShift/issues
- Privacy policy: https://github.com/youval1234/SonarShift/blob/main/PRIVACY.md
- Source license: MIT

No private email, phone number, home address or legal name is included in this listing draft.

## Search terms

audio switcher; sound switcher; microphone; speakers; headphones; hotkeys; audio devices

## Screenshot caption

Choose the audio devices in your shortcut cycle and switch from one place.

## Certification notes

SonarShift is a self-contained x64 WPF desktop utility. It needs the restricted runFullTrust capability for desktop audio endpoint enumeration, default endpoint changes, global hotkeys, and a system tray icon. It is not a background microphone recorder and does not capture or transmit audio. Windows startup is opt-in through StartupTask.

Device enumeration uses IMMDeviceEnumerator. Default endpoint changes currently use the undocumented IPolicyConfig COM interface, a desktop compatibility dependency disclosed here for review. No security protections or certificates are changed or installed.

No account, sign-in, payment, network service or test credentials are required. Test with at least two connected output devices and, for microphone cycling, two recording devices. In Devices, select the endpoints to include; in Shortcuts, assign unused key combinations. Verify the Windows default device changes, then check confirmation sounds, repeated switch notifications, profiles, tray reopening, and import/export. Optional SoundSwitch import requires an existing local SoundSwitch configuration. SonarShift reads that file without modifying it.

Close any other audio-switcher process using the same shortcut while testing. Closing the main window leaves the app in the tray by default; use Quit SonarShift from its tray menu to fully exit.

Validation completed: portable application diagnostics and MakeAppx package pack/unpack with payload hash checks. Windows App Certification Kit and installed Store-package runtime validation have not been completed locally; these are not represented as having passed.
