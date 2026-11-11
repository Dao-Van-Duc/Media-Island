# Verification for the Windows x64 handoff

This contribution combines the completed appearance/interaction work with reproducible self-contained
distribution packaging. Final appearance and motion were manually reviewed and accepted by the contributor.
The checks below distinguish source/build validation from installation and interactive media testing.

## Product and integration checks

- Integrated against upstream `2ddefe0` (`fix idle GPU usage`). The accepted fixed blue/aqua ambient mesh
  replaces the previous ambient-water UI. Upstream album-color extraction, model fields and view-model
  notifications are retained; media refresh retains the stale-session guard.
- Fresh Release build with `--no-incremental`: 0 errors, 0 warnings.
- An ad hoc WPF harness referenced the compiled application without constructing an Application or
  MainWindow, creating a tray, opening media sessions, or changing user settings/registry:
  - 24 ambient frames at 100%, 125%, 150% and 200% DPI; 3,166,554 pixels checked for premultiplied BGRA,
    transparent upper rows and changing/nonempty output.
  - Same-size reuse, DPI-only bitmap recreation, and 1x1 size clamping passed.
  - 40,000 equalizer target samples remained finite and within the intended amplitude range.
  - A 2:1 album image in a square AlbumCover preserved its aspect ratio with centered cropping and a
    rounded clip.
- Reviewed composition-event hook/unhook and restart paths for expansion/collapse, visibility,
  fullscreen, gradient settings and window close. These are source-level checks, not a full interactive
  regression test.

## Packaging checks

- Windows PowerShell 5.1 parsed both build/installer-test scripts and produced the self-contained portable
  ZIP successfully. The locally inspected package contained 466 files and bundled .NET Core and Windows
  Desktop runtime 8.0.33; the CI build's actual runtime version is determined by its installed SDK.
- ZIP CRC, SHA-256 manifest, AMD64 executable header, self-contained runtime metadata, WPF/WinRT/native
  files and icon passed inspection. No source, PDB, scripts, saved user settings or traversal entries were
  present in the portable payload.
- Eight rejection/host-guard cases passed: unsupported RID, invalid/oversized versions, unsafe output
  boundaries, overlapping output/staging, nonempty staging and accidental host installer-test execution.
  Existing test data remained unchanged. A real junction ancestor was rejected with a nonexistent requested
  child; its external test target was untouched.
- `package.yml` builds the EXE using the runner's preinstalled Inno Setup and calls
  `packaging/Test-Installer.ps1` on a disposable Windows Server 2022 runner. It checks artifact hashes,
  install payload hashes, shortcuts/registration, upgrade from older installer metadata, settings and
  current startup preference preservation, bundled CLR startup with global dotnet roots redirected,
  running-instance mutex rejection, foreign/owned startup entries and uninstall cleanup. The successful
  run and installer logs are linked from the PR; inspect that run for the actual CI result.
- The older installer fixture reuses the current payload; it tests installer-version upgrade mechanics,
  not migration from a historical application binary. The runner already has an SDK and administrator
  membership. The loaded local CLR check verifies the runtime used without removing the runner's SDK;
  a clean standard-user Windows 10/11 desktop remains a separate manual check.
- No installer was run over the contributor's existing app. Build success alone is not an installation test.

## Remaining limitations

- End-to-end Windows 10/11 media-player/browser interaction, scrubbing, Snipping Tool, Alt+Tab,
  fullscreen transitions and multi-monitor DPI require interactive regression testing.
- NativeAcrylicBackdrop is an unwired experimental helper, with no active Settings toggle. Native acrylic
  behavior has not been validated and is not advertised as a feature of the current installer.
- Decorative equalizer bars follow playback state; they do not measure audio levels.
- The previously observed baseline CPU usage has not been diagnosed in this handoff. Do not interpret
  renderer microbenchmarks as an overall application performance guarantee.
- Browser session selection / First Playing Wins remains a known deferred issue, with no claim that this
  contribution resolves it. The broader microanimation audit and reduced-motion support are also deferred.
- Only Windows x64 packages are produced. Native ARM64 and x86 are not verified.
- Packages are unsigned. Checksums identify the downloaded build but do not provide a code-signing identity.
- The existing .NET 8 target reached end of support on 2026-11-10. A supported-framework migration is
  a maintainer follow-up, outside this handoff; self-contained distribution does not extend runtime support.
  See [Microsoft's support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
