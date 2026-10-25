# SignalShift 2.4.4 — Less work between audio changes

Default-only notifications read the current default roles and update cached flags. Mute-only notifications read mute state from the tracked endpoints. Device additions/removals, state/name/property changes, unknown default IDs and refresh errors retain a full endpoint scan. Native callbacks still only enqueue work on the owning dispatcher.

The tray compares cached device/profile shapes instead of building identity strings and serializing profiles on every refresh. It rebuilds for changed routes, names, device lists and ordering, including in-place profile edits. Pending changes are retained while profile/shortcut dialogs pause refreshes.

All device switching availability checks, role verification and rollback remain. Microphone control still resolves all current default input roles before changing mute. Update download checks, permissions, same-user IPC, audio recording behavior and Windows security settings are unchanged.

## Measurements

Read-only refresh probes used the same connected endpoints and two sample profiles, with 100 refreshes per scenario:

| Scenario | Managed allocation before | After | Endpoint enumerations before → after | Property reads before → after |
| --- | ---: | ---: | ---: | ---: |
| Known default state | 2,036,288 B | 188,424 B | 200 → 0 | 1,500 → 0 |
| Mute-only state | 2,034,504 B | 18,120 B | 200 → 0 | 1,500 → 0 |

This measures callback refresh work, not retained RAM or complete hotkey-switch latency. The real output-switch test also produced property-change notifications and correctly used the full-scan fallback. [Raw results and method](refresh-2.4.4.json).

Validation covers callback classification/disposal, missing/new defaults, metadata preservation, null defaults, in-place profile/device edits, ordering, unchanged-layout allocation, existing features and release safeguards. Real-device tests verified output switching/tray state, mute commands, registered hotkeys, external mute notifications, and cold startup. Original microphone states, all six default roles and speaker mute/volume were preserved or restored; no microphone audio was recorded.
