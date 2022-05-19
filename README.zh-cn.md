[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE) [![#yourfirstpr](https://img.shields.io/badge/first--timers--only-friendly-blue.svg)](https://github.com/nanoframework/Home/blob/main/CONTRIBUTING.md) [![Build Status](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_apis/build/status/nanoFirmwareFlasher?repoName=nanoframework%2FnanoFirmwareFlasher&branchName=main)](https://dev.azure.com/nanoframework/nanoFirmwareFlasher/_build/latest?definitionId=45&repoName=nanoframework%2FnanoFirmwareFlasher&branchName=main) [![NuGet](https://img.shields.io/nuget/v/nanoff.svg?label=NuGet&style=flat&logo=nuget)](https://www.nuget.org/packages/nanoff/) [![Discord](https://img.shields.io/discord/478725473862549535.svg?logo=discord&logoColor=white&label=Discord&color=7289DA)](https://discord.gg/gCyBu8T)

![nanoFramework logo](https://raw.githubusercontent.com/nanoframework/Home/main/resources/logo/nanoFramework-repo-logo.png)

-----
文档语言: [English](README.md) | [简体中文](README.zh-cn.md)

### 欢迎来到 .NET **nanoFramework** firmware工具库  

这个repo包含nano固件闪光工具。  
这是一个[. NET Core Tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools)允许用一个固件映像(nanoBooter和nanoCLR)来刷新一个.NET **nanoFramework** 目标，应用程序部署(运行.NET应用程序所需的所有程序集)和恢复之前保存的部署映像。  
是.NET **nanoFramework**工具箱的一部分，以及其他各种工具，在.NET **nanoFramework**开发，使用或存储库管理中所需要的。  

它使用了几个第三方工具:

- 乐鑫 esptool
   您可以在存储库中找到esptool和许可信息 [这里](http://github.com/espressif/esptool).
- STM32 Cube Programmer
   您可以找到源代码、许可信息和文档  [这里](https://www.st.com/en/development-tools/stm32cubeprog.html).
- Texas Instruments Uniflash
  您可以找到Uniflash工具和许可信息 [这里](http://www.ti.com/tool/download/UNIFLASH).

## 安装.NET **nanoFramework** Firmware Flasher  

使用以下.NET Core CLI命令一次性安装.NET **nanoFramework** Firmware flash工具:  

```控制台
dotnet tool install -g nanoff
```

成功安装后，将显示一条消息，显示将用于调用工具和所安装的版本的命令。 类似于下面的例子:  

```控制台
You can invoke the tool using the following command: nanoff
Tool 'nanoff' (version '9.9.9') was successfully installed.
```

### 安装路径问题

:warning:当'nanoff'安装在包含变音符字符的路径中时，STM32设备运行命令会出现问题。 这是由STM32 Cube Programmer中的一个已知错误引起的。  
例如，如果你的用户路径是这种情况，你必须把它安装在一个有这些的位置。  
为了实现这一点，使用下面的.NET Core CLI命令，该命令指定了工具安装的路径:  

```控制台
dotnet tool install nanoff --tool-path c:\a-plain-simple-path-to-install-the-tool
```

请注意，如果您没有在STM32设备中使用'nanoff'，则此限制不适用。  

### MacOS使用

你还需要添加'nanoff'到你的路径，一旦安装运行:  

```控制台
export PATH=$PATH:~/.dotnet/tools
```

## 更新.NET **nanoFramework**固件flash  

要更新.NET **nanoFramework** Firmware flash工具，请使用.NET Core CLI命令:  

```控制台
dotnet tool update -g nanoff
```

如果工具安装在特定的路径下，请使用以下.NET Core CLI命令:  

```控制台
dotnet tool update nanoff --tool-path c:\path-where-the-tool-was-installed
```

## 用法

一旦安装了该工具，你可以通过使用它的命令“nanoff”来调用它，这是名称的简短版本，以方便输入。  

```控制台
nanoff [command] [args]
```

该工具包括所有可用命令的帮助。 您可以通过输入以下命令查看所有可用的列表:  

```控制台
nanoff --help
```

## ESP32 用法示例

有多个ESP32图像可用，一些是专门为一个目标构建的。 请参阅 [列表](https://github.com/nanoframework/nf-interpreter#firmware-for-reference-boards).

ESP32_PSRAM_REV0图像将只适用于ESP32系列的任何变体，带或不带pram，以及所有的silicon版本。  
你可以阅读更多关于不同图像之间的差异 [这里](https://docs.nanoframework.NET/content/reference-targets/esp32.html).

FEATHER_S2图像将适用于几乎所有的ESP32-S2系列的变体，暴露嵌入式USB CDC引脚。  

你可以阅读更多关于不同图像之间的差异 [这里](https://docs.nanoframework.NET/content/reference-targets/esp32.html).

当使用'nanoff'时，你可以添加'——target MY_TARGET_NAME_HERE'来使用特定的图像。 如果，相反，您只指定平台与'——platform esp32' 'nanoff'将选择最合适的图像，这取决于所连接的设备的功能。 类似于这个的输出将会显示出什么图像将要被使用:  

```控制台
没有提供目标名称! 根据设备特征使用'ESP32_REV0'。  
```

>注意:请注意，对于ESP32-S2目标，不可能安全地确定使用什么是最好的图像。 由于这个原因，它必须提供适当的目标名称与'——target MY_TARGET_NAME_HERE'。  

部分ESP32单板进入引导加载模式存在问题。 这通常可以通过按住BOOT/FLASH按钮来克服。  
如果' nanoff '检测到这种情况，会显示以下警告:  

```控制台
*** Hold down the BOOT/FLASH button in ESP32 board ***
```

:warning: 要更新FeatherS2和TinyS2，板需要把_download模式，按住[BOOT]，点击[RESET]，然后释放[BOOT]。  

### 更新ESP32版本的目标器固件

更新连接到COM31的ESP32目标器的固件到最新可用的开发版本。  

```控制台
nanoff --update --target ESP32_PSRAM_REV0 --serialport COM31
```

### 使用本地CLR文件更新ESP32-S2 KALUGA 1的固件  

使用本地CLR文件(例如来自一个构建文件)更新连接到COM31的ESP32-S2 KALUGA 1目标的固件。  
这个文件必须是一个二进制文件，具有来自构建的有效的CLR。 对文件内容不执行其他检查或验证。  

```控制台
nanoff --update --target KALUGA_1 --serialport COM31 --clrfile "C:\nf-interpreter\build\nanoCLR.bin" 
```

您可以调整要使用的核心图像的名称。 请参阅前一节以获得完整的列表。  

### 显示所连接的ESP32设备的详细信息

查看与COM31相连的ESP32设备的详细信息。  

```控制台
nanoff --platform esp32 --serialport COM31 --devicedetails 
```

### 将托管应用程序部署到ESP32目标  

将托管应用程序部署到连接到COM31的ESP32_PSRAM_REV0目标器上。  

>Note: 在成功构建之后，可以在Visual Studio项目的Release或Debug文件夹中找到包含部署映像的二进制文件。 此文件包含将托管应用程序部署到目标所需的所有内容(即应用程序可执行文件和所有引用的库和程序集)。  

```控制台
nanoff --target ESP32_PSRAM_REV0 --serialport COM12 --deploy --image "E:\GitHub\nf-Samples\samples\Blinky\Blinky\bin\Debug\Blinky.bin"
```

### 更新ESP32目标和托管应用程序的固件  

要在连接到COM31的ESP32目标机上部署应用程序，您必须指定托管应用程序的路径。 您也可以提供一个地址，该地址将覆盖默认的部署地址。  
本示例使用的是在构建应用程序时可以找到的二进制格式文件。 注意，因为只有应用程序可以运行，所以在构建库时，不会自动创建bin文件。 只对应用程序。  

```控制台
nanoff --target ESP32_PSRAM_REV0 --update --serialport COM31 --deploy --image "c:\eps32-backups\my_awesome_app.bin" --address 0x1B000
```

## STM32用法示例

### 更新指定STM32目标的固件

使用JTAG连接将ST_STM32F769I_DISCOVERY目标的固件更新到最新可用的稳定版本。  

```控制台
nanoff --update --target ST_STM32F769I_DISCOVERY --jtag
```

### 将托管应用程序部署到ST_STM32F769I_DISCOVERY目标  

将托管应用程序部署到ST_STM32F769I_DISCOVERY目标，该目标的部署区域位于0x08080000闪存地址，并在闪存后复位MCU。  

>Note: 在成功构建之后，可以在Visual Studio项目的Release或Debug文件夹中找到包含部署映像的二进制文件。 此文件包含将托管应用程序部署到目标所需的所有内容(即应用程序可执行文件和所有引用的库和程序集)。  

```控制台
nanoff --target ST_STM32F769I_DISCOVERY --deploy --image "E:\GitHub\nf-Samples\samples\Blinky\Blinky\bin\Debug\Blinky.bin" --address 0x08040000 --reset
```

### 更新ST_STM32F769I_DISCOVERY和托管应用程序的固件  

使用JTAG连接和托管应用程序，将ST_STM32F769I_DISCOVERY目标的固件更新到最新可用的预览版本。  
您必须指定托管应用程序的路径。  
本例使用了Visual Studio在构建任何nanoFramework c#应用程序时生成的二进制格式文件。 因为它是一个二进制文件，所以您还必须指定部署区域的flash地址(这里是0x08000000，注意十六进制格式)。  

```控制台
nanoff --update --target ST_STM32F769I_DISCOVERY --jtag --binfile "c:\dev\my awesome app\bin\debug\my_awesome_app.bin" --address 0x08000000
```

### 列出JTAG连接中可用的所有STM32设备  

这有助于列出通过JTAG连接的所有STM32设备。  

```控制台
nanoff --listjtag
```

### 列出所有可用的STM32设备与DFU连接  

这有助于列出所有通过DFU连接的STM32设备。  

```控制台
nanoff --listdfu
```

### 安装STM32 JTAG驱动程序

为STM32 JTAG连接的目标安装驱动程序。  

```控制台
nanoff --installjtagdrivers
```

### 安装STM32 DFU驱动程序

为STM32 DFU连接的目标安装驱动程序。  

```控制台
nanoff --installdfudrivers
```

## TI CC13x2使用示例

### 更新指定TI CC13x2目标的固件  

将TI_CC1352R1_LAUNCHXL目标的固件更新到最新的预览版本。  

```控制台
nanoff --update --target TI_CC1352R1_LAUNCHXL --preview
```

### 安装TI LaunchPad目标所需的XDS110 USB驱动程序  

安装XDS110 USB驱动。

```控制台
nanoff --installxdsdrivers
```

### 预先检查目标是否与连接的设备相匹配

该工具会尽最大努力检查所请求的目标是否符合已连接的目标。  
有时这是不可能的，因为目标名称的差异和变化，或者缺乏所连接设备提供的详细信息，甚至(像DFU所连接的设备)，因为根本不可能确切地确定所连接的设备。  
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

```控制台
nanoff -v q
```

## 目标列表

翻译结果
您可以列出支持的目标及其版本，用于稳定版本或预览。 `--platform` 允许您过滤平台。 

在预览版本中列出可用于ESP32目标的软件包。  

```控制台
nanoff --listboards --platform esp32
```

列出可用于STM32目标的包(稳定版本)。  

```控制台
nanoff --listboards --platform stm32
```

如果你只使用'——listtargets'开关，你会得到所有目标的所有稳定包的列表。  

## Exit codes

可以查看签入代码 [这个源文件](https://github.com/nanoframework/nanoFirmwareFlasher/blob/develop/nanoFirmwareFlasher/ExitCodes.cs).

## 反馈和文档

如欲提供反馈、报告问题及了解如何作出贡献，请参阅 [主储库](https://github.com/nanoframework/Home).

加入我们的Discord社区 [这里](https://discord.gg/gCyBu8T).

## 贡献者

此项目的贡献者列表可在以下网站找到  [贡献者](https://github.com/nanoframework/Home/blob/main/CONTRIBUTORS.md).

## 许可证

**nanoFramework** firmware flash工具是在 [麻省理工学院的许可](LICENSE).

## 规范

这个项目采用了贡献者契约所定义的行为准则，以澄清我们社区的预期行为。  
知识产权[.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

### .NET Foundation

这个项目是由[.NET Foundation](https://dotnetfoundation.org).
