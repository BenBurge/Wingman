#Requires -Version 7
<#
.SYNOPSIS
    Installs or uninstalls a local build of Wingman.

.DESCRIPTION
    Publishes a single-file build of Wingman to a local folder, adds that folder to the user
    PATH, and registers it with `wingman setup` (the scheduled update checks, the tray icon,
    the Start Menu shortcut, and the wingman: protocol). Stops any running instance first, and
    is safe to rerun: each step only changes what is not already in place.

    With -Uninstall, it reverses all of the above: stops running instances, removes what
    `wingman setup --remove` registered, removes the install folder from the user PATH, and
    deletes the install folder.

    Windows only.

.PARAMETER Uninstall
    Remove the local install instead of creating or updating one.

.PARAMETER NoSetup
    Skip `wingman setup` after publishing. Has no effect with -Uninstall.

.PARAMETER Configuration
    Build configuration to publish: Release or Debug.

.PARAMETER Rid
    Runtime identifier to publish for, e.g. win-x64 or win-arm64.

.PARAMETER InstallDir
    Folder to install into.

.PARAMETER DryRun
    Print what each step would do instead of doing it.

.EXAMPLE
    pwsh tools/Install-Local.ps1

.EXAMPLE
    pwsh tools/Install-Local.ps1 -Uninstall

.EXAMPLE
    pwsh tools/Install-Local.ps1 -Configuration Debug -Rid win-arm64 -NoSetup
#>

[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$NoSetup,

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$Rid = 'win-x64',

    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\Wingman",

    [switch]$DryRun
)

if (-not $IsWindows) {
    Write-Error "Install-Local.ps1 is Windows only."
    exit 1
}

$ErrorActionPreference = 'Stop'

# The script lives in tools/, one level below the repo root.
$RepoRoot = Split-Path -Path $PSScriptRoot -Parent
$ProjectPath = Join-Path $RepoRoot 'src\Wingman\Wingman.csproj'
$ExePath = Join-Path $InstallDir 'wingman.exe'

# Add-Type only defines a .NET type in this process; it has no effect on the machine, so it
# runs the same whether or not -DryRun is set. The class must be public: PowerShell cannot
# resolve an internal type by name.
$NativeMethodsSource = @'
using System;
using System.Runtime.InteropServices;

namespace Wingman.Install
{
    public static class NativeMethods
    {
        public static readonly IntPtr HwndMessage = new IntPtr(-3);
        public const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
'@
Add-Type -TypeDefinition $NativeMethodsSource

function Write-Status {
    param([string]$Message)
    Write-Host "==> $Message"
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    Write-Status $Name
    try {
        & $Action
    } catch {
        Write-Error "Failed: $Name`n$_"
        exit 1
    }
}

function Test-SamePath {
    param([string]$Left, [string]$Right)
    return $Left.TrimEnd('\') -ieq $Right.TrimEnd('\')
}

# Closes the tray window (WM_CLOSE) and waits up to 3 seconds, then stops any remaining
# `wingman` process running from -InstallDir. A `wingman.exe` running elsewhere (e.g. a repo
# bin folder) is left alone.
function Stop-RunningInstances {
    if ($DryRun) {
        Write-Host "[dry run] would send WM_CLOSE to the 'Wingman.Tray' window and wait up to 3s"
    } else {
        $hwnd = [Wingman.Install.NativeMethods]::FindWindowEx([Wingman.Install.NativeMethods]::HwndMessage, [IntPtr]::Zero, 'Wingman.Tray', $null)
        if ($hwnd -ne [IntPtr]::Zero) {
            [void][Wingman.Install.NativeMethods]::PostMessage($hwnd, [Wingman.Install.NativeMethods]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)

            $deadline = [DateTime]::UtcNow.AddSeconds(3)
            while ([DateTime]::UtcNow -lt $deadline) {
                $hwnd = [Wingman.Install.NativeMethods]::FindWindowEx([Wingman.Install.NativeMethods]::HwndMessage, [IntPtr]::Zero, 'Wingman.Tray', $null)
                if ($hwnd -eq [IntPtr]::Zero) {
                    break
                }
                Start-Sleep -Milliseconds 100
            }
        }
    }

    $normalizedInstallDir = $InstallDir.TrimEnd('\') + '\'
    $processes = Get-Process -Name 'wingman' -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        $path = $null
        try {
            $path = $process.Path
        } catch {
            continue
        }

        if ($path -and $path.StartsWith($normalizedInstallDir, [StringComparison]::OrdinalIgnoreCase)) {
            if ($DryRun) {
                Write-Host "[dry run] would stop process $($process.Id) ($path)"
            } else {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# `wingman setup --remove` needs the exe that a publish or an uninstall is about to replace or
# delete, so this always runs before either.
function Remove-Registration {
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        return
    }

    if ($DryRun) {
        Write-Host "[dry run] would run: $ExePath setup --remove"
        return
    }

    try {
        $output = & $ExePath setup --remove 2>&1
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        if ($exitCode -ne 0) {
            Write-Warning "'$ExePath setup --remove' exited with code $exitCode"
        }
    } catch {
        Write-Warning "Could not run '$ExePath setup --remove': $_"
    }
}

function Publish-Wingman {
    $publishArgs = @(
        'publish', $ProjectPath,
        '-c', $Configuration,
        '-r', $Rid,
        '-p:Version=0.0.0-local',
        '-o', $InstallDir
    )

    if ($DryRun) {
        Write-Host "[dry run] would run (from $RepoRoot): dotnet $($publishArgs -join ' ')"
        return
    }

    Push-Location $RepoRoot
    try {
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish exited with code $LASTEXITCODE"
        }
    } finally {
        Pop-Location
    }
}

function Update-Path {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @($userPath -split ';' | Where-Object { $_ -ne '' })
    $alreadyPresent = $entries | Where-Object { Test-SamePath $_ $InstallDir }

    if ($Uninstall) {
        if (-not $alreadyPresent) {
            return
        }

        if ($DryRun) {
            Write-Host "[dry run] would remove '$InstallDir' from the user PATH"
            return
        }

        $remaining = $entries | Where-Object { -not (Test-SamePath $_ $InstallDir) }
        [Environment]::SetEnvironmentVariable('Path', ($remaining -join ';'), 'User')
        return
    }

    if (-not $alreadyPresent) {
        if ($DryRun) {
            Write-Host "[dry run] would add '$InstallDir' to the user PATH"
        } else {
            $newPath = if ($userPath) { "$userPath;$InstallDir" } else { $InstallDir }
            [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        }
    }

    $sessionEntries = @($env:Path -split ';' | Where-Object { $_ -ne '' })
    if (-not ($sessionEntries | Where-Object { Test-SamePath $_ $InstallDir })) {
        if ($DryRun) {
            Write-Host "[dry run] would add '$InstallDir' to this session's PATH"
        } else {
            $env:Path = "$env:Path;$InstallDir"
        }
    }
}

function Register-Wingman {
    if ($DryRun) {
        Write-Host "[dry run] would run: $ExePath setup"
        return
    }

    $output = & $ExePath setup 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        Write-Warning "'$ExePath setup' exited with code $exitCode"
    }
}

function Remove-InstallDir {
    if (-not (Test-Path -LiteralPath $InstallDir)) {
        Write-Host "'$InstallDir' does not exist; nothing to remove"
        return
    }

    if ($DryRun) {
        Write-Host "[dry run] would remove '$InstallDir'"
        return
    }

    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Write-Host "Removed '$InstallDir'"
}

function Show-Version {
    if ($DryRun) {
        Write-Host "[dry run] would run: $ExePath --version"
        return
    }

    & $ExePath --version
    Write-Host "Open a new terminal (or restart this one) to pick up the PATH change."
}

Invoke-Step 'Stopping running instances' { Stop-RunningInstances }
Invoke-Step 'Removing existing registration' { Remove-Registration }

if (-not $Uninstall) {
    Invoke-Step "Publishing ($Configuration, $Rid) to $InstallDir" { Publish-Wingman }
}

Invoke-Step 'Updating PATH' { Update-Path }

if (-not $Uninstall -and -not $NoSetup) {
    Invoke-Step 'Registering with wingman setup' { Register-Wingman }
}

if ($Uninstall) {
    Invoke-Step "Removing $InstallDir" { Remove-InstallDir }
} else {
    Invoke-Step 'Printing version' { Show-Version }
}
