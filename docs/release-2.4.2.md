# SignalShift 2.4.2 — Visible version and window bounds

- Move the version directly below the SignalShift name in the sidebar, with larger text and stronger contrast. It stays visible on every page.
- Read the label and About version from executable metadata, avoiding stale hardcoded versions.
- Keep maximized windows inside the current monitor's working area, above the taskbar. Use that monitor's DPI and taskbar position rather than assuming the primary screen.
- Fit the initial window to the available area on smaller or scaled displays.

Validation checks maximized native window bounds against the monitor work area and renders sample previews. Existing audio, profile, update and settings checks remain enabled.
