# SignalShift 2.4.3 — Faster window startup

The settings process starts alongside tray initialization instead of waiting for its audio scan. The window draws its navigation and a short loading message before populating audio controls. Native audio objects remain on their owning UI thread, and the settings process still exits when closed rather than staying cached in RAM.

Reopening the app uses one display-and-update IPC request. Display is requested first, and a busy IPC channel falls back after 500 ms instead of delaying behind separate requests. Duplicate startup-registration work in the settings process is removed. Update checks remain asynchronous.

Five repeated launches per build on one Windows PC measured median first frame at **932.40 → 732.24 ms** (21.5% lower). The populated device page measured **857.57 → 798.13 ms** (6.9% lower). These are isolated-settings tests with real endpoint enumeration and OS caches left intact; they do not predict every first launch after reboot. [Raw samples and method](startup-2.4.3.json).

The regular build remains uncompressed/file-backed. A precompilation experiment saved additional time but added about 18 MiB, so it was not selected.

Current 2.4.x versions are regular GitHub releases. Future releases use version tags and the owner-triggered release workflow. The executables remain unsigned; release classification does not establish publisher trust. [Verification policy](../CODE_SIGNING.md).
