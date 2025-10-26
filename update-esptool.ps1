# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

# this PS1 downloads the latest (or a specific) version of the esptool from their github repo and unpacks it in the appropriate folder for distribution with nanoff

[CmdletBinding(SupportsShouldProcess = $true)]
param (
    [Parameter(HelpMessage = "esptool version requested")][string]$RequestedVersion
)

# get latest version if none was requested
if ([string]::IsNullOrEmpty($RequestedVersion)) {
    # get details about latest version
    $lastRelease = $(gh release list --limit 1 --repo espressif/esptool)

    # grab version
    $version = $lastRelease.Split()[3]
}
else {
    $version = $RequestedVersion
}

# make sure security doesn't block our request
[Net.ServicePointManager]::SecurityProtocol = "tls12, tls11"

$tempPath = (Get-Item -LiteralPath ([System.IO.Path]::GetTempPath())).FullName

#############
# WIN version
$urlWin = "https://github.com/espressif/esptool/releases/download/$version/esptool-$version-windows-amd64.zip"
$outputWin = (Join-Path -Path $tempPath -ChildPath "esptool-$version-windows-amd64.zip")

"Downloading esptool $version for Windows..." | Write-Host -ForegroundColor White -NoNewline
(New-Object Net.WebClient).DownloadFile($urlWin, $outputWin)
"OK" | Write-Host -ForegroundColor Green

#############
# MAC version
$urlMac = "https://github.com/espressif/esptool/releases/download/$version/esptool-$version-macos-amd64.tar.gz"
$outputMac = (Join-Path -Path $tempPath -ChildPath "esptool-$version-macos-amd64.tar.gz")

"Downloading esptool $version for MAC..." | Write-Host -ForegroundColor White -NoNewline
(New-Object Net.WebClient).DownloadFile($urlMac, $outputMac)
"OK" | Write-Host -ForegroundColor Green

###############
# Linux version
$urlLinux = "https://github.com/espressif/esptool/releases/download/$version/esptool-$version-linux-amd64.tar.gz"
$outputLinux = (Join-Path -Path $tempPath -ChildPath "esptool-$version-linux-amd64.tar.gz")

"Downloading esptool for $version Linux..." | Write-Host -ForegroundColor White -NoNewline
(New-Object Net.WebClient).DownloadFile($urlLinux, $outputLinux)
"OK" | Write-Host -ForegroundColor Green

# unzip files
"Unzip files..." | Write-Host -ForegroundColor White -NoNewline
Expand-Archive $outputWin -DestinationPath $tempPath -Force > $null

# tarballs need an extra extraction pass because Expand-Archive only supports zip
& tar -xf $outputMac -C $tempPath
& tar -xf $outputLinux -C $tempPath

"OK" | Write-Host -ForegroundColor Green

# clean destination folders
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin" -Resolve) -Include *.* -Force -Recurse
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac" -Resolve) -Include *.* -Force -Recurse
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux" -Resolve) -Include *.* -Force -Recurse

# copy files to the correct locations
"Copying files to tools folders..." | Write-Host -ForegroundColor White -NoNewline
Move-Item -Path (Join-Path -Path $tempPath -ChildPath "esptool-windows-amd64\**" -Resolve) -Destination (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin" -Resolve) -Force
Move-Item -Path (Join-Path -Path $tempPath -ChildPath "esptool-macos-amd64\**" -Resolve) -Destination (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac" -Resolve) -Force
Move-Item -Path (Join-Path -Path $tempPath -ChildPath "esptool-linux-amd64\**" -Resolve) -Destination (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux" -Resolve) -Force
"OK" | Write-Host -ForegroundColor Green

# cleanup files
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin\LICENSE" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin\README.md" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin\esp_rfc2217_server.exe" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin\espefuse.exe" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin\espsecure.exe" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac\LICENSE" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac\README.md" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac\esp_rfc2217_server" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac\espefuse" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac\espsecure" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux\LICENSE" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux\README.md" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux\esp_rfc2217_server" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux\espefuse" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux\espsecure" -Resolve) -Force
