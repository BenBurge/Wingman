<#
.SYNOPSIS
    Captures real winget output into text fixtures for Wingman.Core.Tests.

.DESCRIPTION
    Runs a fixed set of winget commands on this machine and saves their raw
    stdout as UTF-8 (no BOM) files, plus the exit code of each command. Run
    this on a real Windows machine with winget installed, then copy the
    output folder's contents into tests/Wingman.Core.Tests/Fixtures/.

.PARAMETER OutputPath
    Folder to write the captured fixtures into. Created if it does not exist.

.EXAMPLE
    .\Capture-WingetFixtures.ps1
    .\Capture-WingetFixtures.ps1 -OutputPath C:\temp\wingman-fixtures
#>

param(
    [string]$OutputPath = ".\fixtures"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

# .NET's UTF8Encoding($false) omits the byte-order mark; Set-Content -Encoding utf8
# always writes one, which the parser (and xunit fixture diffs) should not have to skip.
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, $Utf8NoBom)
}

$Captures = @(
    @{ Name = "list";                       Args = @("list") }
    @{ Name = "upgrade";                    Args = @("upgrade") }
    @{ Name = "upgrade-include-unknown";    Args = @("upgrade", "--include-unknown") }
    @{ Name = "search-vscode";              Args = @("search", "visual studio code") }
    @{ Name = "search-git";                 Args = @("search", "git") }
    @{ Name = "search-nomatch";             Args = @("search", "zzzznomatch") }
    @{ Name = "show-vscode";                Args = @("show", "Microsoft.VisualStudioCode") }
    @{ Name = "show-versions-git";          Args = @("show", "Git.Git", "--versions") }
    @{ Name = "pin-list";                   Args = @("pin", "list") }
    @{ Name = "version";                    Args = @("--version") }
)

$CommonArgs = @("--disable-interactivity", "--accept-source-agreements")
$ExitCodes = [ordered]@{}

foreach ($Capture in $Captures) {
    $Name = $Capture.Name
    $Args = $Capture.Args

    # --version does not accept the common flags.
    $FullArgs = if ($Name -eq "version") { $Args } else { $Args + $CommonArgs }

    Write-Host "Capturing '$Name': winget $($FullArgs -join ' ')"

    $StdOut = & winget @FullArgs 2>$null | Out-String
    $ExitCode = $LASTEXITCODE

    $DestPath = Join-Path $OutputPath "$Name.txt"
    Write-Utf8NoBom -Path $DestPath -Content $StdOut
    $ExitCodes[$Name] = $ExitCode
}

$ExitCodesContent = ($ExitCodes.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n"
Write-Utf8NoBom -Path (Join-Path $OutputPath "exit-codes.txt") -Content $ExitCodesContent

Write-Host ""
Write-Host "Done. Copy the contents of '$OutputPath' into tests\Wingman.Core.Tests\Fixtures\ in the Wingman repo:"
Write-Host "  Copy-Item '$OutputPath\*' '<repo>\tests\Wingman.Core.Tests\Fixtures\' -Force"
