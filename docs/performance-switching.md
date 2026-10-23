# Repeated-switch allocation investigation

A user observed small Task Manager memory increases after each hotkey. That can come from temporary allocations, retained reusable buffers, or a leak. A rising working set alone does not distinguish these cases.

## Synthetic comparison, 23 October 2026

The probe warms up 200 simulated output-default changes, then records 2,000 more. It alternates two fake devices, updates the actual tray menu and presents the actual native popup with its normal message processing and animation. It never writes Windows audio defaults, plays sounds, invokes actual global hotkeys, forces garbage collection or trims the working set.

| Metric over 2,000 measured changes | Before | Resource-cache implementation |
| --- | ---: | ---: |
| Cumulative managed allocations, bytes | 100,601,616 | 3,271,464 |
| Allocations per change, bytes | 50,300.8 | 1,635.7 |
| Gen-0 garbage collections | 7 | 0 |
| Private-commit increase, bytes | 18,063,360 | 4,304,896 |
| GDI-object change after warmup | 0 | 0 |
| USER-object change after warmup | 0 | -1 |

Temporary allocations fell **96.7%**. These are cumulative allocation bytes, not RAM retained per switch. The runs had different garbage-collection timing; neither the commit increase nor heap endpoint is a permanent-leak measurement. The baseline managed heap was only 582,368 bytes larger at the end despite allocating about 100 MB, indicating most temporary objects had been reclaimed. The cached implementation allocated about 3.3 MB without needing a collection during the window.

## Implementation and checks

- Default changes update existing tray checkmarks; topology or profile changes still rebuild the menu.
- The popup owns and reuses its pens, brushes, drawing paths and string format, disposing them when it exits.
- The clipping region is recreated only when geometry or DPI changes.
- Device-panel visuals update once after a switch instead of twice.
- A first-show focus check also identified WinForms' managed TopMost focus path. The popup sets its topmost native style while retaining nonactivation and click-through styles instead.

The allocation comparison above precedes that final focus adjustment. Final CI runs validate the complete candidate, including a longer 10,000-change run, popup-handle reuse, native resource bounds, correct tray checkmarks and no popup activation. See the [Windows build workflow](https://github.com/youval1234/SonarShift/actions/workflows/build.yml) and its separate allocation-check artifact for raw final results. The executable artifact contains only the EXE and checksum.

Reproduce using `tools/Test-SwitchAllocations.ps1 -Executable <path> -ReportPath <path>`, optionally `-LongRun`. The probe is available only to instances with an isolated test-data directory. Its default allocation budget is 10,000 bytes per simulated change after warmup. It does not establish that COM audio policy calls, audio drivers or sound playback never allocate or retain memory. Physical-device and real-hotkey endurance testing remain separate.
