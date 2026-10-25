# SignalShift 2.4 — Updates and interface polish

- Consistent vector minimize, maximize/restore and close icons with equal-sized controls.
- Automatic background update checks on launch, plus **Check for updates** and an automatic-update switch in Settings.
- Short hover, press, page and theme transitions that respect Windows animation preferences. No continuous animation runs in the tray.

The portable updater follows published releases from this repository. Preview builds follow newer previews and stable versions; stable builds follow stable versions only. Downloads must match the release SHA-256, size, product name and version. Updates replace the EXE at its current path and restart the app, preserving preferences and local launch integrations. Failed startup restores the previous EXE where replacement is possible. Checks have timeouts; offline failures keep the running app usable.

Update checks contact GitHub over HTTPS. No audio, settings, device names or IDs are uploaded. GitHub receives ordinary request information including IP address and the app version. Disable automatic updates in Settings to stop launch checks. A manual check still contacts GitHub when clicked.

These previews remain unsigned. Checksums verify bytes, not publisher trust, and Windows policies can block execution or updating. The updater does not change Windows security settings. See the [Windows trust guide](windows-trust.md).

Validation includes release selection/channel rules, corrupted/truncated downloads, cancellation, replacement rollback and download-zone preservation, plus existing audio/profile/theme and resource-use checks. An isolated upgrade from 2.3.1 verified restart, filename/settings preservation and a tray process without WPF/audio-test libraries.
