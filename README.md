# 🏝️ Dynamic Island Media for Windows

A lightweight, elegant Windows 11 desktop background application built with **C# (.NET 8, WPF)** that brings the fluid macOS "Dynamic Island" experience to Windows for currently playing media.

---

## ✨ Features

- **Windows Media Transport Controls (SMTC) Integration**:
  - Automatically hooks into `GlobalSystemMediaTransportControlsSessionManager`.
  - Seamlessly detects now-playing tracks, artists, album artwork, and play/pause state from **Spotify, YouTube in Chrome/Edge/Firefox, Apple Music, VLC, Windows Media Player, MusicBee, and more**.
  - Direct media controls: **Previous Track**, **Play/Pause**, **Next Track**, and **Click-to-Seek** progress timeline.
- **Dynamic Island Morphing & Visual Aesthetics**:
  - **Position and pin**: Choose the top or bottom screen edge, adjust the distance (8px by default), drag the island, and pin its position. Expansion direction adapts to the available space.
  - **Compact Pill Mode**: A pill (~240px wide × 38px high) with cropped album art, track metadata, and a decorative spring equalizer that follows playback state and settles to dots when paused. It does not analyze audio levels.
  - **Expanded Island Mode**: An expanded card (~390px wide × 155px high) with rounded album art, track details, a source app badge, playback controls, and a click/drag seek bar.
  - **Ambient background**: A drifting blue/aqua mesh, rendered at device resolution with dithering to reduce dark-gradient banding. Gradient and progress glow can be switched off independently; compact and expanded background opacity have separate controls.
  - **Adaptive Idle Behavior**: Automatically collapses to a subtle idle pill or smoothly fades out completely when no media is active (configurable in Settings).
  - **Windows 11 Fluent Dark Design**: Pure obsidian background (`#0D0D11`), 1px subtle border highlight, 26px rounded corners, and soft elevation drop shadow.
- **Background & System Tray Integration**:
  - Borderless, topmost transparent window anchored above taskbar.
  - Does **not** clutter the Windows Taskbar (`ShowInTaskbar = false`).
  - Sits discreetly in the Windows System Tray with a transparent, bold musical note icon and a custom dark-mode context menu:
    - 🎵 Live playing song title header
    - **Show / Hide Island** (Left-clicking the tray icon also toggles visibility)
    - **Expand / Collapse**
    - **Settings...**
    - **Exit**
- **Settings Dialog**:
  - Top/bottom position and distance from the selected screen edge (0px to 80px, default 8px).
  - Auto-expand on hover with configurable expand/collapse delays.
  - Auto-hide when idle toggle.
  - Launch on Windows startup toggle.

---

## Install without the SDK

See [INSTALL.md](INSTALL.md) for the Windows x64 self-contained installer and portable ZIP, supported
Windows versions, artifact downloads, upgrade/uninstall instructions, and verification limits. End users
do not need Visual Studio or the .NET SDK. Build and installation artifacts are distributed through
GitHub Actions; binaries are not committed to this repository.

## 📁 Project Structure

```text
Media-Island/
├── DynamicIslandMedia.csproj     # Configured for net8.0-windows10.0.19041.0 (WinRT + WPF + WinForms)
├── App.xaml                      # Application definition with single-instance mutex
├── App.xaml.cs                   # App lifecycle, tray initialization, and event wiring
├── MainWindow.xaml               # Dynamic Island UI (Compact Pill & Expanded Island)
├── MainWindow.xaml.cs            # Morphing animations, visualizer waves, dragging, & input
├── SettingsWindow.xaml           # Modern Windows 11 styled settings dialog
├── SettingsWindow.xaml.cs        # Settings code-behind
├── Assets\
│   └── app_icon.ico              # Musical note tray & window icon
├── Models\
│   ├── MediaPlaybackInfo.cs      # Media track, status, artwork, and duration model
│   └── AppSettings.cs            # JSON configuration & Windows startup registry helper
├── Services\
│   ├── WindowsMediaService.cs    # SMTC session listener, media properties & controls
│   └── TrayIconService.cs        # System tray NotifyIcon with dark context menu
├── Helpers\
│   ├── IconHelper.cs             # Asset loader & procedural music note generator fallback
│   └── RelayCommand.cs           # Standard MVVM command implementation
└── README.md
```

---

## 🚀 Building & Running

### Prerequisites
- Windows 10 (version 2004 / build 19041 or higher) or Windows 11.
- .NET 8 SDK (`net8.0-windows10.0.19041.0`).

### Build & Run
Run from PowerShell in the project directory:

```powershell
# Restore and build Debug
dotnet build

# Or build optimized Release
dotnet build -c Release

# Run the application
dotnet run -c Release
```

*Note: If `dotnet` is not yet globally in your environment path, use the direct local SDK path:*
```powershell
& "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" run -c Release
```

Once launched:
- The island appears at the configured screen edge (bottom by default), subject to media/idle visibility settings.
- An icon will appear in the Windows System Tray (near the taskbar clock).

---

## 🎮 How to Interact with the Island

| Action | Result |
| :--- | :--- |
| **Hover mouse over pill** | Automatically expands the island (can be toggled in Settings). |
| **Click on compact pill** | Instantly expands to full controls. |
| **Mouse leaves expanded island** | Collapses after the configured delay (350ms by default). |
| **Click Pin button** | Locks the island's position until unpinned. |
| **Click ⌃ Collapse button** | Collapses back to compact pill immediately. |
| **Click or drag on timeline bar** | Previews the selected position and commits the seek on release. |
| **Click Play / Pause / Prev / Next** | Controls the active player (Spotify, browser, etc.). |
| **Right-click on island** | Toggles between compact and expanded view. |
| **Click system tray icon** | Toggles show / hide visibility of the island. |
| **Right-click tray icon** | Opens dark context menu with Island controls, Settings, and Exit. |

---

## 🎨 Customizing the App Icon

The application is pre-configured to look for `Assets/app_icon.ico`:
1. Drop your custom `.ico` file into `Assets/app_icon.ico` (replacing the existing file).
2. Rebuild the project: `dotnet build`.
3. If you ever delete or rename the file, the built-in procedural generator in `IconHelper.cs` will seamlessly supply a clean vector music note icon so the application never crashes.
