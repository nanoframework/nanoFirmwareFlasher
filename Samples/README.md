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
