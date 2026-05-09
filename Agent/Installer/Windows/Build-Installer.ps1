<#
.SYNOPSIS
    Builds the BorderLink Agent NSIS installer for Windows.

.DESCRIPTION
    Publishes the agent for the requested architecture(s) (self-contained .NET)
    and then invokes makensis to wrap those files into a single .exe.

    Output: Agent\Installer\Windows\out\BorderLink-Agent-Setup-<arch>.exe

.PARAMETER Arch
    "x64", "x86", or "both" (default).

.PARAMETER DefaultServerUrl
    Optional. Pre-fills the Server URL field in the installer UI. Users can
    still override at install time, or pass /SERVERURL=... on the command line.

.PARAMETER Version
    Installer version. Defaults to a date-based string from the latest commit.

.PARAMETER MakeNsisPath
    Path to makensis.exe. If omitted, looked up via PATH and the standard
    "C:\Program Files (x86)\NSIS\" location.

.PARAMETER SkipPublish
    Skip the dotnet publish step (useful when the agent has already been
    published by Utilities\Publish.ps1).

.PARAMETER NoCopyToContent
    Don't copy the produced installers into Server\wwwroot\Content. By default
    the script copies them so they're picked up by the
    /api/clientdownloads/agent/... hosted endpoint.

.PARAMETER CertificatePath
.PARAMETER CertificatePassword
    Optional code-signing PFX. When provided, the produced setup.exe is signed
    with signtool from Utilities\.

.EXAMPLE
    powershell -f Build-Installer.ps1 -DefaultServerUrl https://remote.example.com
    powershell -f Build-Installer.ps1 -Arch x64 -SkipPublish
#>

[CmdletBinding()]
param (
    [ValidateSet('x64','x86','both')]
    [string]$Arch = 'both',
    [string]$DefaultServerUrl = '',
    [string]$Version = '',
    [string]$MakeNsisPath = '',
    [switch]$SkipPublish,
    [switch]$NoCopyToContent,
    [string]$CertificatePath = '',
    [string]$CertificatePassword = ''
)

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot  = (Resolve-Path (Join-Path $ScriptDir '..\..\..')).Path
$AgentProj = Join-Path $RepoRoot 'Agent\Agent.csproj'
$DesktopWinProj = Join-Path $RepoRoot 'Desktop.Win\Desktop.Win.csproj'
$PublishRoot = Join-Path $RepoRoot 'Agent\bin\publish'
$OutDir   = Join-Path $ScriptDir 'out'
$ContentDir = Join-Path $RepoRoot 'Server\wwwroot\Content'
$NsiFile  = Join-Path $ScriptDir 'BorderLink-Agent.nsi'
$SignTool = Join-Path $RepoRoot 'Utilities\signtool.exe'

if (-not $Version) {
    Push-Location $RepoRoot
    try {
        $stamp = git show -s --format=%ci 2>$null
        if ($stamp) {
            $Version = ([DateTimeOffset]::Parse($stamp)).ToString('yyyy.MM.dd.HHmm')
        } else {
            $Version = (Get-Date).ToString('yyyy.MM.dd.HHmm')
        }
    } finally { Pop-Location }
}

if (-not $MakeNsisPath) {
    $candidate = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($candidate) {
        $MakeNsisPath = $candidate.Source
    } elseif (Test-Path "${env:ProgramFiles(x86)}\NSIS\makensis.exe") {
        $MakeNsisPath = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
    } elseif (Test-Path "$env:ProgramFiles\NSIS\makensis.exe") {
        $MakeNsisPath = "$env:ProgramFiles\NSIS\makensis.exe"
    }
}

if (-not (Test-Path $MakeNsisPath)) {
    Write-Error "makensis.exe not found. Install NSIS (https://nsis.sourceforge.io/Download) or pass -MakeNsisPath."
    return
}

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

$archs = if ($Arch -eq 'both') { @('x64','x86') } else { @($Arch) }

foreach ($a in $archs) {
    $rid = "win-$a"
    $publishDir = Join-Path $PublishRoot $rid

    if (-not $SkipPublish) {
        Write-Host "Publishing agent ($rid)..." -ForegroundColor Cyan
        if (Test-Path $publishDir) {
            Get-ChildItem -Path $publishDir -Force | Remove-Item -Recurse -Force
        }
        & dotnet publish $AgentProj `
            --runtime $rid `
            --self-contained `
            --configuration Release `
            -p:Version=$Version `
            -p:FileVersion=$Version `
            --output $publishDir
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $rid (exit $LASTEXITCODE)"
        }

        # The screencaster (BorderLink_Desktop.exe) is its own project — its
        # packaged-win-<arch> publish profile drops the binary into
        # <publish>\Desktop, which is what gets bundled into the installer.
        Write-Host "Publishing screencaster (Desktop.Win, $rid)..." -ForegroundColor Cyan
        & dotnet publish $DesktopWinProj `
            -p:PublishProfile="packaged-win-$a" `
            -p:Version=$Version `
            -p:FileVersion=$Version `
            --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for screencaster $rid (exit $LASTEXITCODE)"
        }

        $desktopExe = Join-Path $publishDir 'Desktop\BorderLink_Desktop.exe'
        if (-not (Test-Path $desktopExe)) {
            throw "Screencaster publish completed but $desktopExe is missing. Check the packaged-win-$a publish profile."
        }
    }

    if (-not (Test-Path (Join-Path $publishDir 'BorderLink_Agent.exe'))) {
        throw "Agent has not been published to $publishDir. Run with -SkipPublish:$false or run Utilities\Publish.ps1 first."
    }

    Write-Host "Building installer ($a)..." -ForegroundColor Cyan
    $outFile = Join-Path $OutDir "BorderLink-Agent-Setup-$a.exe"

    $defines = @(
        "/DARCH=$a",
        "/DAGENT_DIR=$publishDir",
        "/DVERSION=$Version",
        "/DOUT_FILE=$outFile"
    )
    if ($DefaultServerUrl) {
        $defines += "/DDEFAULT_SERVER_URL=$DefaultServerUrl"
    }

    & $MakeNsisPath /V2 /NOCD @defines $NsiFile
    if ($LASTEXITCODE -ne 0) {
        throw "makensis failed for $a (exit $LASTEXITCODE)"
    }

    if ($CertificatePath -and (Test-Path $CertificatePath) -and $CertificatePassword) {
        if (Test-Path $SignTool) {
            Write-Host "Signing $outFile..." -ForegroundColor Cyan
            & $SignTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword `
                /t http://timestamp.digicert.com $outFile
            if ($LASTEXITCODE -ne 0) { Write-Warning "signtool returned exit $LASTEXITCODE" }
        } else {
            Write-Warning "Skipping signing - $SignTool not found."
        }
    }

    Write-Host "Built: $outFile" -ForegroundColor Green

    if (-not $NoCopyToContent) {
        if (Test-Path $ContentDir) {
            $dest = Join-Path $ContentDir "BorderLink-Agent-Setup-$a.exe"
            Copy-Item -Path $outFile -Destination $dest -Force
            Write-Host "Copied to: $dest" -ForegroundColor DarkGray
        } else {
            Write-Warning "Server\wwwroot\Content does not exist; skipping copy. Build the server project first or pass -NoCopyToContent."
        }
    }
}
