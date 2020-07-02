[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT) [![#yourfirstpr](https://img.shields.io/badge/first--timers--only-friendly-blue.svg)](https://github.com/nanoframework/Home/blob/master/CONTRIBUTING.md) [![Build Status](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_apis/build/status/nanoframework.nanoFirmwareFlasher?branchName=develop)](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_build/latest?definitionId=45&branchName=develop) [![NuGet](https://img.shields.io/nuget/v/nanoFirmwareFlasher.svg?label=NuGet&style=flat&logo=nuget)](https://www.nuget.org/packages/nanoFirmwareFlasher/) [![Discord](https://img.shields.io/discord/478725473862549535.svg?logo=discord&logoColor=white&label=Discord&color=7289DA)](https://discord.gg/gCyBu8T)

![nanoFramework logo](https://github.com/nanoframework/Home/blob/master/resources/logo/nanoFramework-repo-logo.png)

-----

### Welcome to the **nanoFramework** nano firmware flasher tool repository!

This repo contains the nano firmware flasher tool.
It's a [.NET Core CLI Global Tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools) that allows flashing a **nanoFramework** target with nanoBooter, nanoCLR, managed application or backup files.
Is part of **nanoFramework** toolbox, along with other various tools that are required in **nanoFramework** development, usage or repository management.

It makes use of several 3rd party tools:

- Espressif esptool.
   You can find the esptool and licensing information on the repository [here](http://github.com/espressif/esptool).
- ST DfuSe USB.
   You can find the source, licensing information and documentation [here](https://www.st.com/en/development-tools/stsw-stm32080.html).
- ST-LINK Utility.
   You can find the source, licensing information and documentation [here](https://www.st.com/content/st_com/en/products/development-tools/software-development-tools/stm32-software-development-tools/stm32-programmers/stsw-link004.html).
- Texas Instruments Uniflash
   You can find the Uniflash tool and licensing information [here](http://www.ti.com/tool/download/UNIFLASH).

## Install **nanoFramework** Firmware Flasher

Perform a one-time install of the **nanoFramework** Firmware Flasher tool using the following .NET Core CLI command:

```console
dotnet tool install -g nanoff
```

In case you're installing a pre-release version of the tool you have to specify the version number and the **nanoFramework** Azure DevOps NuGet feed as the source. Like this:

```console
dotnet tool install -g nanoff --version 9.9.9-preview.100 --add-source https://pkgs.dev.azure.com/nanoframework/feed/_packaging/sandbox/nuget/v3/index.json
```

After a successful installation a message is displayed showing the command that's to be used to call the tool along with the version installed. Similar to the following example:

```console
You can invoke the tool using the following command: nanoff
Tool 'nanoff' (version '9.9.9-preview.100') was successfully installed.
```

To update **nanoFramework** Firmware Flasher tool use the following .NET Core CLI command:

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

### Update the firmware of an ESP32_WROOM_32 target

To update the firmware of an ESP32_WROOM_32 target connected to COM31, to the latest available development version.

```console
nanoff --update --target ESP32_WROOM_32 --serialport COM31
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

### Update the firmware of a specific STM32 target

To update the firmware of the NETDUINO3_WIFI target to the latest available stable version.

```console
nanoff --update --target NETDUINO3_WIFI --stable
```

### Deploy a managed application to a ST_STM32F769I_DISCOVERY target

To deploy a managed application to a ST_STM32F769I_DISCOVERY target, which has the deployment region at 0x08080000 flash address and reset the MCU after flashing it.

>Note: The binary file with the deployment image can be found on the Release or Debug folder of a Visual Studio project after a successful build. This file contains everything that's required to deploy a managed application to a target (meaning application executable and all referenced libraries and assemblies).

```console
nanoff --target ST_STM32F769I_DISCOVERY --deploy --image "E:\GitHub\nf-Samples\samples\Blinky\Blinky\bin\Debug\Blinky.bin" --address 0x08040000 --reset
```

### Update the firmware of a ST_STM32F769I_DISCOVERY along with a managed application

To update the firmware of the ST_STM32F769I_DISCOVERY target to the latest available preview version along with a managed application.
You have to specify the path to the managed application.
This example uses the binary format file that is generated by Visual Studio when building any nanoFramework C# application. Because it's a binary file you have to specify too the flash address of the deployment region (here 0x08000000, mind the hexadecimal format).

```console
nanoff --update --target ST_STM32F769I_DISCOVERY --binfile "c:\dev\my awesome app\bin\debug\my_awesome_app.bin" --address 0x08000000
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

### Update the firmware of a specific TI CC13x2 target

To update the firmware of the TI_CC1352R1_LAUNCHXL target to the latest available stable version.

```console
nanoff --update --target TI_CC1352R1_LAUNCHXL --stable
```

### Install the XDS110 USB drivers required by TI LaunchPad targets

To install the XDS110 USB drivers.

```console
nanoff --platform cc13x2 --installdrivers
```

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

The exit codes can be checked in [this source file](https://github.com/nanoframework/nanoFirmwareFlasher/blob/develop/source/nanoFirmwareFlasher/ExitCodes.cs).

## Feedback and documentation

To provide feedback, report issues and finding out how to contribute please refer to the [Home repo](https://github.com/nanoframework/Home).

Join our Discord community [here](https://discord.gg/gCyBu8T).

## Credits

The list of contributors to this project can be found at [CONTRIBUTORS](https://github.com/nanoframework/Home/blob/master/CONTRIBUTORS.md).

## License

The **nanoFramework** firmware flasher tool is licensed under the [MIT license](https://opensource.org/licenses/MIT).

## Code of Conduct

This project has adopted the code of conduct defined by the [Contributor Covenant](https://github.com/nanoframework/.github/blob/master/CODE_OF_CONDUCT.md)
to clarify expected behavior in our community.
