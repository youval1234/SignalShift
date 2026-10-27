# SignalShift 2.5.0 — Find your color

Settings → Appearance now offers eight themes with clickable preview cards and a selected-state marker:

| Theme | Look |
| --- | --- |
| Dark | Original midnight violet |
| Light | Clean lilac |
| Aurora | Emerald after dark |
| Ocean | Deep blue and electric cyan |
| Sunset | Coral and warm dusk |
| Rose | Orchid and soft neon |
| Sakura | Blush and cherry blossom |
| Sand | Amber and warm ivory |

Selections apply immediately and persist across restarts and settings import/export. The sidebar light/dark button remains available. Device/profile cards, dialogs, text fields, menus and confirmation popups follow the selected palette. Microphone live/muted feedback keeps its green/red meaning and the separate tray icons remain unchanged.

Themes use the existing shared brush cache, with a bounded set of palette tokens. Frozen preview swatches exist only with the Settings page; no theme timers, downloads or background polling are added. Native popup colors are cached when the theme changes, rather than parsed on every action.

Validation includes all 41 feature checks, actual Settings-card selection and persistence for every theme, palette completeness, text/action-label contrast checks, native popup rendering, all-theme UI previews, repeated-switch resource checks and the existing refresh-allocation safeguards.
