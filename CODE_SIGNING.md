# Code signing policy

**Status: preparation only. Sonora has not been accepted by SignPath Foundation, no signing credentials are configured, and existing release executables are unsigned.** GitHub hosting, checksums and build provenance do not replace trusted Authenticode signatures. Smart App Control may block current releases.

The proposed arrangement, subject to approval, is: “Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).” This statement describes the intended provider, not an active sponsorship.

## Scope and roles

- Author, reviewer and release approver: repository owner [youval1234](https://github.com/youval1234).
- Contributors submit pull requests; the owner reviews changes before merging.
- Maintainers must use multifactor authentication for GitHub and SignPath before signing is enabled. This document does not attest to account settings that have not been verified.
- Every signing request requires manual release approval in SignPath. No unreviewed pull-request build is eligible for signing.
- Only `Sonora.exe`, built from this repository's tagged source on GitHub-hosted Windows runners, is in scope. We do not sign third-party software with the project's signing credentials.
- The signing workflow is manual, requires configured provider identifiers and credentials, and verifies the returned Authenticode signature before making a signed artifact available. It never silently falls back to an unsigned “signed” release.
- A successful signing run produces a reviewable artifact. Publishing or replacing a GitHub Release asset is a separate owner action.

See the [privacy policy](PRIVACY.md). No local settings, exported backups, diagnostics, signing keys, tokens, or personal build paths belong in source or release artifacts. Release builds omit debug symbols to avoid embedding local debug paths.

## Setup after provider approval

1. Complete the provider's eligibility review, account verification, MFA and terms acceptance.
2. Configure the project for this GitHub repository and GitHub-hosted builds. Use `Signing/SignPath.xml` as the artifact configuration; the unsigned artifact is a ZIP containing only `Sonora.exe`.
3. Configure a production signing policy with manual approval and the Foundation certificate. Restrict it to reviewed version tags from this repository.
4. Add GitHub repository variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG`. Store a submitter-only token as repository secret `SIGNPATH_API_TOKEN`; never paste it in source, logs or issues.
5. Run **Sign Windows release** from the main branch for the approved release tag. Review the request in SignPath. Verify the signed artifact and its fresh checksum, then publish it to the matching GitHub Release.

Until these steps are complete, `.github/workflows/sign-release.yml` must fail closed. Adding the workflow is not evidence that signing works or that Windows will run an unsigned build.
