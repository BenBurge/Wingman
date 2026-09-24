#Requires -Version 7
<#
.SYNOPSIS
    Builds the per-user Wingman setup executable with Inno Setup 6.

.DESCRIPTION
    Publishes a single-file wingman.exe for -Rid (unless -SkipPublish), compiles
    installer/wingman.iss with ISCC.exe into -OutputDir as
    wingman-v<version>-<rid>-setup.exe, and writes a matching .sha256 file in the
    "<hash>  <file>" format the release zips use. Prints the setup path.

    Windows only. Inno Setup 6.3 or later must be installed
    (winget install JRSoftware.InnoSetup); CI installs it with Chocolatey.

.PARAMETER Version
    The version stamped into wingman.exe and the installer, e.g. 0.1.0.

.PARAMETER Rid
    win-x64 or win-arm64.

.PARAMETER OutputDir
    Folder the setup executable and its .sha256 are written to. Defaults to
    artifacts in the repo; a relative path given here is relative to the
    current directory.

.PARAMETER SkipPublish
    Use an existing publish in -SourceDir instead of running dotnet publish.

.PARAMETER SourceDir
    Folder holding the published wingman.exe. Defaults to out/<rid> in the repo.

.EXAMPLE
    pwsh tools/Build-Installer.ps1 -Version 0.1.0

.EXAMPLE
    pwsh tools/Build-Installer.ps1 -Version 0.1.0 -Rid win-arm64 -OutputDir . -SkipPublish -SourceDir out/win-arm64
#>

[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.0.0-local',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',

    [string]$OutputDir = 'artifacts',

    [switch]$SkipPublish,

    [string]$SourceDir
)

if (-not $IsWindows) {
    Write-Error "Build-Installer.ps1 is Windows only."
    exit 1
}

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Path $PSScriptRoot -Parent
$ScriptPath = Join-Path $RepoRoot 'installer\wingman.iss'
$ProjectPath = Join-Path $RepoRoot 'src\Wingman\Wingman.csproj'
$CurrentDir = (Get-Location -PSProvider FileSystem).ProviderPath

# ISCC resolves relative paths against the .iss folder, so every path it gets is absolute.
function Resolve-FullPath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path, $CurrentDir)
}

function Find-Iscc {
    $onPath = Get-Command 'ISCC.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) {
        return $onPath.Source
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

$SourceDir = if ($SourceDir) { Resolve-FullPath $SourceDir } else { Join-Path $RepoRoot "out\$Rid" }
$OutputDir = if ($PSBoundParameters.ContainsKey('OutputDir')) { Resolve-FullPath $OutputDir } else { Join-Path $RepoRoot $OutputDir }

if (-not $SkipPublish) {
    Write-Host "==> Publishing $Rid $Version to $SourceDir"
    & dotnet publish $ProjectPath -c Release -r $Rid -o $SourceDir "-p:Version=$Version" "-p:InformationalVersion=$Version"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish exited with code $LASTEXITCODE"
    }
}

$exePath = Join-Path $SourceDir 'wingman.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Published wingman.exe not found: $exePath"
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw "Inno Setup 6 is required: winget install JRSoftware.InnoSetup"
}

# x64compatible also lets the x64 build install on Arm64 Windows, which runs x64 code.
$archAllowed = if ($Rid -eq 'win-arm64') { 'arm64' } else { 'x64compatible' }
$outputName = "wingman-v$Version-$Rid-setup"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "==> Compiling $outputName with $iscc"
& $iscc `
    "/DAppVersion=$Version" `
    "/DSourceDir=$SourceDir" `
    "/DOutputDir=$OutputDir" `
    "/DOutputName=$outputName" `
    "/DArchAllowed=$archAllowed" `
    $ScriptPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC exited with code $LASTEXITCODE"
}

$setupName = "$outputName.exe"
$setupPath = Join-Path $OutputDir $setupName
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "ISCC did not produce $setupPath"
}

$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLower()
"$hash  $setupName" | Out-File -Encoding ascii -LiteralPath "$setupPath.sha256"

Write-Host $setupPath
