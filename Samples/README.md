# nano firmware flasher library

We are making this tool available as a .NET library to allow 3rd party applications to easily integrate these features.
This is distributed as a NuGet package. All that's required is to reference it. There are versions available for .NET 6.0 and .NET Framework 4.7.2.
Note that the .NET 6.0 distribution is multi-platform and can be used in Windows, Mac OS and Linux machines.

>Note: if you're coding a .NET Framework application, make sure to [migrate](https://devblogs.microsoft.com/nuget/migrate-packages-config-to-package-reference) the project to use NuGet Package Reference.

## Sample applications

The following sample applications are available within the Samples solution in this folder.

### List J-Link Devices

This console application lists all the J-Link devices connected to the PC.
A single line of code is all it takes:

```csharp
var connecteDevices = JLinkDevice.ListDevices();
```

To output the devices, just need to iterate over the devices collection, like this:

```csharp
foreach (string deviceId in connecteDevices)
{
    Console.WriteLine(deviceId);
}
```

### List ST JTAG Devices

This console application lists all the ST Microelectronics JTAG devices connected to the PC.
A single line of code is all it takes:

```csharp
var connecteDevices = StmJtagDevice.ListDevices();
```

To output the devices, just need to iterate over the devices collection, like this:

```csharp
foreach (string deviceId in connecteDevices)
{
    Console.WriteLine(deviceId);
}
```

### Update ESP32 firmware

This console application updates the firmware of an ESP32 device connected through a COM port.

ESP32 updates rely on `esptool`, so this needs to be instantiated first:

```csharp
EspTool espTool  = new EspTool(
                    "COM10",
                    1500000,
                    "dio",
                    40,
                    null,
                    VerbosityLevel.Quiet);
```

Next the ESP32 device needs to be queried so the application can be made aware of it's details:

```csharp
Esp32DeviceInfo esp32Device = espTool.GetDeviceDetails(null, false);
```

And finally the firmware can be updated in a single call:

```csharp
await Esp32Operations.UpdateFirmwareAsync(
        espTool,
        esp32Device,
        null,
        true,
        null,
        false,
        null,
        null,
        null,
        false,
        VerbosityLevel.Quiet,
        null);
```

The operation returns an exit code reporting success or failure of the update operation.

### Show nano device details

This console application gets (and optionally prints) details about a .NET nanoFramework device connected to the PC.
A couple of lines of code it's all it takes:

```csharp
var nanoDeviceOperations = new NanoDeviceOperations();

NanoDeviceBase nanoDevice = null;
nanoDeviceOperations.GetDeviceDetails(
    "COM22",
    ref nanoDevice);
```

Here's a sample output from the above code:

```text
HAL build info: nanoCLR running @ ORGPAL_PALTHREE built with ChibiOS v2021.11.2.11
  Target:   ORGPAL_PALTHREE
  Platform: STM32F7

Firmware build Info:
  Date:        Aug 18 2022
  Type:        MinSizeRel build with ChibiOS v2021.11.2.11
  CLR Version: 1.8.0.495
  Compiler:    GNU ARM GCC v10.3.1

OEM Product codes (vendor, model, SKU): 0, 0, 0

Serial Numbers (module, system):
  00000000000000000000000000000000
  0000000000000000

Target capabilities:
  Has nanoBooter: YES
  nanoBooter: v1.8.0.495
  IFU capable: NO
  Has proprietary bootloader: NO

AppDomains:

Assemblies:
  FileAccess, 1.0.0.0
  System.IO.Streams, 1.1.9.9530
  System.IO.FileSystem, 1.1.2.59752
  mscorlib, 1.12.0.4

Native Assemblies:
  mscorlib v100.5.0.17, checksum 0x004CF1CE
  nanoFramework.Runtime.Native v100.0.9.0, checksum 0x109F6F22
  nanoFramework.Hardware.Stm32 v100.0.4.4, checksum 0x0874B6FE
  nanoFramework.Networking.Sntp v100.0.4.4, checksum 0xE2D9BDED
  nanoFramework.ResourceManager v100.0.0.1, checksum 0xDCD7DF4D
  nanoFramework.System.Collections v100.0.1.0, checksum 0x2DC2B090
  nanoFramework.System.Text v100.0.0.1, checksum 0x8E6EB73D
  nanoFramework.Runtime.Events v100.0.8.0, checksum 0x0EAB00C9
  EventSink v1.0.0.0, checksum 0xF32F4C3E
  System.IO.FileSystem v1.0.0.0, checksum 0x3AB74021
```

### Update the CLR of a connected nano device

This console application updates the CLR of a .NET nanoFramework device connected to the PC.
A couple of lines of code is all it takes.

>Note 1: this operation requires that the target device has been already programmed .NET nanoFramework firmware. It's meant to perform updates using the serial COM port that's used for Visual Studio debugging without requiring any JTAG or other specific hardware connection.
>Note 2: because the nano device capabilities can be queried there's no need to specify the target name or any other details, making the operation rather smooth and simple to execute.

```csharp
var nanoDeviceOperations = new NanoDeviceOperations();
UpdateDeviceClrAsync(
    "COM55",
    VerbosityLevel.Nornal);
```