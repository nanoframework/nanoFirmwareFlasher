# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

# this PS1 downloads the latest version of SEGGER J-Link from their website and unpacks it

# version
$version = "766f"

# make sure security doesn't block our request
[Net.ServicePointManager]::SecurityProtocol = "tls12, tls11"

#############
# WIN version
$urlWin = "https://www.segger.com/downloads/jlink/JLink_Windows_V" + $version + "_x86_64.exe"
# $outputWin = "$env:TEMP\JLink_Windows_x86_64.7zip"
$outputWin = "e:\temp\JLink_Windows_x86_64.7zip"

"Downloading SEGGER J-Link for Windows..." | Write-Host -ForegroundColor White -NoNewline
(New-Object Net.WebClient).DownloadFile($urlWin, $outputWin)
"OK" | Write-Host -ForegroundColor Green
