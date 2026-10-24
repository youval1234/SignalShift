# Microsoft Store preparation

**Archived, 24 October 2026:** Microsoft Store distribution has been discontinued.
The submission and product entry were deleted. The dated certification status below
is historical, not a current pending submission. Packaging sources remain for
reference; CI runs this workflow only when manually requested. Current downloads
are on [GitHub Releases](https://github.com/youval1234/SignalShift/releases).

The historical Sonora 1.3.0 release is unsigned. SonarShift 1.4.0 is the renamed app now in preparation. A GitHub release, SHA-256 checksum, or GitHub artifact attestation is not a Windows Authenticode signature and does not resolve Smart App Control's publisher check.

## Microsoft Store route

Microsoft signs MSIX submissions after certification. The repository includes `package-store.ps1` and an MSIX manifest. Store builds use the Windows StartupTask API instead of the portable build's Run registry entry. Startup remains opt-in; Windows user/policy overrides are respected. Settings import/export and the existing settings location are retained.

Status as of **23 October 2026**: SonarShift **1.4.0.0 x64** has been uploaded, validated, and submitted for Microsoft Store certification under public publisher `youval1234`. Partner Center reports **In certification**. Approval and a certified Store build are still pending. Publishing is held until the owner selects **Publish now**, so passing certification will not automatically release it. The exact Product identity values are recorded in `Packaging/StoreIdentity.json`; they are public package metadata, not account credentials or signing keys. See [CODE_SIGNING.md](../CODE_SIGNING.md) for the separate standalone EXE signing status.

The submission uses free pricing, an English (United States) listing, and GitHub website/support links. The generated content ratings include IARC/PEGI/Microsoft 3+ and ESRB Everyone. The listing and reviewer notes are retained in [StoreListing.en-US.md](StoreListing.en-US.md). No private contact details are included in that file or the public Store support fields.

To build the actual unsigned submission package using those assigned values:

```powershell
./package-store.ps1 -UseStoreIdentity -MakeAppxPath 'C:\path\to\WindowsSDK\x64\MakeAppx.exe'
```

This creates `SonarShift-1.4.0.0-x64-StoreSubmission-unsigned.msix`. Packaging does not upload the file or publish the app. Confirm the final customer-facing listing and contact fields before submission. Public publisher identity is `youval1234`, not a personal legal name.

## Enrollment and privacy check

1. Begin at [Microsoft Store developer onboarding](https://storedeveloper.microsoft.com/). Choose Individual for this non-commercial hobby project, subject to Microsoft's account rules.
2. The account owner completes Microsoft sign-in, ID/selfie verification and any agreement acceptance directly with Microsoft. Do not put ID documents, verification images, account credentials or enrollment screenshots in this repository or workflow artifacts.
3. At Profile details, inspect the actual Publisher Display Name before completing setup. A nickname is a candidate, not a guarantee of approval. Microsoft requires unique, non-misleading publisher names; names implying an organization may require a Company account. Confirm the proposed public name and any public contact fields meet the owner's privacy requirement before publication.
4. Reserve the product name after enrollment. Copy the exact Product identity values into the packaging command below. Do not substitute draft values or guess a publisher CN.
5. Review the Store listing, customer-contact information and package identity before submitting. App certification and acceptance of the publisher name remain Microsoft's decisions.

## Build and validate

For a packaging validation build:

```powershell
./package-store.ps1 -Draft -MakeAppxPath 'C:\path\to\WindowsSDK\x64\MakeAppx.exe'
```

The draft identity is deliberately marked as unverified. Do not install or submit the draft. A successful MakeAppx build validates package structure, not Store approval or runtime compatibility. Runtime testing on the protected development computer is pending trusted signing; do not disable protection or install a local root certificate to bypass that requirement.

The version is read from `SonarShift.csproj` and gains a final `.0` for the Store. An explicit `-Version` must match it. The script unpacks the resulting MSIX and checks every staged file's SHA-256, rejects private/debug files, and produces `.sha256` and `.build.json` receipts alongside it. Receipts contain package identity and relative payload names, not local build paths. The **Build Store validation package** GitHub workflow builds only a draft identity and never submits or publishes it.

After the owner completes Microsoft developer enrollment and reserves an app, copy the exact values from Partner Center > Product identity:

```powershell
./package-store.ps1 -IdentityName 'VALUE_FROM_PARTNER_CENTER' -Publisher 'CN=VALUE_FROM_PARTNER_CENTER' -PublisherDisplayName 'VALUE_FROM_PARTNER_CENTER' -MakeAppxPath 'C:\path\to\WindowsSDK\x64\MakeAppx.exe'
```

Only the resulting non-draft package is for Store submission. Microsoft must certify and sign it before distribution through the Store. There is no purchased or generated signing identity in this repository.

Windows SDK MakeAppx is available in the official Windows SDK or the Microsoft.Windows.SDK.BuildTools NuGet package. The local draft was validated with package version 10.0.26100.4948. `package-store.ps1` verifies that the tool has a valid signature before running it and does not install or execute SonarShift.

## Before the first submission

- Validate the actual account/product identity and public listing details.
- Run Windows App Certification Kit checks and test the installed package on an authorized test system: device switching, shortcuts, sounds, popup, tray, startup, import/export, update retention and uninstall behavior.
- SonarShift switches default audio through `IPolicyConfig`, an undocumented Windows interface. Packaging validation does not establish that Microsoft will accept this behavior under Store policy; describe it accurately in certification notes and resolve any reviewer concerns before publishing.
- Moving from portable SonarShift to Store SonarShift is a separate installation. Export settings to a `.txt` backup first. Test import and single-instance behavior so two installations do not compete for shortcuts.
- For later updates, increase the source version and submit a new package through Partner Center. Store submission automation needs additional account setup and is not enabled by the draft packaging workflow.

## GitHub distribution route

GitHub Actions can build SonarShift, submit a build artifact to a signing provider, and publish the signed executable in GitHub Releases. A trusted signing provider and its approval are still required.

[SignPath Foundation](https://signpath.org/) declined the current application because SonarShift lacks established public visibility. No provider credentials are configured. The public GitHub repository and its checksums do not make its standalone EXE signed. The separate signed-release workflow stops until an approved provider is configured.

Official references:

- [Microsoft code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Smart App Control signing requirements](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
- [StartupTask](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask)
- [Store publisher-name rules](https://learn.microsoft.com/en-us/windows/apps/publish/store-developer-code-of-conduct)
- [Store policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
- [SignPath GitHub integration](https://docs.signpath.io/trusted-build-systems/github)
