# SignalShift 2.4.5 — Microphone status at a glance

- The system tray now shows a green microphone when live, a red slashed microphone when muted, amber for mixed default-input states, or gray when unavailable. Hover text describes the state, including changes made by other programs. Windows may place the icon in its **^** hidden-icons menu.
- Muted notifications now show a red microphone with a diagonal slash. The messages remain **Microphone muted** and **Microphone live**.
- Settings adds **Start microphone live at Windows sign-in**. Enable it with **Start with Windows** to unmute the default microphones once at sign-in. Ordinary launches, restarts and updates preserve intentional mute. The option is off by default; existing users are not silently opted in.
- A delayed microphone gets a bounded 30-second startup retry window. Manual microphone commands cancel the pending action. Icons and tooltip strings are cached, and the retry timer stops after completion, cancellation or expiry.

All 38 feature checks pass. Real-device tests verified opt-in sign-in unmute, normal restart preservation, later intentional mute, external changes and matching tray state. Tests restored original microphone states, all six default audio roles and speaker mute/volume; no microphone audio was recorded. A 2,000-change notification regression test showed no increase in GDI or USER object counts after warmup.

See [microphone status and startup](microphone-status.md) for setup. This portable release remains unsigned; update verification and Windows security settings are unchanged.
