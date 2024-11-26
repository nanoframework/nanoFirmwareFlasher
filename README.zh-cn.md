[![#yourfirstpr](https://img.shields.io/badge/first--timers--only-friendly-blue.svg)](https://github.com/nanoframework/Home/blob/main/CONTRIBUTING.md) [![Build Status](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_apis/build/status/nanoFirmwareFlasher?repoName=nanoframework%2FnanoFirmwareFlasher&branchName=main)](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_build/latest?definitionId=45&repoName=nanoframework%2FnanoFirmwareFlasher&branchName=main) [![NuGet](https://img.shields.io/nuget/v/nanoff.svg?label=NuGet&style=flat&logo=nuget)](https://www.nuget.org/packages/nanoff/) [![Discord](https://img.shields.io/discord/478725473862549535.svg?logo=discord&logoColor=white&label=Discord&color=7289DA)](https://discord.gg/gCyBu8T)

![nanoFramework logo](https://raw.githubusercontent.com/nanoframework/Home/main/resources/logo/nanoFramework-repo-logo.png)

-----
切换语言: [English](README.md) | [简体中文](README.zh-cn.md)

### 欢迎来到.NET **nanoFramework** 固件刷写工具存储库

这个存储库包含.NET nanoFramework固件刷写工具。  
这是一个[.NET Core 工具](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools)，它能够将固件映像 (nanoBooter和nanoCLR) 刷写进一个.NET **nanoFramework** 支持的设备，部署应用程序（运行.NET应用程序所需的所有程序集）并恢复先前保存的部署映像。 
它是.NET **nanoFramework**工具集的一部分，与其他用于.NET **nanoFramework**开发、使用或仓库管理的各种工具一起使用。  

它使用了几个第三方工具:

- 乐鑫 esptool
   您可以在存储库中找到esptool和许可信息 [链接](http://github.com/espressif/esptool).
- STM32 Cube Programmer
   您可以找到源代码、许可信息和文档  [链接](https://www.st.com/en/development-tools/stm32cubeprog.html).
- Texas Instruments Uniflash
  您可以找到Uniflash工具和许可信息 [链接](http://www.ti.com/tool/download/UNIFLASH).

我们还将此工具作为 .NET 库分发，以便它可以集成到第三方应用程序中。请查看“Samples”文件夹中的 [README](Samples\README.md) 以获取更多详细信息以及示例应用程序。

## 安装.NET **nanoFramework** 固件刷写工具

使用以下.NET Core CLI命令一键安装.NET **nanoFramework** 固件刷写工具:  

```shell
dotnet tool install -g nanoff
```

成功安装后，将显示一条消息，显示将用于调用工具和所安装的版本的命令。 类似于下面的例子:  

```shell
You can invoke the tool using the following command: nanoff
Tool 'nanoff' (version '9.9.9') was successfully installed.
```

### 安装路径相关的问题

> [!CAUTION]
> 已知在安装路径中包含变音符号（例如：法语中的重音符号（acute accent）用于表示元音应该发/ay/的音）时，使用nanoff为STM32设备运行命令会出现问题。这是由于STM32 Cube Programmer中的一个已知错误引起的。如果您的路径中存在这种情况，您必须将其安装在没有这些字符的位置。为了实现这一点，可以使用以下.NET Core CLI命令，在其中指定工具将安装的路径：

```shell
dotnet tool install nanoff --tool-path c:\a-plain-simple-path-to-install-the-tool
```

请注意，如果您没有在STM32设备中使用'nanoff'，则此限制不适用。  

### 对于MacOS

您需要将nanoff添加到您的路径中。安装完成后，运行以下命令：

```shell
export PATH=$PATH:~/.dotnet/tools
```

## 更新.NET **nanoFramework**固件flash  

要更新.NET **nanoFramework** 固件刷写工具，请使用.NET Core CLI命令:  

```shell
dotnet tool update -g nanoff
```

如果工具安装在特定的路径下，请使用以下.NET Core CLI命令:  

```shell
dotnet tool update nanoff --tool-path c:\path-where-the-tool-was-installed
```

## 使用方法

一旦安装了该工具，你就可以通过命令“nanoff”来调用它，这是该程序名称的简称，以方便输入。  

```shell
nanoff [command] [args]
```

该工具包括所有可用命令的帮助，您可以通过输入以下命令来查看:  

```shell
nanoff --help
```

## ESP32 用法示例

有多个ESP32固件可用，一些是专门为一个设备构建的。 请参阅 [列表](https://github.com/nanoframework/nf-interpreter#firmware-for-reference-boards).

ESP32_PSRAM_REV0固件将只适用于ESP32系列的任何变体，带或不带pram，以及所有silicon版。  
你可以阅读更多关于不同固件之间的差异 [链接](https://docs.nanoframework.NET/content/reference-targets/esp32.html).

FEATHER_S2固件将适用于几乎所有的ESP32-S2系列的变体，暴露嵌入式USB CDC引脚。  

你可以阅读更多关于不同固件之间的差异 [链接](https://docs.nanoframework.NET/content/reference-targets/esp32.html).

当使用'nanoff'时，你可以添加'——target MY_TARGET_NAME_HERE'来使用特定的固件。 如果，相反，您只指定平台与'——platform esp32' 'nanoff'将自动选择最合适的固件，这取决于所连接的设备的功能。 类似于这个的输出将会显示出什么固件将要被使用:  

```shell
No target name was provided! Using 'ESP32_REV0' based on the device characteristics.
```

>注意:请注意，对于ESP32-S2，不可能安全地确定使用什么是最好的固件。 由于这个原因，它必须提供适当的设备名称与'——target MY_TARGET_NAME_HERE'。  

部分ESP32单板进入引导加载模式存在问题。 这通常可以通过按住BOOT/FLASH按钮来解决。  
如果' nanoff '检测到这种情况，会显示以下警告:  

```shell
*** Hold down the BOOT/FLASH button in ESP32 board ***
```

> [!WARNING]
> 要更新FeatherS2、TinyS2和一些S3模块，您需要将开发板置于下载模式。具体操作方法是按住[BOOT]按钮，单击[RESET]按钮，然后松开[BOOT]按钮。  

### 更新ESP32版本的设备器固件

更新连接到COM31的ESP32设备器的固件到最新可用的开发版本。  

```shell
nanoff --update --target ESP32_PSRAM_REV0 --serialport COM31
```

### 使用本地CLR文件更新ESP32-S2 KALUGA 1的固件  

使用本地CLR文件(例如来自一个构建文件)更新连接到COM31的ESP32-S2 KALUGA 1设备的固件。  
这个文件必须是一个二进制文件，且必须是有效且经过构建的CLR。 对文件内容不执行其他检查或验证。  

```shell
nanoff --update --target KALUGA_1 --serialport COM31 --clrfile "C:\nf-interpreter\build\nanoCLR.bin" 
```

您可以调整要使用的核心固件的名称。 请参阅前一节以获得完整的列表。  

### 显示所连接的ESP32设备的详细信息

查看与COM31相连的ESP32设备的详细信息。  

```shell
nanoff --platform esp32 --serialport COM31 --devicedetails 
```

可选地，可以传递额外的参数 `--checkpsram`，它会强制检测PSRAM的可用性。

### 将托管应用程序部署到ESP32设备  

将托管应用程序部署到连接到COM31的ESP32_PSRAM_REV0设备上。  

>Note: 在成功构建之后，可以在Visual Studio项目的Release或Debug文件夹中找到包含部署映像的二进制文件。 此文件包含将托管应用程序部署到设备所需的所有内容(即应用程序可执行文件和所有引用的库和程序集)。  

```shell
nanoff --target ESP32_PSRAM_REV0 --serialport COM12 --deploy --image "E:\GitHub\nf-Samples\samples\Blinky\Blinky\bin\Debug\Blinky.bin"
```

### 更新ESP32设备和托管应用程序的固件  

要在连接到COM31的ESP32设备机上部署应用程序，您必须指定托管应用程序的路径。 您也可以提供一个地址，该地址将覆盖默认的部署地址。  
本示例使用的是在构建应用程序时可以找到的二进制格式文件。 注意，因为只有应用程序可以运行，所以在构建库时，不会自动创建bin文件，这只针对应用程序。  

```shell
nanoff --target ESP32_PSRAM_REV0 --update --serialport COM31 --deploy --image "c:\eps32-backups\my_awesome_app.bin" --address 0x1B000
```

### 跳过备份配置分区

在更新连接到 COM31 的 ESP32 目标的固件时跳过备份配置分区。

```shell
nanoff --update --target ESP32_PSRAM_REV0 --serialport COM31 --noconfigbackup
```

## STM32用法示例

### 更新指定STM32设备的固件

使用JTAG连接将ST_STM32F769I_DISCOVERY设备的固件更新到最新可用的稳定版本。  

```shell
nanoff --update --target ST_STM32F769I_DISCOVERY --jtag
```

### 将托管应用程序部署到ST_STM32F769I_DISCOVERY设备  

将托管应用程序部署到ST_STM32F769I_DISCOVERY设备，该设备的部署区域位于0x08080000 Flash地址，并在刷写后复位MCU。  

>Note: 在成功构建之后，可以在Visual Studio项目的Release或Debug文件夹中找到包含部署映像的二进制文件。 此文件包含将托管应用程序部署到设备所需的所有内容(即应用程序可执行文件和所有引用的库和程序集)。  

```shell
nanoff --target ST_STM32F769I_DISCOVERY --deploy --image "E:\GitHub\nf-Samples\samples\Blinky\Blinky\bin\Debug\Blinky.bin" --address 0x08040000 --reset
```

### 更新ST_STM32F769I_DISCOVERY和托管应用程序的固件  

使用JTAG连接和托管应用程序，将ST_STM32F769I_DISCOVERY设备的固件更新到最新可用的预览版本。  
您必须指定托管应用程序的路径。  
本例使用了Visual Studio在构建任何nanoFramework c#应用程序时生成的二进制格式文件。 因为它是一个二进制文件，所以您还必须指定部署区域的flash地址(这里是0x08000000，注意十六进制格式)。  

```shell
nanoff --update --target ST_STM32F769I_DISCOVERY --jtag --binfile "c:\dev\my awesome app\bin\debug\my_awesome_app.bin" --address 0x08000000
```

### 列出JTAG连接中可用的所有STM32设备  

这将列出通过JTAG连接的所有STM32设备。  

```shell
nanoff --listjtag
```

### 列出所有可用的STM32设备与DFU连接  

这将列出所有通过DFU连接的STM32设备。  

```shell
nanoff --listdfu
```

### 安装STM32 JTAG驱动程序

为STM32 JTAG连接的设备安装驱动程序。  

```shell
nanoff --installjtagdrivers
```

### 安装STM32 DFU驱动程序

为STM32 DFU连接的设备安装驱动程序。  

```shell
nanoff --installdfudrivers
```

## TI CC13x2使用示例

### 更新指定TI CC13x2设备的固件  

将TI_CC1352R1_LAUNCHXL设备的固件更新到最新的预览版本。  

```shell
nanoff --update --target TI_CC1352R1_LAUNCHXL --preview
```

### 安装TI LaunchPad设备所需的XDS110 USB驱动程序  

安装XDS110 USB驱动。

```shell
nanoff --installxdsdrivers
```

### 预先检查设备是否与连接的设备相匹配

该工具会尽最大努力检查所请求的设备是否符合已连接的设备。  
有时这是不可能的，因为设备名称的差异和变化，或者缺乏所连接设备提供的详细信息，甚至(像DFU所连接的设备)，因为根本不可能确切地确定所连接的设备。  
这并不一定意味着固件不能工作，所以这只是一个建议。  

要禁用此验证，请在命令行中添加'——nofitcheck'选项。  

### 工具输出详细

可以通过' v|verbosity '选项设置工具输出详细。  

例如，如果在一个自动化流程中使用该工具，该流程需要最小的输出以简化对执行返回结果的处理，那么这是很方便的。 它可以设置为:  

- q[uiet]
- m[inimal]
- n[ormal]
- d[etailed]
- diag[nostic]

```shell
nanoff -v q
```

## 设备列表

翻译结果
您可以列出支持的设备及其版本，用于稳定版本或预览。 `--platform` 允许您过滤平台。 

在预览版本中列出可用于ESP32设备的软件包。  

```shell
nanoff --listboards --platform esp32
```

列出可用于STM32设备的包(稳定版本)。  

```shell
nanoff --listboards --platform stm32
```

如果你只使用'——listtargets'开关，你会得到所有设备的所有稳定包的列表。  

## 部署文件到设备存储

一些设备如 ESP32、Orgpal 和其他一些设备有可用的存储空间。文件可以部署到这个存储空间中。你需要使用 `filedeployment` 参数指向一个 JSON 文件，在刷写设备时部署文件：

```console
nanoff --target XIAO_ESP32C3 --update --masserase --serialport COM21 --filedeployment C:\path\deploy.json
```

JSON 文件中可以包含一个可选的 `SerialPort`，以防上传文件的端口与刷写设备的端口不同或未在主命令行中指定，并且必须包含一个 `Files` 条目的列表。每个条目必须包含 `DestinationFilePath`，即目标完整路径文件名，以及 `SourceFilePath`，即要部署的内容的源文件路径；否则，要删除文件时，必须包含要部署的源文件的完整路径和文件名：

```json
{
   "serialport":"COM42",
   "files": [
      {         
         "DestinationFilePath": "I:\\TestFile.txt",
         "SourceFilePath": "C:\\tmp\\NFApp3\\NFApp3\\TestFile.txt"
      },
      {
         "DestinationFilePath": "I:\\NoneFile.txt"
      },
      {
         "DestinationFilePath": "I:\\wilnotexist.txt",
         "SourceFilePath": "C:\\WRONGPATH\\TestFile.txt"
      }
   ]
}
```

如果你只想部署文件而不进行其他操作，你可以只指定：

```console
nanoff --filedeployment C:\path\deploy.json
```

在这种情况下，`SerialPort` 必须在 JSON 文件中存在。

> **注意：**
> 如果存储中已经存在文件，它将被新文件替换。
>
> 如果文件不存在且请求删除，则不会发生任何事情，会显示警告。
>
> 如果由于某种问题无法上传文件，其他文件的部署将继续，并会显示错误。

## 清除缓存位置

如果需要，可以清除存储在本地缓存中的固件包。
另外，缓存位置是用户文件夹中的目录 `-nanoFramework\fw_cache`。

当命令中包含此选项时，不会处理其他选项。

```console
nanoff --clearcache
```

## 固件存档

默认情况下，_nanoff_ 使用在线仓库来查找固件包。也可以使用本地目录作为固件的来源。可以通过 _--updatearchive_ 选项来填充固件存档：

```console
nanoff --updatearchive --target ESP32_S3_ALL --archivepath c:\...\firmware 
nanoff --updatearchive --platform esp32 --archivepath c:\...\firmware
```

查看已存档的固件列表：

```console
nanoff --listtargets --fromarchive --archivepath c:\...\firmware
```

要在设备上安装固件，使用与平常相同的命令行参数，但添加 _--fromarchive_ 和 _--archivepath_：

```console
nanoff --nanodevice --update --serialport COM9 --fromarchive --archivepath c:\...\firmware
```

## 跳过版本检查

默认情况下，nanoff 会检查是否发布了新版本的工具。如果不需要，可以添加选项 _--suppressnanoffversioncheck_ 来跳过检查。

## Exit codes

可以查看签入代码 [这个源文件](https://github.com/nanoframework/nanoFirmwareFlasher/blob/develop/nanoFirmwareFlasher/ExitCodes.cs).

## 反馈和文档

如欲提供反馈、报告问题及了解如何作出贡献，请参阅 [主储库](https://github.com/nanoframework/Home).

加入我们的Discord社区 [这里](https://discord.gg/gCyBu8T).

## 贡献者

此项目的贡献者列表可在以下网站找到  [贡献者](https://github.com/nanoframework/Home/blob/main/CONTRIBUTORS.md).

## 许可证

**nanoFramework** firmware flash工具使用 [MIT许可证](LICENSE).

## 规范

这个项目采用了贡献者契约所定义的行为准则，以澄清我们社区的预期行为。  
知识产权[.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

### .NET Foundation

这个项目由[.NET Foundation](https://dotnetfoundation.org)提供支持.
