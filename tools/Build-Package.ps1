<#
.SYNOPSIS
    Builds self-contained Windows x64 distribution packages for Media Island.

.DESCRIPTION
    Compiles a self-contained, untrimmed win-x64 publish directory for Media Island,
    generates a portable distribution ZIP, compiles an Inno Setup per-user installer
    (if ISCC.exe is available), and calculates SHA-256 checksums for current-run artifacts.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Release.

.PARAMETER Runtime
    Target runtime identifier. Restricted to win-x64.

.PARAMETER Version
    Numeric package version (three or four components, e.g. 1.0.0).

.PARAMETER PublishDir
    Custom staging directory for dotnet publish output. Must reside within <RepoRoot>\publish.

.PARAMETER OutputDir
    Custom output directory for final packages. Must reside within <RepoRoot>\publish.

.PARAMETER SkipInstaller
    Skip Inno Setup installer compilation even if ISCC.exe is available.
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [string]$Version = "1.0.0",

    [string]$PublishDir = "",

    [string]$OutputDir = "",

    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 1. Validate Version syntax
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$') {
    throw "Invalid version '$Version'. Use three or four numeric components, such as 1.0.0."
}
$numericVersion = [version]$Version
foreach ($part in @($numericVersion.Major, $numericVersion.Minor, $numericVersion.Build, $numericVersion.Revision)) {
    if ($part -gt 65535) { throw "Version components must be no greater than 65535." }
}

# 2. Determine and validate repository root
$RepoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPath = Join-Path $RepoRoot "DynamicIslandMedia.csproj"
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "DynamicIslandMedia.csproj not found at expected repository root: $RepoRoot"
}

# 3. Path boundaries: enforce strict publish root boundary with trailing separator
$PublishRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "publish"))
# String.TrimEnd works in both Windows PowerShell 5.1 (.NET Framework) and PowerShell 7.
function Add-DirectorySeparator([string]$Value) {
    return $Value.TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
}
$PublishRootWithSep = Add-DirectorySeparator $PublishRoot

# Default staging uses a fresh unique GUID directory to avoid dangerous recursive cleanup
if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $PublishRoot ("stage-" + [Guid]::NewGuid().ToString("N"))
} else {
    $PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PublishRoot "dist"
} else {
    $OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}

# Boundary validation: ensure path is inside $PublishRoot and no ancestor is a reparse point
function Assert-SafePublishPath([string]$Path, [string]$ParamName) {
    $normalized = [System.IO.Path]::GetFullPath($Path)
    $normalizedWithSep = Add-DirectorySeparator $normalized

    if (-not $normalizedWithSep.StartsWith($PublishRootWithSep, [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals($PublishRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Security validation failed: $ParamName ('$Path') must be a child subdirectory within '$PublishRootWithSep'."
    }

    # Inspect all existing ancestors up to repository root for junctions / reparse points
    $curr = [System.IO.DirectoryInfo]::new($normalized)
    while ($null -ne $curr) {
        if ($curr.Exists -and $curr.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
            throw "Security validation failed: Reparse point / junction detected at ancestor '$($curr.FullName)' for $ParamName."
        }
        if ($curr.FullName.Equals($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $curr = $curr.Parent
    }
}

Assert-SafePublishPath -Path $PublishDir -ParamName "PublishDir"
Assert-SafePublishPath -Path $OutputDir -ParamName "OutputDir"

# Disjointness check: PublishDir and OutputDir must not be identical or nested
$publishNorm = Add-DirectorySeparator $PublishDir
$outputNorm = Add-DirectorySeparator $OutputDir

if ($publishNorm.Equals($outputNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
    $publishNorm.StartsWith($outputNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
    $outputNorm.StartsWith($publishNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Security validation failed: PublishDir and OutputDir must be completely disjoint directories."
}
if ((Test-Path -LiteralPath $PublishDir) -and
    (!(Test-Path -LiteralPath $PublishDir -PathType Container) -or
     @(Get-ChildItem -LiteralPath $PublishDir -Force).Count -ne 0)) {
    throw "PublishDir must be new or empty. Existing files are never removed or mixed into a new package."
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Media Island — Build & Package Distribution" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Configuration : $Configuration"
Write-Host "Runtime       : $Runtime"
Write-Host "Version       : $Version"
Write-Host "Publish Dir   : $PublishDir"
Write-Host "Output Dir    : $OutputDir"
Write-Host "=========================================================="

# 4. Check dotnet CLI
$dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    throw "dotnet CLI is not found in PATH. Please install .NET 8 SDK."
}
Write-Host "[1/5] Dotnet SDK verified: $(& dotnet --version)" -ForegroundColor Green

# 5. Prepare fresh isolated staging and output directories (no blind recursive deletes on user paths)
if (-not (Test-Path -LiteralPath $PublishRoot)) {
    New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
}
if (-not (Test-Path -LiteralPath $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
}
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$buildStartTimeUtc = [DateTime]::UtcNow.AddSeconds(-2)

# 6. Publish self-contained, untrimmed, multi-file application
Write-Host "[3/5] Publishing self-contained $Runtime binaries..." -ForegroundColor Yellow
$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:UseAppHost=true",
    "-p:Version=$Version",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:PublishReadyToRun=false",
    "-o", $PublishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# 7. Strictly verify current-run output files
$mainExe = Join-Path $PublishDir "DynamicIslandMedia.exe"
if (-not (Test-Path -LiteralPath $mainExe)) {
    throw "Published executable DynamicIslandMedia.exe is missing from $PublishDir."
}
$mainExeItem = Get-Item -LiteralPath $mainExe
if ($mainExeItem.CreationTimeUtc -lt $buildStartTimeUtc -and $mainExeItem.LastWriteTimeUtc -lt $buildStartTimeUtc) {
    throw "Freshness check failed: DynamicIslandMedia.exe was not created or copied in the current publish run."
}

$mainDll = Join-Path $PublishDir "DynamicIslandMedia.dll"
if (-not (Test-Path -LiteralPath $mainDll)) {
    throw "Published library DynamicIslandMedia.dll is missing from $PublishDir."
}
foreach ($required in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'wpfgfx_cor3.dll',
        'PresentationFramework.dll', 'System.Windows.Forms.dll', 'Microsoft.Windows.SDK.NET.dll',
        'WinRT.Runtime.dll', 'DynamicIslandMedia.deps.json', 'DynamicIslandMedia.runtimeconfig.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $PublishDir $required) -PathType Leaf)) {
        throw "Required self-contained dependency missing: $required"
    }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $PublishDir 'DynamicIslandMedia.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'includedFrameworks') -or
    !(@($runtimeConfig.runtimeOptions.includedFrameworks.name) -contains 'Microsoft.NETCore.App') -or
    !(@($runtimeConfig.runtimeOptions.includedFrameworks.name) -contains 'Microsoft.WindowsDesktop.App')) {
    throw 'Runtime configuration is not a self-contained Core + Windows Desktop deployment.'
}

$assetIcon = Join-Path $PublishDir "Assets\app_icon.ico"
if (-not (Test-Path -LiteralPath $assetIcon)) {
    Write-Warning "Assets\app_icon.ico was not copied by publish. Ensuring icon in staging..."
    $repoIcon = Join-Path $RepoRoot "Assets\app_icon.ico"
    if (Test-Path -LiteralPath $repoIcon) {
        $publishAssets = Join-Path $PublishDir "Assets"
        if (-not (Test-Path -LiteralPath $publishAssets)) {
            New-Item -ItemType Directory -Path $publishAssets -Force | Out-Null
        }
        Copy-Item -LiteralPath $repoIcon -Destination (Join-Path $publishAssets "app_icon.ico") -Force
    }
}
if (!(Test-Path -LiteralPath $assetIcon -PathType Leaf)) { throw 'Required application icon is missing.' }
Write-Host "[3/5] Published and verified fresh output in $PublishDir" -ForegroundColor Green

# 8. Generate Portable ZIP
Write-Host "[4/5] Creating portable distribution ZIP..." -ForegroundColor Yellow
$zipName = "MediaIsland-$Version-$Runtime-portable.zip"
$zipPath = Join-Path $OutputDir $zipName
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
$zipItem = Get-Item -LiteralPath $zipPath
Write-Host "Created portable archive: $zipName ($([math]::Round($zipItem.Length / 1MB, 2)) MB)" -ForegroundColor Green

$currentRunArtifacts = @($zipPath)

# 9. Inno Setup per-user installer
Write-Host "[5/5] Checking Inno Setup compiler (ISCC.exe)..." -ForegroundColor Yellow
$isccPath = $null
if (-not $SkipInstaller) {
    $isccCmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($isccCmd) {
        $isccPath = $isccCmd.Source
    } else {
        $candidatePaths = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
            "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
        )
        foreach ($cp in $candidatePaths) {
            if (Test-Path -LiteralPath $cp) {
                $isccPath = $cp
                break
            }
        }
    }
}

if ($isccPath) {
    Write-Host "Compiling Inno Setup installer using: $isccPath" -ForegroundColor Yellow
    $issScript = Join-Path $RepoRoot "packaging\MediaIsland.iss"
    $setupExeName = "MediaIsland-$Version-$Runtime-setup.exe"
    $setupExePath = Join-Path $OutputDir $setupExeName
    if (Test-Path -LiteralPath $setupExePath) {
        Remove-Item -LiteralPath $setupExePath -Force
    }

    $isccArgs = @(
        "/DAppVersion=$Version",
        "/DPublishDir=$PublishDir",
        "/DOutputDir=$OutputDir",
        $issScript
    )
    & $isccPath @isccArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $setupExePath)) {
        throw "Installer executable $setupExeName was not generated."
    }
    $setupItem = Get-Item -LiteralPath $setupExePath
    if ($setupItem.LastWriteTimeUtc -lt $buildStartTimeUtc) {
        throw "Freshness check failed: $setupExeName was not created in the current build run."
    }
    Write-Host "Created installer: $setupExeName ($([math]::Round($setupItem.Length / 1MB, 2)) MB)" -ForegroundColor Green
    $currentRunArtifacts += $setupExePath
} else {
    Write-Host "NOTICE: Inno Setup compiler (ISCC.exe) was not found on this machine." -ForegroundColor Cyan
    Write-Host "Skipping installer EXE compilation. The portable ZIP is ready for distribution." -ForegroundColor Cyan
    Write-Host "(In GitHub Actions CI, Inno Setup is pre-installed on windows-2022 runners.)" -ForegroundColor Cyan
}

# 10. Calculate SHA-256 Checksums strictly for current-run artifacts
Write-Host "`nCalculating SHA-256 checksums for current-run artifacts..." -ForegroundColor Yellow
$checksumFile = Join-Path $OutputDir "SHA256SUMS.txt"
$hashEntries = @()

foreach ($artifactPath in $currentRunArtifacts) {
    $fileName = Split-Path -Leaf $artifactPath
    $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $entry = "$hash  $fileName"
    $hashEntries += $entry
    Write-Host "  $entry" -ForegroundColor White
}

$hashEntries | Out-File -FilePath $checksumFile -Encoding utf8 -Force
Write-Host "Checksum file written: $checksumFile" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "Packaging completed successfully!" -ForegroundColor Green
Write-Host "Distribution packages location: $OutputDir" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
