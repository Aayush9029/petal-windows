# Porting decisions

## Architecture

WPF supplies vector-based, DPI-aware native desktop UI and direct Windows integration without an embedded Chromium engine. .NET 10 is published self-contained so end users need no SDK/runtime installation. C# is used for the UI and orchestration; inference runs in sherpa-onnx's native C++/ONNX Runtime libraries. A Rust layer would not improve the inference engine itself and would add another binding/toolchain here.

`Petal.Core` owns the pinned model catalog, verified/resumable downloads, PCM normalization, chunked offline recognition, and atomic JSON persistence. `Petal.Windows` owns UI, microphone sessions, global shortcuts, clipboard/SendInput, tray, and process lifecycle. `Petal.Checks` is an executable test suite with fake HTTP transports plus explicit real-model/stress modes.

Each job launches the same executable in `--worker` mode with structured arguments. This isolates native inference, allows the parent to kill canceled work, and releases model memory after completion. The UI verifies hashes before launching the worker. Audio never leaves the computer. The price is cold model loading on each recording; persistent warm workers are a potential later optimization.

## Upstream mapping

| macOS surface | Windows implementation |
|---|---|
| SettingsView / NavigationSplitView | WPF Fluent navigation, five pages, standard resizable Windows window, 1000 × 740 default |
| GeneralPane | Global shortcut, custom key capture, tap-to-toggle recording, automatic insertion and clipboard restoration |
| RecordingPane | Input device, edge silence trimming, audio ducking, Windows microphone permissions |
| TranscriptionPane / ModelSelectorCard | Parakeet V3 and Whisper Small/Turbo/Tiny cards, downloads, pause/resume, selection, deletion |
| HistoryPane | Local recordings, search, selectable text, copy/export, playback, reprocess, deletion |
| AdvancedPane | Retention modes, local folders, remove audio/reset history, attribution |
| MenuBarPopover | Searchable acrylic tray palette on left-click; native command menu on right-click |
| FloatingCapsuleView | Non-activating bottom capsule with live microphone level, processing motion, text-only hover-to-finish, context-menu cancel, copied/pasted feedback |
| Keyboard/Accessibility clients | RegisterHotKey with no repeat, foreground-aware SendInput, clipboard fallback |
| CoreML/MLX transcription | INT8 ONNX CPU engines, compatible with AMD/Intel/NVIDIA-equipped x64 PCs |

## Deliberate first-release differences

- No Apple-only engines or enhancement APIs. Qwen/Voxtral need separately validated Windows engines and weights.
- No claim that Windows CPU model speed matches upstream Apple Silicon rating meters.
- Only Parakeet V3 is exposed initially; other Parakeet variants can be added after testing compatible ONNX exports.
- No auto speed-up or lossy history compression. Original-speed 16 kHz PCM is retained for reproducible transcription.
- Built-in WPF Fluent controls follow system light/dark appearance; standard Windows window chrome and Segoe UI. No glass/opaque switch.
- No Sparkle updater, automatic launch registration, or code signing. These need Windows release infrastructure.
- Input to elevated apps can be restricted by Windows. The app checks foreground ownership and leaves text on the clipboard when focus cannot be restored.

Rendered Windows screenshots and test reports are generated in `artifacts`. Exact same-state macOS image comparison remains unavailable because the upstream reference screenshots are private local paths, not repository assets.
