<p align="center">
  <img src="assets/readme/petal-icon.png" alt="Petal app icon" width="120" height="120">
  <h1 align="center">Petal for Windows</h1>
</p>

<p align="center">Fast, local speech to text. A native Windows app with Whisper and Parakeet.</p>

<p align="center">
  <a href="https://github.com/Aayush9029/petal-windows/releases/latest"><img alt="Download for Windows" src="assets/readme/download-windows.svg"></a>
  <a href="https://github.com/Aayush9029/petal"><img alt="Petal for macOS" src="https://img.shields.io/badge/Petal%20for%20macOS-black?style=for-the-badge&amp;logo=apple"></a>
</p>

<img src="assets/readme/screenshots/history.png" alt="Petal history with searchable transcripts and audio playback" width="100%">

Record from any app with a shortcut you choose. Press it again to finish, and Petal pastes the text where you were typing. Your audio stays on your computer.

## Get started

[Download the latest release](https://github.com/Aayush9029/petal-windows/releases/latest), extract the ZIP, and open `Petal.exe`. Or run the [PowerShell installer](scripts/install.ps1):

```powershell
Invoke-WebRequest https://github.com/Aayush9029/petal-windows/releases/latest/download/install.ps1 -OutFile install.ps1
.\install.ps1
```

Windows 10 22H2 or Windows 11, x64. [Unsigned preview](docs/WINDOWS-SECURITY.md).

## Screenshots

| Your shortcut | Local models |
| --- | --- |
| ![Shortcut settings](assets/readme/screenshots/general.png) | ![Model downloads](assets/readme/screenshots/models.png) |

[Additional screenshots](assets/readme/screenshots/README.md).

[Build and contribute](docs/DEVELOPMENT.md) · [Windows security](docs/WINDOWS-SECURITY.md)

[MIT license](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)
