<#
.SYNOPSIS
    Publishes the BorderLink server to a self-contained folder ready to run.

.DESCRIPTION
    Wraps `dotnet publish` for the Server project. Defaults match the layout
    Trent uses for local testing:
        Server\bin\publish\<rid>\BorderLink_Server.exe --urls=http://localhost:5000

    Use this any time you've changed Razor components, controllers, or static
    assets and want to refresh the running build. Razor components compile
    into the server assembly, so a CSS-only or .razor-only edit will not
    appear until you re-publish (or run from source via `dotnet run`).

.PARAMETER Rid
    Runtime identifier. Defaults to win-x64.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutDir
    Publish output directory. Defaults to Server\bin\publish\<rid>.

.PARAMETER Run
    Start the published BorderLink_Server.exe after publishing.

.PARAMETER Url
    URL to bind when -Run is set. Defaults to http://localhost:5000.

.PARAMETER NoRestore
    Skip the implicit restore step. Useful when iterating quickly and the
    package graph hasn't changed.

.PARAMETER SkipLibman
    Skip the LibraryManager (libman) restore. Defaults to $true because
    libman's @msgpack/msgpack and signalr-protocol-msgpack packages 404 on
    unpkg in some environments — and the restored files in wwwroot\lib\ are
    already checked in. Set to $false (or pass -SkipLibman:$false) to force
    libman to refresh those libraries.

.EXAMPLE
    powershell -f Utilities\Publish-Server.ps1
    powershell -f Utilities\Publish-Server.ps1 -Run
    powershell -f Utilities\Publish-Server.ps1 -Rid linux-x64 -Configuration Release
#>

[CmdletBinding()]
param (
    [string]$Rid = 'win-x64',
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [string]$OutDir = '',
    [switch]$Run,
    [string]$Url = 'http://localhost:5000',
    [switch]$NoRestore,
    [bool]$SkipLibman = $true
)

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot  = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$ServerProj = Join-Path $RepoRoot 'Server\Server.csproj'

if (-not $OutDir) {
    $OutDir = Join-Path $RepoRoot "Server\bin\publish\$Rid"
}

if (-not (Test-Path $ServerProj)) {
    throw "Server.csproj not found at $ServerProj."
}

# Date-based version stamp consistent with Utilities\Publish.ps1.
Push-Location $RepoRoot
try {
    $stamp = git show -s --format=%ci 2>$null
    if ($stamp) {
        $version = ([DateTimeOffset]::Parse($stamp)).ToString('yyyy.MM.dd.HHmm')
    } else {
        $version = (Get-Date).ToString('yyyy.MM.dd.HHmm')
    }
} finally { Pop-Location }

Write-Host "Publishing BorderLink Server..." -ForegroundColor Cyan
Write-Host "  Project:       $ServerProj"
Write-Host "  Configuration: $Configuration"
Write-Host "  RID:           $Rid"
Write-Host "  Output:        $OutDir"
Write-Host "  Version:       $version"

# A running BorderLink_Server.exe locks the DLLs in $OutDir and breaks the
# publish copy step with a confusing MSB3027. Detect early and explain.
$runningExePath = Join-Path $OutDir 'BorderLink_Server.exe'
if (Test-Path $runningExePath) {
    $running = Get-Process -Name 'BorderLink_Server' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -ieq $runningExePath }
    if ($running) {
        Write-Error ("BorderLink_Server.exe (PID {0}) is currently running from $OutDir. " +
                     "Stop it (Ctrl+C in its terminal, or 'Stop-Process -Id {0}') and re-run this script." -f $running.Id)
        exit 1
    }
}

$publishArgs = @(
    'publish', $ServerProj,
    '--configuration', $Configuration,
    '--runtime', $Rid,
    '--self-contained',
    '--output', $OutDir,
    "-p:Version=$version",
    "-p:FileVersion=$version"
)
if ($NoRestore) {
    $publishArgs += '--no-restore'
}
if ($SkipLibman) {
    # Disables Microsoft.Web.LibraryManager.Build's restore target.
    $publishArgs += '-p:LibraryRestore=False'
}

$started = Get-Date
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)."
}
$elapsed = (Get-Date) - $started
Write-Host ("Publish completed in {0:N1}s." -f $elapsed.TotalSeconds) -ForegroundColor Green

$exe = Join-Path $OutDir 'BorderLink_Server.exe'
if (-not (Test-Path $exe)) {
    # Linux/macOS RIDs produce a binary without an extension.
    $exe = Join-Path $OutDir 'BorderLink_Server'
}

if ($Run) {
    if (-not (Test-Path $exe)) {
        throw "Published binary not found at $exe."
    }
    Write-Host "Starting $exe --urls=$Url ..." -ForegroundColor Cyan
    Push-Location $OutDir
    try {
        & $exe "--urls=$Url"
    } finally { Pop-Location }
} else {
    Write-Host ""
    Write-Host "To run:" -ForegroundColor DarkGray
    Write-Host "  cd `"$OutDir`""
    Write-Host "  .\BorderLink_Server.exe --urls=$Url"
}
