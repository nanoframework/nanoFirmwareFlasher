echo "Make sure you have Python3 installed"

echo ""
echo "Install esptool..."
echo ""

# make sure you have pip installed
curl -sSL https://bootstrap.pypa.io/get-pip.py -o get-pip.py
python3 get-pip.py

echo "If you get an error message at the next line, try to add pip path to the path:"
echo "export PATH=$PATH:/path/to/pip"

# install esptool via pip into esptool-python
pip install --target=esptool-python esptool

echo ""
echo "Packaging esptool Python in Windows executable..."
echo ""

# create a package that can run esptool without python via PyInstaller
pyinstaller --specpath "esptool-python" --distpath "esptool__" --workpath "esptool-python\build" "esptool-python\esptool.py"

# copy esptool files
echo "Replace Linux with Mac for Mac"
cp -R esptool__/esptool nanoFirmwareFlasher/lib/esptool/esptoolLinux

# clean up working folders
rm -rf esptool-python
rm -rf esptool__

echo ""
echo "Esptool folder updated!"
echo ""

echo "***************************************************************"
echo "* MAKE SURE THAT ALL FILES ARE AVAILABLE IN SOLUTION EXPLORER *"
echo "* AS EMBEDDED RESOURCES AND MARKED AS COPY TO OUTPUT FOLDER   *"
echo "***************************************************************"
echo ""