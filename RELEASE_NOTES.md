# SignalShift release notes

## 2.4.1

- Highlight the profile matching the current audio setup, with an ACTIVE badge.
- Follow external device changes without new polling.
- Keep the update-check footer consistent with the completed result.

[Details](docs/release-2.4.1.md).

## 2.4.0

- Consistent vector window controls.
- Launch-time GitHub updates, a manual Settings update button and an automatic-check opt-out.
- Short hover, press, page and theme transitions respecting Windows animation preferences.
- Verified in-place replacement, settings preservation and rollback checks.

[Details](docs/release-2.4.md).

## 2.3.1

- Microphone feedback uses the exact messages **Microphone muted** and **Microphone live**.
- Public screenshots use sample devices and profiles.
- Documentation focuses on the audio application and its supported controls.

## 2.3

- Add microphone mute through the app, tray, configurable hotkey and optional fixed
  command-line action.
- Read actual Windows endpoint state and follow external changes through callbacks.
- Preserve speaker volume/mute, microphone gain and default-device choices.

## 2.2

- Add sidebar light/dark control and remove the theme hotkey.
- Add output samples and temporary five-second microphone playback tests.
- Fix device-test interoperability with the live audio-device watcher in 2.2.1.

## 2.1

- Add whole-profile cycling, skipping disconnected setups and preserving individual
  profile shortcuts.
- Improve settings reload recovery after transient file locks.

See GitHub Releases for earlier device-switching, settings portability and feedback updates.
