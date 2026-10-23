# SonarShift 1.5 portable memory comparison

Measured 23 October 2026 on the same Windows 11 PC, using the same 1.5 source and isolated settings, with only `EnableCompressionInSingleFile` changed. Each scenario settled for five seconds and then took ten samples, one second apart. Both variants were self-contained Windows x64 EXEs. Settings were open at the same default size and showed the same connected devices. An existing user tray instance remained running throughout both tests.

| Mean memory, bytes | Compact EXE | Default EXE | Reduction |
| --- | ---: | ---: | ---: |
| Tray private working set | 55,255,040 | 9,875,456 | 82.1% |
| Tray private commit | 58,386,432 | 13,998,080 | 76.0% |
| Tray total working set | 132,960,666 | 58,377,421 | 56.1% |
| Settings open: combined private working set | 183,215,309 | 54,928,998 | 70.0% |
| Settings open: combined private commit | 238,243,430 | 111,282,586 | 53.3% |
| Settings open: sum of total working sets | 407,153,459 | 195,126,886 | 52.1% |

The open-settings measurements include **both** the host and settings process. Summed total working sets count shared pages more than once; combined private working set is the more useful comparison for Task Manager's usual process Memory column. Private commit is reserved committed memory, not resident physical RAM. These numbers are samples, not limits or guarantees. The tests verified startup, callback registration and process cleanup without switching real audio defaults.

Raw samples: [compact](benchmarks/interface-compact.json), [default](benchmarks/interface-default.json). Reproduce with `tools/Measure-InterfaceFootprint.ps1`, passing `-Executable` and `-ReportPath`. Keep the sample duration, machine, screen scaling and connected devices consistent.

## Why packaging matters

Microsoft documents that compressed single-file assemblies must decompress into memory at startup. Each process then bears that private memory cost. Leaving assemblies uncompressed permits file-backed loading, reducing private memory and allowing Windows to share read-only pages. See [Microsoft's single-file deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps).

The cost is disk space: the tested EXE increased from **75,695,359 to 173,429,793 bytes**, approximately 72.2 to 165.4 MiB. Those are executable sizes, not complete installed footprints including .NET's native extraction cache. `build.ps1 -Compact` remains available when EXE size matters more than memory. No trimming, unsupported full-extraction compatibility mode, forced garbage collection or working-set trimming is used.

The Store package already uses unpacked assemblies. This packaging change does not establish a new Store memory improvement. The submitted 1.4 package is unchanged; the 1.5 preview still requires packaged startup and physical device validation before a stable release.
