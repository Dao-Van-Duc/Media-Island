<# Run only on the disposable GitHub Actions Windows runner, never on a user's installed copy. #>
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$')][string]$Version)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP) {
    throw 'Installer smoke tests require a disposable GitHub Actions runner. Do not run on a user machine.'
}
if ([version]$Version -le [version]'0.0.0') { throw 'CI upgrade testing requires a package version above 0.0.0.' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repoRoot 'publish\dist'
$stage = Join-Path $repoRoot 'publish\ci-staging'
$logs = Join-Path $repoRoot 'publish\test-logs'
$oldPackages = Join-Path $repoRoot 'publish\test-packages'
$testDir = Join-Path $env:RUNNER_TEMP ('Media Island smoke ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs,$oldPackages -Force | Out-Null
$setup = Join-Path $dist "MediaIsland-$Version-win-x64-setup.exe"
$registryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B51E168-5226-444F-A9F7-704983BF6C3E}_is1'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupName = 'DynamicIslandMedia'
$expectedCommand = '"' + (Join-Path $testDir 'DynamicIslandMedia.exe') + '"'
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Media Island.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Media Island.lnk'
$settingsFile = Join-Path $env:LOCALAPPDATA 'DynamicIslandMedia\settings.json'

function Assert([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}
function StartupCommand {
    $value = Get-ItemProperty -LiteralPath $runKey -Name $startupName -ErrorAction SilentlyContinue
    if ($null -eq $value) { return $null }
    return $value.$startupName
}
function RunInstaller([string]$Executable, [string]$Label, [string[]]$Extra = @(), [switch]$ExpectBlocked) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        ('/LOG="{0}"' -f (Join-Path $logs "$Label.log"))) + $Extra
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($ExpectBlocked) {
        Assert ($process.ExitCode -ne 0) "$Label unexpectedly continued while the app mutex existed."
    } else {
        Assert ($process.ExitCode -eq 0) "$Label failed with exit code $($process.ExitCode)."
    }
    Write-Host "PASS: $Label (exit $($process.ExitCode))"
}
function AssertInstalled([string]$ExpectedVersion) {
    Assert (Test-Path -LiteralPath $registryKey) 'Per-user uninstall registration is absent.'
    Assert ((Get-ItemProperty -LiteralPath $registryKey).DisplayVersion -eq $ExpectedVersion) 'Installed version mismatch.'
    Assert (Test-Path -LiteralPath $startLink) 'Start Menu shortcut is absent.'
    Assert (Test-Path -LiteralPath $desktopLink) 'Desktop shortcut is absent.'
    $shell = New-Object -ComObject WScript.Shell
    try {
        Assert ($shell.CreateShortcut($startLink).TargetPath -eq (Join-Path $testDir 'DynamicIslandMedia.exe')) 'Shortcut target mismatch.'
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    foreach ($source in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $relative = $source.FullName.Substring($stage.Length).TrimStart([char]'\')
        $installed = Join-Path $testDir $relative
        Assert (Test-Path -LiteralPath $installed) "Missing installed payload file: $relative"
        Assert ((Get-FileHash -LiteralPath $source.FullName).Hash -eq (Get-FileHash -LiteralPath $installed).Hash) "Installed payload mismatch: $relative"
    }
}
function AssertUninstalled([string]$SettingsHash) {
    Assert (!(Test-Path -LiteralPath (Join-Path $testDir 'DynamicIslandMedia.exe'))) 'Application executable remains after uninstall.'
    Assert (!(Test-Path -LiteralPath $registryKey)) 'Uninstall registration remains.'
    Assert (!(Test-Path -LiteralPath $startLink)) 'Start Menu shortcut remains.'
    Assert (!(Test-Path -LiteralPath $desktopLink)) 'Desktop shortcut remains.'
    Assert ((Get-FileHash -LiteralPath $settingsFile).Hash -eq $SettingsHash) 'Uninstall changed/deleted settings.'
}

$expectedArtifacts = @("MediaIsland-$Version-win-x64-portable.zip", "MediaIsland-$Version-win-x64-setup.exe")
$manifest = @(Get-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt'))
Assert ($manifest.Count -eq 2) 'Checksum manifest must describe exactly the current ZIP and installer.'
foreach ($line in $manifest) {
    Assert ($line -match '^([a-fA-F0-9]{64})  (.+)$') 'Malformed checksum manifest entry.'
    $expectedHash = $Matches[1]
    $name = $Matches[2]
    Assert ($name -in $expectedArtifacts) 'Unexpected artifact path in checksum manifest.'
    Assert ((Get-FileHash -LiteralPath (Join-Path $dist $name)).Hash -eq $expectedHash) "Artifact hash mismatch: $name"
}

# An older INSTALLER version uses the same payload: installer upgrade mechanics, not historical code migration.
$compiler = (Get-Command ISCC.exe -ErrorAction Stop).Source
& $compiler '/DAppVersion=0.0.0' "/DPublishDir=$stage" "/DOutputDir=$oldPackages" (Join-Path $PSScriptRoot 'MediaIsland.iss')
Assert ($LASTEXITCODE -eq 0) 'Previous-version fixture installer compilation failed.'
$oldSetup = Join-Path $oldPackages 'MediaIsland-0.0.0-win-x64-setup.exe'
$destination = '/DIR="{0}"' -f $testDir
RunInstaller $oldSetup 'install-previous-version' @($destination, '/TASKS="desktopicon,startupentry"')
AssertInstalled '0.0.0'
Assert ((StartupCommand) -eq $expectedCommand) 'Installer did not register selected startup task.'

New-Item -ItemType Directory -Path (Split-Path -Parent $settingsFile) -Force | Out-Null
Set-Content -LiteralPath $settingsFile -Value '{"IsPositionPinned":false,"Marker":"installer-preservation-test"}' -NoNewline -Encoding UTF8
$settingsHash = (Get-FileHash -LiteralPath $settingsFile).Hash
Remove-ItemProperty -LiteralPath $runKey -Name $startupName
RunInstaller $setup 'upgrade-preserve-preferences' @($destination)
AssertInstalled $Version
Assert ($null -eq (StartupCommand)) 'Upgrade silently re-enabled startup after the user disabled it.'
Assert ((Get-FileHash -LiteralPath $settingsFile).Hash -eq $settingsHash) 'Upgrade modified settings.'
Write-Host 'PASS: version upgrade preserves settings bytes and the current disabled-startup preference.'

$emptyRuntimeRoot = Join-Path $env:RUNNER_TEMP ('empty-dotnet-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $emptyRuntimeRoot | Out-Null
$info = [Diagnostics.ProcessStartInfo]::new((Join-Path $testDir 'DynamicIslandMedia.exe'))
$info.UseShellExecute = $false
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.CreateNoWindow = $true
$info.EnvironmentVariables['DOTNET_ROOT'] = $emptyRuntimeRoot
$info.EnvironmentVariables['DOTNET_ROOT_X64'] = $emptyRuntimeRoot
$info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$app = [Diagnostics.Process]::Start($info)
try {
    Assert ($app.WaitForInputIdle(15000)) 'Application did not become idle during startup.'
    Start-Sleep -Seconds 3
    $app.Refresh()
    Assert (!$app.HasExited) 'Application exited during bundled-runtime startup.'
    Assert $app.Responding 'Application is not responding.'
    Assert ($app.MainWindowTitle -notmatch 'Media Island Error|already running') 'Application displayed a startup error.'
    $clr = @($app.Modules | Where-Object { $_.ModuleName -ieq 'coreclr.dll' })
    Assert ($clr.Count -eq 1) 'Expected exactly one loaded CLR module.'
    Assert ($clr[0].FileName -eq (Join-Path $testDir 'coreclr.dll')) 'Application loaded a global CLR instead of the bundled runtime.'
    Write-Host 'PASS: installed application responds and loads its bundled coreclr.dll with global dotnet roots redirected.'
} finally {
    if (!$app.HasExited) { $app.Kill(); [void]$app.WaitForExit(10000) }
    $app.Dispose()
}

$mutex = [Threading.Mutex]::new($false, 'DynamicIslandMedia_SingleInstance_Mutex')
try {
    RunInstaller $setup 'running-instance-install-blocked' @($destination) -ExpectBlocked
    RunInstaller (Join-Path $testDir 'unins000.exe') 'running-instance-uninstall-blocked' @() -ExpectBlocked
} finally { $mutex.Dispose() }

# Reset this synthetic CI marker after app startup, which is allowed to save its own preferences.
Set-Content -LiteralPath $settingsFile -Value '{"Marker":"uninstall-preservation-test"}' -NoNewline -Encoding UTF8
$settingsHash = (Get-FileHash -LiteralPath $settingsFile).Hash
$foreign = '"C:\Other Portable App\DynamicIslandMedia.exe"'
Set-ItemProperty -LiteralPath $runKey -Name $startupName -Value $foreign
RunInstaller (Join-Path $testDir 'unins000.exe') 'uninstall-preserve-foreign-startup'
AssertUninstalled $settingsHash
Assert ((StartupCommand) -eq $foreign) 'Uninstall deleted a different portable installation startup command.'

RunInstaller $setup 'reinstall-own-startup' @($destination, '/TASKS="desktopicon,startupentry"')
AssertInstalled $Version
Assert ((StartupCommand) -eq $expectedCommand) 'Explicit startup task did not register this installation.'
RunInstaller (Join-Path $testDir 'unins000.exe') 'uninstall-clean-own-startup'
AssertUninstalled $settingsHash
Assert ($null -eq (StartupCommand)) 'Uninstall left this installation startup command behind.'
Write-Host 'PASS: install, older-version upgrade, bundled runtime startup, mutex rejection, both startup cleanup cases, shortcuts and settings preservation.'
Write-Host 'LIMIT: runner has SDK/admin membership; no full Windows 10/11 desktop/media regression or historical application-binary migration was tested.'
