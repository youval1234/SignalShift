# SignalShift 2.4.6 — Separate app and microphone tray icons

SignalShift's purple app icon is restored. A separate microphone icon shows live, muted, mixed or unavailable state beside it, with its own state tooltip. Both icons open SignalShift on double-click and share the existing audio-control menu on right-click. Windows may place either icon in the **^** hidden-icons menu; drag each one out to keep it visible.

The microphone icon still follows external mute changes and uses four cached images. Unchanged status refreshes do not replace icons or rebuild menus. Both tray components are disposed on exit; the shared menu is owned and released once by the host. Startup behavior and the red slashed-microphone popup are unchanged.

Validation includes 39 feature checks, tray separation and menu ownership, repeated unchanged-status allocation checks, real-device mute/startup notifications and the existing repeated-switch resource tests. Audio defaults and original mute/volume state are restored after hardware tests. No microphone audio is recorded by these tests.
