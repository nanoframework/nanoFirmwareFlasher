#
# Copyright (c) 2019 The nanoFramework project contributors
# See LICENSE file in the project root for full license information.
#


# requirements
# pip: install instructions @ https://pip.pypa.io/en/stable/installing/#upgrading-pip
# PyInstaller: install instructions @ https://pyinstaller.readthedocs.io/en/stable/installation.html

Write-Host ""
Write-Host "Install esptool..."
Write-Host ""

# install esptool via pip into esptool-python
pip install --target=esptool-python esptool

Write-Host ""
Write-Host "Packaging esptool Python in Windows executable..."
Write-Host ""

# create a package that can run esptool without python via PyInstaller
pyinstaller --distpath esptool__ --workpath esptool-python\build --specpath esptool-python esptool-python\esptool.py

# clean esptool folder (to catch new and deleted files)
# keep bin files
Remove-Item -Path esptool -Exclude *.bin -Recurse -Force

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