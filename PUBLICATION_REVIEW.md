# Publication review — 22 October 2026

Before changing this repository from private to public:

- Scanned every reachable branch/tag and commit with Gitleaks 8.30.1. No credentials or private keys were detected.
- Inspected historical file names and contents for local user paths, personal identifiers, saved settings, real audio endpoint IDs, debug symbols, and archived data. No such data was found in tracked source/history. Commit email addresses use GitHub's noreply domain.
- Visually inspected the two UI screenshots. They contain the app interface and generic audio product names, without account names, device identifiers, personal files or other desktop content.
- Reviewed the source's file access, startup registration, global shortcuts, audio switching, imports and diagnostic behavior. The application does not include a network client, telemetry, remote command execution or credential collection. It uses local Windows audio APIs and user-requested settings files.
- Reviewed all nine existing GitHub Actions build logs. No local personal identifiers were found. The build workflow uploads only `Sonora.exe` and its checksum from GitHub-hosted runners; it does not read or upload a maintainer's local settings.
- Found a local build-directory path in the CodeView debug metadata of each original release executable. Rebuilt all four original version tags with debug symbols disabled, inspected the embedded bundle contents, and replaced the release executables and checksums before publication. No settings, diagnostics, private keys or personal path markers were present in the cleaned bundles. The bundled core runtime library matched the official local .NET SDK runtime file.
- Successfully compiled the signing-preparation source with no warnings or errors and parsed the signing workflow/configuration. The new workflow is manual, does not run for pull requests, requires provider configuration, and rejects unsigned/unexpected results.

This is a source/privacy review and static artifact inspection, not a guarantee against every vulnerability. The cleaned release builds were not launched to bypass Smart App Control. They remain unsigned. SignPath eligibility, account setup, terms, production signing and post-signing runtime validation are still pending.

See [Code signing policy](CODE_SIGNING.md) and [Privacy policy](PRIVACY.md).
