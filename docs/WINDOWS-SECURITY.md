# Windows security and distribution

Petal runs as a standard desktop app. It does not request administrator rights, install a driver, add antivirus exclusions, or change SmartScreen settings. The PowerShell installer verifies release checksums and preserves Internet-zone metadata for Windows reputation checks.

## Current status

The preview is unsigned and has not been reviewed by Microsoft. Local testing and Defender scanning are not Microsoft certification. A new unsigned download can trigger SmartScreen or Smart App Control.

Trusted Authenticode signing requires a publisher certificate or a verified signing-service account. `scripts/sign.ps1` supports timestamped SHA-256 signatures and checks trust before packaging. No self-signed root is installed. Microsoft Store submission still requires a Partner Center identity and review.

MSIX full-trust packaging is not the same as an AppContainer sandbox. Petal uses a global shortcut and pastes into other desktop apps; those integrations must be evaluated before an AppContainer conversion. The app is not advertised as AppContainer-isolated.

## Windows Sandbox

Enable the Windows Sandbox optional feature on a supported Windows Pro/Enterprise/Education system. A restart may be needed. Then:

```powershell
./scripts/test-sandbox.ps1 -Launch
```

The generated configuration disables networking, GPU sharing, microphone, camera, printer, and clipboard redirection. The release folder is read-only. Only the dedicated `artifacts/sandbox/results` folder is writable from the sandbox. The UI smoke test creates fresh data inside the disposable environment and writes screenshots to that results folder.

This checks startup and rendering without installed SDKs or network access. It does not test microphone capture or model downloads because those are deliberately unavailable in this configuration. It does not isolate the normal installed app.

## Release checks

- Run the core tests and the packaged UI/playback checks.
- Run the Windows Sandbox startup check when available.
- Scan the exact packaged files with up-to-date Microsoft Defender.
- Publish SHA256SUMS.txt beside the ZIP.
- Keep dependencies locked and review updates.
- For public trust, complete verified signing or Microsoft Store review.

[Windows Sandbox configuration](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file) · [SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) · [Store distribution](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store)

On the development PC, enabling Windows Sandbox reported that a restart is required. The sandbox run is pending that restart. The normal host build, UI and playback checks have passed; see TESTING.md for their scope.
