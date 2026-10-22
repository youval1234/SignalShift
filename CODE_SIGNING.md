# Code signing and release verification

Current GitHub previews are unsigned Windows x64 builds. Smart App Control or an
organization's policies may block them. GitHub hosting, checksums and build
provenance do not replace a trusted Authenticode signature. The app and release
scripts never disable Windows protections. See the [trust guide](docs/windows-trust.md).

The unsigned preview workflow is owner-triggered. It requires a successful main
build, matching project/tag versions and an existing draft targeting that exact
source. Only the EXE and checksum are uploaded; asset sizes and SHA-256 digests are
verified before publication. Previews remain marked as prereleases.

A separate signed-release workflow is available for a future configured signing
provider. It requires reviewed source, approved signing policy, exact publisher
identity, Windows Authenticode trust and timestamp checks. Missing configuration,
unsigned files or failed verification stop publication without an unsigned fallback.

Signing tokens and credentials belong in repository secrets. Source, public logs
and release assets must never include local settings, private signing keys,
exported backups, private paths or contributor contact details. Release builds omit
debug symbols. Third-party license notices remain included.
