#
# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.
#


# requirements
# pip: install instructions @ https://pip.pypa.io/en/stable/installing/#upgrading-pip
# pypiwin32: pre-requisite for PyInstaller on Windows: install instructions @ https://pypi.org/project/pypiwin32/219/
# PyInstaller: install instructions @ https://pyinstaller.readthedocs.io/en/stable/installation.html


# move to destination path
$repoPath = Join-Path -Path $PSScriptRoot -ChildPath "\.." -Resolve
$libPath = Join-Path -Path $repoPath -ChildPath "lib"

Set-Location $libPath

Write-Host ""
Write-Host "Install esptool..."
Write-Host ""

# install esptool via pip into esptool-python
pip install --target=esptool-python esptool

Write-Host ""
Write-Host "Packaging esptool Python in Windows executable..."
Write-Host ""

# create a package that can run esptool without python via PyInstaller
pyinstaller --specpath "esptool-python" --distpath "esptool__" --workpath "esptool-python\build" "esptool-python\esptool.py"

# copy esptool files 
Get-ChildItem "esptool__\esptool" -File -Recurse | Copy-Item -Destination "esptool" -Force

# clean up working folders
Remove-Item -Path esptool-python -Recurse -Force
Remove-Item -Path esptool__ -Recurse -Force

Write-Host ""
Write-Host "Esptool folder updated!" -ForegroundColor Green
Write-Host ""

Write-Host "***************************************************************" -ForegroundColor Yellow
Write-Host "* MAKE SURE THAT ALL FILES ARE AVAILABLE IN SOLUTION EXPLORER *" -ForegroundColor Yellow
Write-Host "* AS EMBEDDED RESOURCES AND MARKED AS COPY TO OUTPUT FOLDER   *" -ForegroundColor Yellow
Write-Host "***************************************************************" -ForegroundColor Yellow
Write-Host ""