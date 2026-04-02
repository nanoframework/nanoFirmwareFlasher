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
    [Parameter(HelpMessage = "Stub version to download")][string]$RequestedVersion
)

# Source this helper
. .\common.ps1

$chipTypes = @("esp32", "esp32s2", "esp32s3", "esp32c3", "esp32c6", "esp32h2")
$stubDir = Join-Path (Join-Path (Join-Path $PSScriptRoot "nanoFirmwareFlasher.Library") "Esp32Serial") "StubImages"
$repoOwner = "espressif"
$repoName = "esp-flasher-stub"

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

foreach ($chip in $chipTypes) {
    # Release assets are named: esp32.json, esp32c3.json, etc.
    $assetName = "$chip.json"
    $url = "https://github.com/$repoOwner/$repoName/releases/download/$version/$assetName"
    # Our embedded resources are named: stub_esp32.json, stub_esp32c3.json, etc.
    $outputFile = Join-Path $stubDir "stub_$chip.json"
    
    "  Downloading $assetName..." | Write-Host -ForegroundColor Gray -NoNewline
    
    try {
        Invoke-WebRequest -Uri $url -OutFile $outputFile -UseBasicParsing -ErrorAction Stop
        " OK" | Write-Host -ForegroundColor Green
    }
    catch {
        " FAILED (may not exist for this chip/version)" | Write-Host -ForegroundColor Yellow
    }
}

"Stub images downloaded to: $stubDir" | Write-Host -ForegroundColor Green
"Remember to rebuild the project to embed the new stub images." | Write-Host -ForegroundColor White
