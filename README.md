[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE) [![#yourfirstpr](https://img.shields.io/badge/first--timers--only-friendly-blue.svg)](https://github.com/nanoframework/Home/blob/main/CONTRIBUTING.md) [![Build Status](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_apis/build/status/nanoframework.nanoFirmwareFlasher?branchName=develop)](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_build/latest?definitionId=45&branchName=develop) [![NuGet](https://img.shields.io/nuget/v/nanoff.svg?label=NuGet&style=flat&logo=nuget)](https://www.nuget.org/packages/nanoff/) [![Discord](https://img.shields.io/discord/478725473862549535.svg?logo=discord&logoColor=white&label=Discord&color=7289DA)](https://discord.gg/gCyBu8T)

![nanoFramework logo](https://github.com/nanoframework/Home/blob/main/resources/logo/nanoFramework-repo-logo.png)

-----

### Welcome to the .NET **nanoFramework** firmware flasher tool repository!

This repo contains the nano firmware flasher tool.
It's a [.NET Core CLI Global Tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools) that allows flashing a .NET **nanoFramework** target with nanoBooter, nanoCLR, managed application or backup files.
Is part of .NET **nanoFramework** toolbox, along with other various tools that are required in .NET **nanoFramework** development, usage or repository management.

It makes use of several 3rd party tools:

- Espressif esptool
   You can find the esptool and licensing information on the repository [here](http://github.com/espressif/esptool).
- QMKDfuSe
   Tool based on STM DfusSe tool. You can find the source, licensing information and documentation [here](https://github.com/qmk/qmk_dfuse).
- STM32 Cube Programmer
   You can find the source, licensing information and documentation [here](https://www.st.com/en/development-tools/stm32cubeprog.html).
- Texas Instruments Uniflash
   You can find the Uniflash tool and licensing information [here](http://www.ti.com/tool/download/UNIFLASH).

## Install .NET **nanoFramework** Firmware Flasher

Perform a one-time install of the .NET **nanoFramework** Firmware Flasher tool using the following .NET Core CLI command:

```console
dotnet tool install -g nanoff
```

After a successful installation a message is displayed showing the command that's to be used to call the tool along with the version installed. Similar to the following example:

```console
You can invoke the tool using the following command: nanoff
Tool 'nanoff' (version '9.9.9') was successfully installed.
```

#### Install path issues

:warning: That are know issues running commands for STM32 devices when `nanoff` is installed in a path that contains diacritic characters. This is caused by a known bug in STM32 Cube Programmer. 
If that's the case with your user path, for example, you have to install it in a location that does have those.
To accomplish that, use the following .NET Core CLI command on which the path where the tool will be installed is specified:

```console
dotnet tool install nanoff --tool-path c:\a-plain-simple-path-to-install-the-tool
```

Note that if you're not using `nanoff` with STM32 devices, this limitation does not apply.

## Update .NET **nanoFramework** Firmware Flasher

To update .NET **nanoFramework** Firmware Flasher tool use the following .NET Core CLI command:

```console
dotnet tool update -g nanoff
```

## Usage

Once the tool is installed, you can call it by using its command `nanoff`, which is a short version of the name to ease typing.

```console
nanoff [command] [args]
```

The tool includes help for all available commands. You can see a list of all available ones by entering:

```console
nanoff --help
```

## ESP32 usage examples 

>Note: some ESP32 boards have issues entering bootloader mode. This can be usually overcome by holding down the BOOT/FLASH button in the board.
In case nanoff detects this situation the following warning is shown: 
```console
*** Hold down the BOOT/FLASH button in ESP32 board ***
```

### Update the firmware of an ESP32_WROOM_32 target

To update the firmware of an ESP32_WROOM_32 target connected to COM31, to the latest available development version.

```console
nanoff --update --target ESP32_WROOM_32 --serialport COM31
```

### Update the firmware of an ESP32_WROOM_32 target with a local CLR file

To update the firmware of an ESP32_WROOM_32 target connected to COM31 with a local CLR file (for example from a build).
This file has to be a binary file with a valid CLR from a build. No other checks or validations are performed on the file content.

```console
nanoff --update --target ESP32_WROOM_32 --serialport COM31 --clrfile "C:\nf-interpreter\build\nanoCLR.bin" 
```

### Show details of the connected ESP32 device

To show the details of the ESP32 device connected to COM31.

```console
nanoff --platform esp32 --serialport COM31 --devicedetails 
```

### Deploy a managed application to an ESP32_WROOM_32 target

To deploy a managed application to an ESP32_WROOM_32 target connected to COM31, which has the deployment region at 0x190000 flash address.

>Note: The binary file with the deployment image can be found on the Release or Debug folder of a Visual Studio project after a successful build. This file contains everything that's required to deploy a managed application to a target (meaning application executable and all referenced libraries and assemblies).

```console
nanoff --target ESP32_WROOM_32 --serialport COM12 --deploy --image "E:\GitHub\nf-Samples\samples\Blinky\Blinky\bin\Debug\Blinky.bin" --address 0x190000
```

### Update the firmware of an ESP32 target along with a managed application

To update the firmware of an ESP32 target connected to COM31, to the latest available development version.
You have to specify the path to the managed application.
This example uses the binary format file that was saved on a previous backup operation.

```console
nanoff --update --target ESP32_WROOM_32 --serialport COM31 --deployment "c:\eps32-backups\my_awesome_app.bin"
```

## STMP32 usage examples 

### Update the firmware of a specific STM32 target

To update the firmware of the ST_STM32F769I_DISCOVERY target to the latest available stable version using the JTAG connection.

```console
nanoff --update --target ST_STM32F769I_DISCOVERY --jtag
```

### Deploy a managed application to a ST_STM32F769I_DISCOVERY target

To deploy a managed application to a ST_STM32F769I_DISCOVERY target, which has the deployment region at 0x08080000 flash address and reset the MCU after flashing it.

>Note: The binary file with the deployment image can be found on the Release or Debug folder of a Visual Studio project after a successful build. This file contains everything that's required to deploy a managed application to a target (meaning application executable and all referenced libraries and assemblies).

```console
nanoff --target ST_STM32F769I_DISCOVERY --deploy --image "E:\GitHub\nf-Samples\samples\Blinky\Blinky\bin\Debug\Blinky.bin" --address 0x08040000 --reset
```

### Update the firmware of a ST_STM32F769I_DISCOVERY along with a managed application

To update the firmware of the ST_STM32F769I_DISCOVERY target to the latest available preview version, using a JTAG connection, along with a managed application.
You have to specify the path to the managed application.
This example uses the binary format file that is generated by Visual Studio when building any nanoFramework C# application. Because it's a binary file you have to specify too the flash address of the deployment region (here 0x08000000, mind the hexadecimal format).

```console
nanoff --update --target ST_STM32F769I_DISCOVERY --preview --jtag --binfile "c:\dev\my awesome app\bin\debug\my_awesome_app.bin" --address 0x08000000
```

### List all STM32 devices available with JTAG connection

This useful to list all STM32 devices that are connected through JTAG.

```console
nanoff --listjtag
```

### List all STM32 devices available with DFU connection

This useful to list all STM32 devices that are connected through DFU.

```console
nanoff --listdfu
```

### Install STM32 JTAG drivers

To install the drivers for STM32 JTAG connected targets.

```console
nanoff --installjtagdrivers
```

### Install STM32 DFU drivers

To install the drivers for STM32 DFU connected targets.

```console
nanoff --installdfudrivers
```

## TI CC13x2 usage examples 

### Update the firmware of a specific TI CC13x2 target

To update the firmware of the TI_CC1352R1_LAUNCHXL target to the latest preview version.

```console
nanoff --update --target TI_CC1352R1_LAUNCHXL --preview
```

### Install the XDS110 USB drivers required by TI LaunchPad targets

To install the XDS110 USB drivers.

```console
nanoff --installxdsdrivers
```

### Pre-check if target fits connected device

The tool tries to make a best effort sanity check on whether the requested target fits the connected target.
Sometimes that's not possible because of the differences and variations on the target names, or lack of details provided by the connected device or even (like with DFU connected devices) because it's not possible to determine exactly what device is connected at all.
This doesn't necessarily mean that the firmware wont' work, so take this as an advice only.

To disable this validation add `--nofitcheck` option to the command line.

### Tool output verbosity

The tool output verbosity can be set through the  `v|verbosity` option.

This is convenient, for example, if this tool is being used in a automated process where the minimum output is desired to ease processing the return result of the execution. It can be set to:

- q[uiet]
- m[inimal]
- n[ormal]
- d[etailed]
- diag[nostic]

```console
nanoff -v q
```

## Exit codes

The exit codes can be checked in [this source file](https://github.com/nanoframework/nanoFirmwareFlasher/blob/develop/nanoFirmwareFlasher/ExitCodes.cs).

## Feedback and documentation

To provide feedback, report issues and finding out how to contribute please refer to the [Home repo](https://github.com/nanoframework/Home).

Join our Discord community [here](https://discord.gg/gCyBu8T).

## Credits

The list of contributors to this project can be found at [CONTRIBUTORS](https://github.com/nanoframework/Home/blob/main/CONTRIBUTORS.md).

## License

The **nanoFramework** firmware flasher tool is licensed under the [MIT license](LICENSE).

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behaviour in our community.
For more information see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

### .NET Foundation

This project is supported by the [.NET Foundation](https://dotnetfoundation.org).
