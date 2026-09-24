#Requires -Version 7
<#
.SYNOPSIS
    Stamps a release version and installer hashes into the BenBurge.Wingman winget manifest.

.DESCRIPTION
    Rewrites PackageVersion in all three manifest files and, in the installer
    manifest, the InstallerUrl and InstallerSha256 for each architecture. Uses
    plain text replacement (no YAML module) so it runs the same on Windows and
    Linux. The release workflow runs this in the `release` job (ubuntu-latest)
    against the .sha256 files downloaded from the `publish` job, then uploads
    the updated manifests folder as an artifact; nothing here commits back to
    the repo or calls `winget validate` (not available on Linux).

.PARAMETER Version
    The release version, e.g. 0.1.0. Must be three dot-separated integers.

.PARAMETER Sha256Dir
    Folder containing wingman-v<version>-win-x64-setup.exe.sha256 and
    wingman-v<version>-win-arm64-setup.exe.sha256, each a line of the form
    "<hash>  <filename>" as written by .github/workflows/release.yml.

.PARAMETER ManifestDir
    Folder containing the three BenBurge.Wingman manifest files. Defaults to
    manifests/BenBurge.Wingman relative to the current directory.

.EXAMPLE
    pwsh tools/Update-Manifest.ps1 -Version 0.1.0 -Sha256Dir .\artifacts
#>

param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Sha256Dir,

    [string]$ManifestDir = "manifests/BenBurge.Wingman"
)

$ErrorActionPreference = "Stop"

# .NET's UTF8Encoding($false) omits the byte-order mark; Set-Content -Encoding utf8
# always writes one, which the manifests (and their yaml-language-server header) should
# not have to carry.
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, $Utf8NoBom)
}

function Get-InstallerHash {
    param(
        [Parameter(Mandatory)]
        [string]$Arch
    )

    $fileName = "wingman-v$Version-win-$Arch-setup.exe.sha256"
    $path = Join-Path $Sha256Dir $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing hash file for '$Arch': $path"
    }

    $line = (Get-Content -LiteralPath $path -Raw).Trim()
    $hash = ($line -split '\s+')[0]
    if ($hash -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Hash for '$Arch' in '$path' is not 64 hex characters: '$hash'"
    }

    return $hash.ToUpperInvariant()
}

if (-not (Test-Path -LiteralPath $Sha256Dir -PathType Container)) {
    throw "Sha256Dir not found: $Sha256Dir"
}

$hashes = [ordered]@{
    x64   = Get-InstallerHash -Arch "x64"
    arm64 = Get-InstallerHash -Arch "arm64"
}

$versionManifest   = Join-Path $ManifestDir "BenBurge.Wingman.yaml"
$localeManifest    = Join-Path $ManifestDir "BenBurge.Wingman.locale.en-US.yaml"
$installerManifest = Join-Path $ManifestDir "BenBurge.Wingman.installer.yaml"

foreach ($path in @($versionManifest, $localeManifest, $installerManifest)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Manifest file not found: $path"
    }
}

function Set-PackageVersion {
    param(
        [string]$Path
    )

    $content = Get-Content -LiteralPath $Path -Raw
    # A multiline $ only matches immediately before \n, so on a CRLF file it needs
    # the \r pulled into a lookahead - matching it elsewhere would either fail to
    # anchor (outside the match) or consume the \r into the match and strip it from
    # the replaced text.
    $updated = $content -replace '(?m)^PackageVersion:[ \t]*\S+[ \t]*(?=\r?$)', "PackageVersion: $Version"
    if ($updated -eq $content) {
        throw "Could not find a PackageVersion line in '$Path'."
    }

    Write-Utf8NoBom -Path $Path -Content $updated
}

Set-PackageVersion -Path $versionManifest
Set-PackageVersion -Path $localeManifest
Set-PackageVersion -Path $installerManifest

# Walked line by line (rather than one multi-line regex) so each architecture's
# InstallerUrl, its optional placeholder comment, and its InstallerSha256 can be
# handled as a unit without a MatchEvaluator closure, and so every other line -
# including its own line ending - passes through untouched.
$installerContent = Get-Content -LiteralPath $installerManifest -Raw
$lines = [regex]::Split($installerContent, '(?<=\r?\n)')
$output = [System.Collections.Generic.List[string]]::new()
$archsUpdated = [System.Collections.Generic.HashSet[string]]::new()
$pendingArch = $null

foreach ($line in $lines) {
    if ($line -match '^(?<indent>[ \t]*)InstallerUrl:[ \t]*(?<url>\S+)[ \t]*(?<eol>\r?\n)?$') {
        # Captured into locals before any further -match calls, which would otherwise
        # overwrite the automatic $Matches populated above.
        $urlIndent = $Matches.indent
        $urlValue = $Matches.url
        $urlEol = $Matches.eol

        $arch = $null
        if ($urlValue -match 'win-x64-setup\.exe$') { $arch = "x64" }
        elseif ($urlValue -match 'win-arm64-setup\.exe$') { $arch = "arm64" }

        if ($arch) {
            $newUrl = "https://github.com/BenBurge/Wingman/releases/download/v$Version/wingman-v$Version-win-$arch-setup.exe"
            $output.Add("$($urlIndent)InstallerUrl: $newUrl$($urlEol)")
            $pendingArch = $arch
            continue
        }
    }

    if ($pendingArch -and $line -match '^[ \t]*#.*Placeholder') {
        continue
    }

    if ($pendingArch -and $line -match '^(?<indent>[ \t]*)InstallerSha256:[ \t]*(?<hash>\S+)[ \t]*(?<eol>\r?\n)?$') {
        $output.Add("$($Matches.indent)InstallerSha256: $($hashes[$pendingArch])$($Matches.eol)")
        [void]$archsUpdated.Add($pendingArch)
        $pendingArch = $null
        continue
    }

    $output.Add($line)
}

if ($pendingArch) {
    throw "Found InstallerUrl for '$pendingArch' but no following InstallerSha256 in '$installerManifest'."
}
foreach ($arch in $hashes.Keys) {
    if (-not $archsUpdated.Contains($arch)) {
        throw "Could not find an InstallerUrl for '$arch' in '$installerManifest'."
    }
}

Write-Utf8NoBom -Path $installerManifest -Content (-join $output)

Write-Host "Updated manifests in '$ManifestDir' to version $Version"
foreach ($arch in $hashes.Keys) {
    $url = "https://github.com/BenBurge/Wingman/releases/download/v$Version/wingman-v$Version-win-$arch-setup.exe"
    Write-Host "  $arch"
    Write-Host "    $url"
    Write-Host "    $($hashes[$arch])"
}
