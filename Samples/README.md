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

nanoDeviceOperations.GetDeviceDetails("COM22");
```

Here's a sample output from the above code:

```text

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