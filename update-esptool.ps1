# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

# this PS1 downloads the latest version of the esptool from their github repo, unpacks it

# get details about latest version
$lastRelease = $(gh release list --limit 1 --repo espressif/esptool)

# grab version
$version = $lastRelease.Split()[3]

# make sure security doesn't block our request
[Net.ServicePointManager]::SecurityProtocol = "tls12, tls11"

#############
# WIN version
$urlWin = "https://github.com/espressif/esptool/releases/download/$version/esptool-$version-win64.zip"
$outputWin = "$env:TEMP\esptool-$version-win64.zip"

"Downloading esptool for Windows..." | Write-Host -ForegroundColor White -NoNewline
(New-Object Net.WebClient).DownloadFile($urlWin, $outputWin)
"OK" | Write-Host -ForegroundColor Green

#############
# MAC version
$urlMac = "https://github.com/espressif/esptool/releases/download/$version/esptool-$version-macos.zip"
$outputMac = "$env:TEMP\esptool-$version-macos.zip"

"Downloading esptool for MAC..." | Write-Host -ForegroundColor White -NoNewline
(New-Object Net.WebClient).DownloadFile($urlMac, $outputMac)
"OK" | Write-Host -ForegroundColor Green

###############
# Linux version
$urlLinux = "https://github.com/espressif/esptool/releases/download/$version/esptool-$version-linux-amd64.zip"
$outputLinux = "$env:TEMP\esptool-$version-linux-amd64.zip"

"Downloading esptool for Linux..." | Write-Host -ForegroundColor White -NoNewline
(New-Object Net.WebClient).DownloadFile($urlLinux, $outputLinux)
"OK" | Write-Host -ForegroundColor Green

# unzip files
"Unzip files..." | Write-Host -ForegroundColor White -NoNewline
Expand-Archive $outputWin -DestinationPath $env:TEMP -Force > $null
Expand-Archive $outputMac -DestinationPath $env:TEMP  -Force > $null
Expand-Archive $outputLinux -DestinationPath $env:TEMP  -Force > $null
"OK" | Write-Host -ForegroundColor Green

# copy files to the correct locations
"Copying files to tools folders..." | Write-Host -ForegroundColor White -NoNewline
Move-Item -Path (Join-Path -Path $env:TEMP -ChildPath "esptool-$version-win64\**" -Resolve) -Destination (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin" -Resolve)
Move-Item -Path (Join-Path -Path $env:TEMP -ChildPath "esptool-$version-macos\**" -Resolve) -Destination (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac" -Resolve)
Move-Item -Path (Join-Path -Path $env:TEMP -ChildPath "esptool-$version-linux-amd64\**" -Resolve) -Destination (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux" -Resolve)
"OK" | Write-Host -ForegroundColor Green

# cleanup files
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin\LICENSE" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolWin\README.md" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac\LICENSE" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolMac\README.md" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux\LICENSE" -Resolve) -Force
Remove-Item -Path (Join-Path -Path $PSScriptRoot -ChildPath "lib\esptool\esptoolLinux\README.md" -Resolve) -Force
