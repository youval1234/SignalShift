# Windows signing preparation

The portable 1.3.0 release is unsigned. A GitHub release, SHA-256 checksum, or GitHub artifact attestation is not a Windows Authenticode signature and does not resolve Smart App Control's publisher check.

## Microsoft Store route

Microsoft signs MSIX submissions after certification. The repository includes `package-store.ps1` and an MSIX manifest. Store builds use the Windows StartupTask API instead of the portable build's Run registry entry. Startup remains opt-in; Windows user/policy overrides are respected. Settings import/export and the existing settings location are retained.

For a packaging validation build:

```powershell
./package-store.ps1 -Draft -MakeAppxPath 'C:\path\to\WindowsSDK\x64\MakeAppx.exe'
```

The draft identity is deliberately marked as unverified. Do not install or submit the draft. A successful MakeAppx build validates package structure, not Store approval or runtime compatibility. Runtime testing on the protected development computer is pending trusted signing; do not disable protection or install a local root certificate to bypass that requirement.

After the owner completes Microsoft developer enrollment and reserves an app, copy the exact values from Partner Center > Product identity:

```powershell
./package-store.ps1 -IdentityName 'VALUE_FROM_PARTNER_CENTER' -Publisher 'CN=VALUE_FROM_PARTNER_CENTER' -PublisherDisplayName 'VALUE_FROM_PARTNER_CENTER' -MakeAppxPath 'C:\path\to\WindowsSDK\x64\MakeAppx.exe'
```

Only the resulting non-draft package is for Store submission. Microsoft must certify and sign it before distribution through the Store. There is no purchased or generated signing identity in this repository.

Windows SDK MakeAppx is available in the official Windows SDK or the Microsoft.Windows.SDK.BuildTools NuGet package. The local draft was validated with package version 10.0.26100.4948. `package-store.ps1` verifies that the tool has a valid signature before running it and does not install or execute Sonora.

## GitHub distribution route

GitHub Actions can build Sonora, submit a build artifact to a signing provider, and publish the signed executable in GitHub Releases. A trusted signing provider and its approval are still required.

[SignPath Foundation](https://signpath.org/) offers free signing to qualifying open-source projects. [Conditions](https://signpath.org/terms) include an OSI-approved license, verifiable build provenance, maintainer MFA, a signing/privacy policy, and manual release approval. They also assess project reputation and can decline applications. This one-day-old private repository has not been approved. Changing repository visibility is an owner decision and must not be done implicitly.

No signing provider credentials are configured, no SignPath application has been submitted, and no build is represented as signed. Keep the repository private until its owner explicitly approves publishing its source and history. Paid signing with a trusted provider can also be integrated with GitHub without changing repository visibility.

Official references:

- [Microsoft code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Smart App Control signing requirements](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
- [StartupTask](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask)
- [SignPath GitHub integration](https://docs.signpath.io/trusted-build-systems/github)
