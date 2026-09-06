# Development

Use Windows x64 and the .NET 10 SDK selected by `global.json`.

```powershell
./scripts/build.ps1 -Check -Package
```

The output is `artifacts/Petal-win-x64.zip` and `artifacts/SHA256SUMS.txt`. Keep the executable and DLLs together. Models, recordings, SDK caches, and build output are excluded from Git.

## Code layout

- `src/Petal.Core`: model downloads, offline inference, audio conversion, storage, shortcuts, and search.
- `src/Petal.Windows`: WPF interface, microphone, playback, tray, global shortcuts, and clipboard integration.
- `src/Petal.Checks`: repeatable core checks and optional real-model tests.

Transcription runs in a child process using the same executable. Canceling stops that process, and finishing releases the model's memory. Model files are pinned by revision and SHA-256 in `src/Petal.Core/models.json`.

## Tests

The build's `-Check` option runs deterministic checks without downloading models. Real-model and long-audio checks need Internet access for the initial downloads (about 2.2 GB):

```powershell
dotnet run --project src/Petal.Checks -c Release -- models test-data whisper-tiny parakeet-v3 whisper-small whisper-turbo
dotnet run --project src/Petal.Checks -c Release -- stress test-data
```

The packaged app also provides these test modes. Each takes `DATA_DIR OUTPUT_DIR`; use a separate data directory, never your personal Petal data.

| Mode | Checks |
| --- | --- |
| `--ui-smoke` | Settings pages, menus, history filtering, recording-bar states |
| `--shortcut-smoke` | Key capture, registration, conflicts, and replacement |
| `--integration-smoke` | Worker inference, clipboard insertion, microphone capture, storage, cancellation |
| `--playback-smoke` | Play, pause, seek, navigation, search, and error recovery |

Integration checks need the `speech.wav`, Whisper Tiny, and Parakeet fixtures from the real-model checks. They briefly record and delete microphone audio, and paste only into their own test window. Run them before the playback checks to create fixture history.

## Release tools

| Script | Purpose |
| --- | --- |
| `build.ps1` | Build, check, package, and generate checksums |
| `install.ps1` | Download, verify, and install a public release |
| `sign.ps1` | Sign with an existing publisher certificate |

See [Windows security](WINDOWS-SECURITY.md) for signing and isolated testing. Normal user data is under `%LOCALAPPDATA%/Petal`; it survives app updates. Use Advanced to manage saved recordings.

## Updating dependencies

Use the solution to update all three lockfiles together:

```powershell
dotnet restore Petal.sln --force-evaluate
./scripts/build.ps1 -Check -Package
dotnet list Petal.sln package --outdated --include-transitive
dotnet list Petal.sln package --vulnerable --include-transitive
```

Dependabot groups weekly NuGet updates and GitHub Actions updates. Major audio updates also need the real-model, compressed-audio, microphone, and playback checks above. Keep the published sherpa-onnx NuGet package and its native libraries together; a newer upstream C++ release may precede its .NET package.
