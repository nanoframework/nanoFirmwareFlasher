# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

<#
.SYNOPSIS
    Downloads stub loader images from the official Espressif esp-flasher-stub repository
    (Apache 2.0 / MIT dual licensed) and saves them as embedded resource JSON files.

.DESCRIPTION
    This script downloads pre-compiled stub flasher binaries from Espressif's
    esp-flasher-stub repository (https://github.com/espressif/esp-flasher-stub).
    The stubs enable high-speed flash operations (baud rate change, compressed writes, MD5).

    The downloaded stubs are placed in nanoFirmwareFlasher.Library/Esp32Serial/StubImages/
    as embedded JSON resources.

.PARAMETER RequestedVersion
    Version tag to download (e.g. "v0.2.0"). Defaults to latest release.
#>

[CmdletBinding()]
param (
    [Parameter(HelpMessage = "Stub version to download")][string]$RequestedVersion,
    [Parameter(HelpMessage = "Number of download attempts per asset")][ValidateRange(1, 10)][int]$RetryCount = 3,
    [Parameter(HelpMessage = "Initial delay between retries in seconds")][ValidateRange(1, 30)][int]$RetryDelaySeconds = 2
)

# Source this helper
. .\common.ps1

$chipTypes = @(
    "esp32",
    "esp32c2",
    "esp32c3",
    "esp32c5",
    "esp32c6",
    "esp32c61",
    "esp32h2",
    "esp32h4",
    "esp32p4",
    "esp32s2",
    "esp32s3",
    "esp32s31"
)
$stubDir = Join-Path (Join-Path (Join-Path $PSScriptRoot "nanoFirmwareFlasher.Library") "Esp32Serial") "StubImages"
$repoOwner = "espressif"
$repoName = "esp-flasher-stub"

function Invoke-DownloadWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][int]$Attempts,
        [Parameter(Mandatory = $true)][int]$InitialDelaySeconds
    )

    $lastError = $null

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $DestinationPath -UseBasicParsing -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_

            if ($attempt -ge $Attempts) {
                break
            }

            $delaySeconds = [Math]::Min($InitialDelaySeconds * $attempt, 15)
            " (retry $attempt/$($Attempts - 1) in ${delaySeconds}s)" | Write-Host -ForegroundColor Yellow -NoNewline
            Start-Sleep -Seconds $delaySeconds
        }
    }

    throw $lastError
}

# Create output directory
if (-not (Test-Path $stubDir)) {
    New-Item -ItemType Directory -Path $stubDir -Force | Out-Null
}

# Get version
if ([string]::IsNullOrEmpty($RequestedVersion)) {
    "Getting latest release from $repoOwner/$repoName..." | Write-Host -ForegroundColor White -NoNewline
    
    try {
        $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoOwner/$repoName/releases/latest"
        $version = $releases.tag_name
    }
    catch {
        "Failed to get latest release. Please specify a version with -RequestedVersion." | Write-Host -ForegroundColor Red
        exit 1
    }
    
    " $version" | Write-Host -ForegroundColor White
}
else {
    $version = $RequestedVersion
}

"Downloading stub images for version $version..." | Write-Host -ForegroundColor White

$failedAssets = @()

foreach ($chip in $chipTypes) {
    # Release assets are named: esp32.json, esp32c3.json, etc.
    $assetName = "$chip.json"
    $url = "https://github.com/$repoOwner/$repoName/releases/download/$version/$assetName"
    # Our embedded resources are named: stub_esp32.json, stub_esp32c3.json, etc.
    $outputFile = Join-Path $stubDir "stub_$chip.json"
    $tempFile = "$outputFile.$PID.tmp"
    
    "  Downloading $assetName..." | Write-Host -ForegroundColor Gray -NoNewline
    
    try {
        Invoke-DownloadWithRetry -Uri $url -DestinationPath $tempFile -Attempts $RetryCount -InitialDelaySeconds $RetryDelaySeconds

        # Promote only after successful download, so stale files are not partially updated.
        Move-Item -Path $tempFile -Destination $outputFile -Force

        " OK" | Write-Host -ForegroundColor Green
    }
    catch {
        if (Test-Path $tempFile) {
            Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
        }

        $failedAssets += $assetName
        " FAILED ($($_.Exception.Message))" | Write-Host -ForegroundColor Red
    }
}

if ($failedAssets.Count -gt 0) {
    "Failed to download $($failedAssets.Count) stub asset(s): $($failedAssets -join ', ')" | Write-Host -ForegroundColor Red
    "No existing stub file was replaced unless its download completed successfully." | Write-Host -ForegroundColor Yellow
    exit 1
}

"Stub images downloaded to: $stubDir" | Write-Host -ForegroundColor Green
"Remember to rebuild the project to embed the new stub images." | Write-Host -ForegroundColor White
