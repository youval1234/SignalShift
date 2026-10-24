# Windows Smart App Control and unsigned previews

SignalShift's GitHub preview is unsigned. Smart App Control may block it because
Windows cannot establish publisher trust. A matching SHA-256 hash checks that the
download matches the published bytes; it does not establish that the app is safe.
GitHub hosting and turning a security feature off do not create a trusted signature.

## Before changing a setting

Download only from [this project's releases](https://github.com/youval1234/SignalShift/releases).
Compare the EXE with SHA256SUMS.txt using PowerShell:

```powershell
Get-FileHash .\SignalShift.exe -Algorithm SHA256
```

If you prefer to retain Smart App Control, leave it enabled and do not run this
unsigned preview on that PC. Work/school policies may prohibit changes; ask the
administrator rather than attempting a bypass.

## Optional Windows setting, chosen by the user

1. Open **Windows Security** from Start.
2. Choose **App & browser control → Smart App Control settings**.
3. Read Windows' warning. If you accept the reduced protection for the entire PC,
   choose **Off** and confirm it yourself.
4. Try the downloaded SignalShift EXE again. Other antivirus or organization
   policies can still block it; this is not a universal unblock procedure.

Microsoft's [current FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
says recent Windows updates allow re-enabling Smart App Control through Windows
Security without reinstalling. Availability varies with Windows build/configuration;
follow the warning on your device. Do not assume older systems can turn it back on
without a reset/reinstallation. Re-enabling protection can block SignalShift again.
Microsoft currently documents no per-app Smart App Control exception.

The project owner supplied this external video:
[How to Turn On and Off Smart App Control Without Reinstalling Windows](https://youtu.be/BeYSdePDRtk).
The video's transcript describes changing `VerifiedAndReputablePolicyState` in the
system registry: 0 to turn protection off and 1 to turn it on, followed by a restart.
It also suggests 2 as a fallback, but that is not equivalent to enforcing protection
and is not a project recommendation. This registry method has not been tested by
the project on supported Windows builds; seeing a changed UI alone does not prove
that the intended policy is enforced. The short tutorial above uses Microsoft's
documented Windows interface instead. No registry patches,
security-disabling scripts, exclusions or certificates are installed by the app.

## Responsibility notice

Changing or disabling Windows security features is entirely your decision and
responsibility. You accept the risks and consequences of your changes, including
reduced protection against other software. To the extent permitted by applicable
law, the authors and maintainers disclaim liability for issues, loss or damage
arising from those changes or use of this software. The software is provided
without warranty under the [MIT license](../LICENSE). This notice does not override
rights or obligations that applicable law does not permit to be excluded.
