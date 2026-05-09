<#
.SYNOPSIS
    Builds the Windows agent installer(s) and re-publishes the BorderLink
    server, in that order.

.DESCRIPTION
    Order matters: Build-Installer.ps1 drops the produced .exe into
    Server\wwwroot\Content\ so it gets copied into the published server output.
    If you publish first, the new installer files won't be in the running
    server's wwwroot until the next publish.

    Pass -Run to also start the server when publishing finishes.

.PARAMETER Arch
    Installer architecture(s) to build: x64, x86, or both. Default: both.

.PARAMETER SkipInstaller
    Skip the installer build (just re-publish the server).

.PARAMETER SkipPublish
    Skip the server publish (just rebuild the installer).

.PARAMETER SkipPublishAgent
    Pass -SkipPublish to Build-Installer.ps1 (skip the dotnet publish of the
    Agent project). Use when you've already published the agent recently and
    just want to rewrap the NSIS installer.

.PARAMETER Run
    Start the published BorderLink_Server.exe after publishing.

.PARAMETER Url
    URL to bind when -Run is set. Default: http://localhost:5000.

.PARAMETER Rid
    Server runtime identifier. Default: win-x64.

.PARAMETER DefaultServerUrl
    Pre-fills the Server URL field in the produced installer's UI. The hosted
    download endpoint embeds the right URL into the filename anyway, so this
    only matters for installers grabbed manually from out\.

.EXAMPLE
    powershell -f Rebuild.ps1
    powershell -f Rebuild.ps1 -Run
    powershell -f Rebuild.ps1 -SkipInstaller
    powershell -f Rebuild.ps1 -SkipPublishAgent -Run
#>

[CmdletBinding()]
param (
    [ValidateSet('x64','x86','both')]
    [string]$Arch = 'both',
    [switch]$SkipInstaller,
    [switch]$SkipPublish,
    [switch]$SkipPublishAgent,
    [switch]$Run,
    [string]$Url = 'http://localhost:5000',
    [string]$Rid = 'win-x64',
    [string]$DefaultServerUrl = ''
)

$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$InstallerScript = Join-Path $Root 'Agent\Installer\Windows\Build-Installer.ps1'
$PublishScript   = Join-Path $Root 'Utilities\Publish-Server.ps1'

if (-not (Test-Path $InstallerScript)) { throw "Missing: $InstallerScript" }
if (-not (Test-Path $PublishScript))   { throw "Missing: $PublishScript" }

$started = Get-Date

if (-not $SkipInstaller) {
    Write-Host ""
    Write-Host "===== 1/2  Build agent installer ($Arch) =====" -ForegroundColor Magenta
    $installerArgs = @{
        Arch = $Arch
    }
    if ($SkipPublishAgent)  { $installerArgs['SkipPublish']      = $true }
    if ($DefaultServerUrl)  { $installerArgs['DefaultServerUrl'] = $DefaultServerUrl }

    & $InstallerScript @installerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed (exit $LASTEXITCODE)."
    }
} else {
    Write-Host "Skipping installer build (-SkipInstaller)." -ForegroundColor DarkGray
}

if (-not $SkipPublish) {
    Write-Host ""
    Write-Host "===== 2/2  Publish server ($Rid) =====" -ForegroundColor Magenta
    $publishArgs = @{
        Rid = $Rid
        Url = $Url
    }
    if ($Run) { $publishArgs['Run'] = $true }

    & $PublishScript @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Server publish failed (exit $LASTEXITCODE)."
    }
} else {
    Write-Host "Skipping server publish (-SkipPublish)." -ForegroundColor DarkGray
}

$elapsed = (Get-Date) - $started
Write-Host ""
Write-Host ("Done in {0:N1}s." -f $elapsed.TotalSeconds) -ForegroundColor Green
