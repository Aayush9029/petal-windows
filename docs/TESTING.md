# Verification: September 4, 2026

Test machine: Windows x64, AMD Ryzen AI Max+ 395, AMD Radeon 8060S. All model tests used the CPU execution provider. No CUDA or NVIDIA hardware was involved.

## Build and distribution

- Release build: zero warnings and errors.
- Workspace build script: build, checks, self-contained `win-x64` publish and ZIP packaging verified.
- Self-contained app: approximately 186 MB extracted / 87 MB ZIP, excluding separately downloaded models.
- Final packaged executable completed native integration tests without `DOTNET_ROOT` or the SDK in its environment.
- All five settings pages were rendered and visually inspected. Dropdown contrast and model/appearance layout were corrected after inspection. Screenshots are in `artifacts/packaged-check`.

## Core checks

Seven suites cover catalog completeness, immutable hashes/revisions, 16-bit WAV round trips, 48 kHz stereo resampling, edge trimming and silence, history persistence/privacy/deletion, corrupt JSON recovery, preference migration, verified downloads, cache reuse, HTTP Range resume, servers ignoring Range, checksum rejection, and canceled downloads.

## Real speech

The fixture is the public 3.85-second English speech sample from the pinned Parakeet ONNX repository. Its SHA-256 is `148b936b43ce7c546a866e64da059f0458aee2d65e617f16e9d94f06e8d99ed6`.

All four models produced: “Ask not what your country can do for you. Ask what you can do for your country.” Silent PCM produces an empty transcript.

| Model | Initial measured load | Initial measured inference |
|---|---:|---:|
| Whisper Tiny | 2.67 s | 0.38 s |
| Parakeet V3 | 3.89 s | 0.26 s |
| Whisper Small | 3.98 s | 1.30 s |
| Whisper Large V3 Turbo | 3.14 s | 1.34 s |

These are single short-clip observations, not a general benchmark or accuracy evaluation. Load time varies with filesystem cache and concurrent activity; per-job verification, preprocessing and clipboard handling add overhead.

A 69.5-second repeated-speech stress fixture exposed dropped phrases with large contexts. Segmentation was changed to split at sustained quiet boundaries. Whisper Tiny and Parakeet V3 then preserved all 32 expected occurrences of “country.” MP3 and M4A fixtures were encoded and decoded through Windows Media Foundation with duration and non-silent output checks.

## Native integration

`artifacts/packaged-check/integration.txt` records successful checks for:

- Whisper child-process transcription, actual expected output and clipboard copy.
- History and saved audio surviving storage reinitialization.
- Windows SendInput inserting the transcript into a focused, app-owned text field.
- Global recording shortcut registration.
- Default microphone capturing PCM audio; the 0.4-second recording was deleted immediately.
- Parakeet transcription through the complete app pipeline.
- Canceling an actual running native worker, confirming its process exits, returning UI to idle, and deleting temporary files.
- Rendering all settings pages with real saved history.

One earlier run could not activate the test window because Windows denied foreground access, so that run safely skipped input injection. A subsequent full run and the final packaged run both passed paste insertion.

## Remaining release validation

No second physical PC, Windows ARM64 system, or macOS screenshot host was available. DPI/layout resizing, prolonged everyday dictation, diverse accents/languages, microphone unplug/replug, all audio session ducking combinations, third-party elevated-app paste, and accessibility/screen-reader coverage still need broader user testing. Exact macOS screenshot equivalence is unverified; the private screenshots referenced upstream were unavailable. The app is an unsigned first release, not an assertion that every Windows configuration or transcription is error-free.

## Windows UI revision: September 5, 2026

Version 0.2 replaces custom control templates with the Microsoft WPF Fluent theme and native window chrome. Seven core suites pass; Release build has zero warnings/errors. Light and dark rendering covers all five pages, history/tray context menus, recording, hover-to-stop, and processing states. Routed mouse-enter/leave assertions verify the waveform returns after hover. One light render run timed out; the isolated rerun completed successfully.

The native shortcut smoke test verifies Alt + Space registration, a deliberate F8 conflict, preservation of the old registration and preference on failure, release of the old shortcut after switching, and restoration to Alt + Space. This test does not inject keys or start a microphone recording. Real model/audio/paste results above are from the prior engine verification; this UI revision leaves that pipeline unchanged. Interactive desktop inspection was unavailable because the computer-use approval timed out; screenshots were rendered by the app itself.

Updated screenshots: artifacts/windows-Dark and artifacts/windows-Light-final. Shortcut report: artifacts/windows-Shortcut/shortcuts.txt.

The final self-contained package also passed UI rendering and shortcut conflict checks without DOTNET_ROOT. The normal app was reopened with Alt + Space; existing model downloads and history were retained.

## Version 0.3 verification

Eight core suites pass, including shortcut serialization for F5, Home, media keys, modifiers, and less common virtual keys. Reserved keys are rejected. Native checks capture F5, Home, and Volume Mute through the dialog routed event; register F5 and Home with Windows; verify conflicts preserve the existing binding; and verify released bindings can be registered again. Hardware Fn behavior has not been verified.

The UI smoke checks cover five pages, the searchable tray palette (including a no-match filter assertion), native context menus, and waveform/Finish/processing states in light and dark. These are WPF render checks; DWM acrylic is outside WPF rendering and is represented by its theme fallback in the test images. Physical desktop hover/dismissal and the system-composed acrylic appearance still need interactive verification.

Release builds have zero warnings/errors. Hold duration, release polling, and push-to-talk behavior were removed; MOD_NOREPEAT prevents a held shortcut from toggling repeatedly. Existing model inference tests remain documented above.

Signing checks found no code-signing certificate in either Personal certificate store. The timestamped Authenticode signing script passes PowerShell syntax validation; actual signing and trust verification await a publisher certificate and SignTool. The current ZIP remains unsigned.

The final v0.3 self-contained package passed shortcut/dialog and dark UI smoke checks without DOTNET_ROOT. Authenticode reports NotSigned. ZIP SHA-256: 16FF8C7866C42C6C87A82FBCC9E5D1D523EFE3F9F20148F167C8770E0EEFBF38.

## Version 0.3.1 verification

Sidebar items now have an explicit 36-DIP height, with reduced padding. All authored C# UI copy was checked for em dashes and en dashes; none remain. Native Fluent control templates are retained.

Playback now uses the asynchronous Windows WPF MediaPlayer rather than opening and resetting a WaveOut device on the UI thread. The real saved WAV fixture passed muted open, pause, seek, resume, search while playing, navigation cleanup, replay, end-of-audio, and missing-file recovery checks. A 25ms dispatcher heartbeat continued during these operations (41 ticks in this run). Opening audio has a 10-second recovery timeout. This removes the synchronous WaveOut driver path implicated by code inspection; the original user-reported freeze was not reproduced before the change.

Nine core suites pass, including search with whitespace, accents, multiple terms, case, and model/source metadata. Search has a clear action, Ctrl+F in Settings, Escape-to-clear, and delayed history filtering. A matching selected recording continues playing when results refresh. The UI render check passed and the compact sidebar was visually inspected in the light theme.

The final self-contained v0.3.1 package passed playback and dark UI smoke checks without DOTNET_ROOT. ZIP SHA-256: A5507D54FB6E4237C21F91E87647F85F5AAD17F7155E3D509945604F0AA8AC5C.

## Public preview preparation

The History list was compacted to 58-DIP rows and 224-DIP width, with selection inset from rounded panel edges. Fictional demo entries and Windows-generated speech were used for the README screenshots in assets/readme/screenshots. The playback regression check passed after this layout change.

Windows Sandbox was enabled on the development PC, but Windows reported RestartNeeded=true. The generated isolated configuration is XML-valid; execution is pending a host restart. No sandbox pass is claimed. Microsoft Defender was enabled and completed a custom scan of the release directory. Local checks do not confer Microsoft approval.

## Published release checks

The main-branch Windows build completed successfully on GitHub Actions: https://github.com/Aayush9029/petal-windows/actions/runs/33998874327.

The public v0.3.1 ZIP was downloaded through scripts/install.ps1 into an isolated test installation. Its SHA-256 matched both SHA256SUMS.txt and the GitHub asset digest. The installed executable matched the packaged executable. Internet-zone metadata remained present. The downloaded app completed its UI smoke check without an installed SDK or DOTNET_ROOT.

The final release folder completed a Microsoft Defender custom scan with no matching detections. This result is local scan evidence, not a Microsoft approval or a guarantee about future reputation checks. Windows Sandbox execution still requires the pending host restart.
