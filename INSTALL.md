# Media Island — Installation & Packaging Guide

Media Island is distributed as a self-contained, 64-bit Windows desktop application. All required .NET 8 Windows Desktop runtime and native libraries are bundled directly into the distribution packages.

---

## Distribution Packages

| Package | Format | Purpose |
|---|---|---|
| **Setup Installer** | `MediaIsland-<version>-win-x64-setup.exe` | Standard per-user installation (installs to `%LocalAppData%\Programs`, creates Start Menu shortcut, registers uninstaller). |
| **Portable Edition** | `MediaIsland-<version>-win-x64-portable.zip` | Standalone archive for manual extraction and portable execution without installation. |

Both packages are fully self-contained. **No pre-installed .NET runtime is required.**

---

## System Requirements & Technical Boundaries

- **Operating System:** Windows 10 (version 2004 / Build 19041 or higher) or Windows 11.
- **Architecture:** 64-bit x64 (AMD64 / Intel 64).
  *Note:* Native ARM64 is neither built nor tested. Running under emulation on ARM64 devices is untested.
- **Privileges:** Standard user account. No administrative privileges (UAC elevation) are required.
- **Runtime Lifecycle Notice:** The application targets .NET 8 (`net8.0-windows10.0.19041.0`). In accordance with Microsoft's support policy, .NET 8 standard support concluded on 2026-11-10. This package bundles a self-contained copy of the .NET 8.0.x runtime; future repository maintenance may evaluate migrating to a subsequent LTS framework.
- **Testing Limits:** Automated CI validates compilation, self-contained packaging, and installer life cycle on disposable Windows runners. Live media session detection (SMTC) and interactive window animations require execution within an active desktop user session with playing media.

---

## Obtaining Packages

Distribution packages are generated as build artifacts via the repository's GitHub Actions workflow:

1. Visit the repository: [truongsonphan187-cmd/Media-Island](https://github.com/truongsonphan187-cmd/Media-Island).
2. Navigate to the **Actions** tab and select the latest successful run of the **Package & Distribution** workflow.
3. Under **Artifacts**, download `MediaIsland-win-x64-packages.zip`.
4. Extract the artifact archive to obtain `MediaIsland-<version>-win-x64-setup.exe`, `MediaIsland-<version>-win-x64-portable.zip`, and `SHA256SUMS.txt`.

For an unmerged pull request, use the successful **Actions run linked in that PR**; packages may be on
the contributor's fork until the maintainer merges the workflow. GitHub requires sign-in to download
Actions artifacts. Artifacts expire after 30 days; rebuild the recorded commit if they have expired.

Alternatively, build packages locally following the [Building from Source](#building-packages-from-source) section below.

---

## 1. Setup Installer Guide (Recommended)

### Installation Steps

1. Verify file integrity using PowerShell and `SHA256SUMS.txt`:
   ```powershell
   Get-FileHash .\MediaIsland-*-win-x64-setup.exe -Algorithm SHA256
   ```
2. Run `MediaIsland-<version>-win-x64-setup.exe`.
3. Follow the setup wizard:
   - Application files are installed into `%LocalAppData%\Programs\Media Island`.
   - A Start Menu shortcut is created (`Media Island`).
   - You may optionally select tasks to create a desktop shortcut or register automatic startup with Windows.
4. Complete the wizard.

### Windows SmartScreen Notice (Unsigned Binaries)

Because binaries built from this repository are not digitally signed with a commercial code-signing certificate, Windows Defender SmartScreen may display an unrecognized app notice:

> *"Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting."*

Check the package source and SHA-256 **before** running it. The workflow produces unsigned binaries;
a matching checksum checks file integrity, not a code-signing identity. If you trust the verified build,
Windows may offer **More info** and **Run anyway**. No system-wide SmartScreen change is required.

---

## 2. Portable Edition Guide

For running without installation:

1. Download and extract `MediaIsland-<version>-win-x64-portable.zip` into any folder (e.g., `D:\Tools\MediaIsland`).
2. Run `DynamicIslandMedia.exe`.
3. Access controls and settings via the musical note icon in the Windows System Tray.

*Note on Startup:* If "Launch on Windows startup" is toggled in Settings, the application registers its current directory path under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. If you move the portable folder, launch the app from its new location and toggle the setting to update the registry path.

---

## 3. Upgrades and Running Processes

- **Graceful Process Detection:** If Media Island is currently running, the installer will detect the active instance via the application mutex (`DynamicIslandMedia_SingleInstance_Mutex`) and request that you close Media Island via the system tray menu (`Exit`) before proceeding. The installer does not forcibly terminate running processes.
- **Configuration Preservation:** Application settings and custom coordinates are stored separately in `%LocalAppData%\DynamicIslandMedia\settings.json`. Running an installer upgrade preserves this configuration file.
- **Startup Preference:** Upgrading does not restore an old startup task after you disabled startup in
  the application. Selecting the optional startup task explicitly enables it; use the app's Settings to
  disable an existing startup entry. A portable installation's different startup command is preserved on uninstall.

---

## 4. Uninstallation

1. Close Media Island via system tray > **Exit**.
2. Open Windows **Settings** > **Apps** > **Installed apps** (or **Apps & features**).
3. Locate **Media Island** and select **Uninstall**.
4. The uninstaller removes:
   - Application binaries in `%LocalAppData%\Programs\Media Island`.
   - Start Menu and desktop shortcuts.
   - The startup registry entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` *only if it points to this installation's executable*. Any startup entry pointing to a different portable path is left untouched.

*Optional full cleanup:* To delete retained user configuration:
```powershell
Remove-Item -Recurse -Force "$env:LocalAppData\DynamicIslandMedia"
```

---

## 5. Building Packages from Source

### Prerequisites

- Windows with the .NET 8 SDK and Windows PowerShell 5.1 or PowerShell 7. No additional .NET workload is required.
- *(Optional for setup EXE)* Inno Setup 6 (`ISCC.exe`). If Inno Setup is not installed locally, the script automatically produces the portable ZIP archive and skips the installer executable.

### Execution

Run the packaging script from the repository root:

```powershell
.\tools\Build-Package.ps1 -Configuration Release -Runtime win-x64 -Version "1.0.0"
```

Output packages and `SHA256SUMS.txt` are written to `publish\dist\`.

If the default PowerShell execution policy blocks local scripts, this process-only command can run a
locally trusted checkout without changing the machine/user policy (organization policies still apply):

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File .\tools\Build-Package.ps1 -Version 1.0.0
```

Versions use three or four numeric components, each at most 65535. The builder uses a fresh staging
directory under `publish`; an explicit staging path must be new/empty. Installer verification runs only
on a disposable GitHub runner and is guarded against accidental use on a normal user machine.

The runner checks an older installer version (using the same payload) upgrading to this version, payload
hashes, shortcuts, settings/startup preservation, bundled-runtime startup and uninstall. It already has an
SDK and an account with administrator membership; redirecting dotnet roots and checking the loaded local
CLR verifies the bundled runtime without removing the runner's SDK. This is not a clean Windows 10/11
standard-user desktop/media regression or a historical application-data migration test. See
[docs/VERIFICATION.md](docs/VERIFICATION.md) for actual results and remaining checks.
