# SignalShift privacy policy

SignalShift runs locally. It has no accounts, advertising, telemetry, network client
or automatic uploads.

The app reads audio endpoint names, identifiers and default-device/mute state to
show and control Windows audio. Preferences, selected devices, shortcuts and
profiles are stored in `%LOCALAPPDATA%\Sonora`. Import backups and error logs stay
on the computer. SoundSwitch migration reads its local configuration without
changing it.

Settings export writes a user-selected text file with preferences, profiles, audio
device names/IDs and startup preference. Share these files only intentionally;
SignalShift does not upload them.

Optional microphone tests start only after you click Record & hear. Up to five
seconds stays in a bounded memory buffer, is played through the output you choose,
and is cleared when replaced or closed. Clips are not saved, exported or uploaded.
The idle tray never captures audio. Windows microphone permissions still apply.
Output tests play a generated sample without changing defaults or system volume.

Microphone mute reads and changes only the Windows default recording endpoints'
mute state. It does not capture audio or change speaker settings. Notifications
keep the displayed status current. The optional fixed command-line mute action
uses a same-user local pipe; it does not accept arbitrary code or executable paths.

Startup is opt-in. To uninstall, turn off startup, quit the tray and delete the
portable EXE and local settings folder. Export preferences first if wanted.

GitHub hosts source and release downloads under its own privacy policy. The
installed application does not contact GitHub. Public screenshots use sample
devices and profiles rather than a contributor's local configuration.
