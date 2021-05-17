//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommandLine;
using System;
using System.Reflection;
using System.Threading.Tasks;
using nanoFramework.Tools.FirmwareFlasher.Extensions;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommandLine.Text;
using System.IO;
using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class Program
    {
        private static ExitCodes _exitCode;
        private static string _extraMessage;
        private static VerbosityLevel verbosityLevel = VerbosityLevel.Normal;
        private static AssemblyInformationalVersionAttribute informationalVersionAttribute;
        private static string headerInfo;
        private static CopyrightInfo copyrightInfo;

        internal static string ExecutingPath;

        public static async Task<int> Main(string[] args)
        {
            // take care of static fields
            informationalVersionAttribute = Attribute.GetCustomAttribute(
                Assembly.GetEntryAssembly(),
                typeof(AssemblyInformationalVersionAttribute))
            as AssemblyInformationalVersionAttribute;

            headerInfo = $"nanoFramework Firmware Flasher v{informationalVersionAttribute.InformationalVersion}";

            copyrightInfo = new CopyrightInfo(true, $"nanoFramework project contributors", 2019);

            // need this to be able to use ProcessStart at the location where the .NET Core CLI tool is running from
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            var fullPath = Uri.UnescapeDataString(uri.Path);
            ExecutingPath = Path.GetDirectoryName(fullPath);
            
            // check for empty argument collection
            if (!args.Any())
            {
                // no argument provided, show help text and usage examples

                // because of short-comings in CommandLine parsing 
                // need to customize the output to provide a consistent output
                var parser = new Parser(config => config.HelpWriter = null);
                var result = parser.ParseArguments<Options>(new string[] { "", ""});

                var helpText = new HelpText(
                    new HeadingInfo(headerInfo),
                    copyrightInfo)
                        .AddPreOptionsLine("No command was provided.")
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine(HelpText.RenderUsageText(result))
                        .AddPreOptionsLine("")
                        .AddOptions(result);

                Console.WriteLine(helpText.ToString());

                return (int)ExitCodes.OK;
            }

            var parsedArguments = Parser.Default.ParseArguments<Options>(args);

            await parsedArguments
                .WithParsedAsync(opts => RunOptionsAndReturnExitCodeAsync(opts))
                .WithNotParsedAsync(errors => HandleErrorsAsync(errors));

            if (verbosityLevel > VerbosityLevel.Quiet)
            {
                OutputError(_exitCode, verbosityLevel > VerbosityLevel.Normal, _extraMessage);
            }

            return (int)_exitCode;
        }

        static Task HandleErrorsAsync(IEnumerable<Error> errors)
        {
            _exitCode = ExitCodes.E9000;
            return Task.CompletedTask;
        }

        static async Task RunOptionsAndReturnExitCodeAsync(Options o)
        {
            #region parse verbosity option

            switch (o.Verbosity)
            {
                // quiet
                case "q":
                case "quiet":
                    verbosityLevel = VerbosityLevel.Quiet;
                    break;

                // minimal
                case "m":
                case "minimal":
                    verbosityLevel = VerbosityLevel.Minimal;
                    break;

                // normal
                case "n":
                case "normal":
                    verbosityLevel = VerbosityLevel.Normal;
                    break;

                // detailed
                case "d":
                case "detailed":
                    verbosityLevel = VerbosityLevel.Detailed;
                    break;

                // diagnostic
                case "diag":
                case "diagnostic":
                    verbosityLevel = VerbosityLevel.Diagnostic;
                    break;

                default:
                    throw new ArgumentException("Invalid option for Verbosity");
            }

            #endregion

            Console.WriteLine(headerInfo);
            Console.WriteLine(copyrightInfo);
            Console.WriteLine();

            #region target processing

            // if a target name was specified, try to be smart and set the platform accordingly (in case it wasn't specified)
            if (string.IsNullOrEmpty(o.Platform)
                && !string.IsNullOrEmpty(o.TargetName))
            {
                // easiest one: ESP32
                if (o.TargetName.Contains("ESP32"))
                {
                    o.Platform = "esp32";
                }
                else if (
                    o.TargetName.Contains("ST") ||
                    o.TargetName.Contains("MBN_QUAIL") ||
                    o.TargetName.Contains("NETDUINO3") ||
                    o.TargetName.Contains("GHI FEZ") ||
                    o.TargetName.Contains("IngenuityMicro") ||
                    o.TargetName.Contains("ORGPAL")
                )
                {
                    // candidates for STM32
                    o.Platform = "stm32";
                }
                else if (
                   o.TargetName.Contains("TI_CC1352")
                )
                {
                    // candidates for TI CC13x2
                    o.Platform = "cc13x2";
                }
                else
                {
                    // other supported platforms will go here
                    // in case a wacky target is entered by the user, the package name will be checked against Cloudsmith repo
                }
            }

            #endregion

            #region platform specific options

            // if an option was specified and has an obvious platform, try to be smart and set the platform accordingly (in case it wasn't specified)
            if (string.IsNullOrEmpty(o.Platform))
            {
                // JTAG related
                if ( 
                    o.ListJtagDevices ||
                    !string.IsNullOrEmpty(o.JtagDeviceId) ||
                    o.HexFile.Any() ||
                    o.BinFile.Any())
                {
                    o.Platform = "stm32";
                }
                // DFU related
                else if ( 
                    o.ListDevicesInDfuMode ||
                    !string.IsNullOrEmpty(o.DfuDeviceId) ||
                    !string.IsNullOrEmpty(o.DfuFile))
                {
                    o.Platform = "stm32";
                }
                // ESP32 related
                else if (
                    !string.IsNullOrEmpty(o.SerialPort) ||
                    (o.BaudRate != 921600) ||
                    (o.Esp32FlashMode != "dio") ||
                    (o.Esp32FlashFrequency != 40))
                {
                    o.Platform = "esp32";
                }
            }

            #endregion


            #region ESP32 platform options

            if (o.Platform == "esp32")
            {
                // COM port is mandatory for ESP32
                if (string.IsNullOrEmpty(o.SerialPort))
                {
                    _exitCode = ExitCodes.E6001;
                    return;
                }
                
                EspTool espTool;

                try
                {
                    espTool = new EspTool(
                        o.SerialPort,
                        o.BaudRate,
                        o.Esp32FlashMode,
                        o.Esp32FlashFrequency,
                        o.Esp32PartitionTableSize);
                }
                catch(Exception)
                {
                    _exitCode = ExitCodes.E4005;
                    return;
                }

                EspTool.DeviceInfo esp32Device;

                if (espTool.ComPortAvailable)
                {
                    try
                    {
                        esp32Device = espTool.TestChip();
                    }
                    catch (EspToolExecutionException ex)
                    {
                        _exitCode = ExitCodes.E4000;
                        _extraMessage = ex.Message;

                        return;
                    }
                }
                else
                {
                    // couldn't open COM port
                    // done here, this command has no further processing
                    _exitCode = ExitCodes.E6000;

                    return;
                }

                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.WriteLine($"Connected to ESP32 { esp32Device.ChipName } with MAC address { esp32Device.MacAddress }");
                    Console.WriteLine($"features { esp32Device.Features }");

                    string flashSize = esp32Device.FlashSize >= 0x10000 ? $"{ esp32Device.FlashSize / 0x100000 }MB" : $"{ esp32Device.FlashSize / 0x400 }kB";

                    Console.WriteLine($"Flash information: manufacturer 0x{ esp32Device.FlashManufacturerId } device 0x{ esp32Device.FlashDeviceModelId } size { flashSize }");
                }

                // set verbosity
                espTool.Verbosity = verbosityLevel;

                // backup requested
                if (!string.IsNullOrEmpty(o.BackupPath) ||
                   !string.IsNullOrEmpty(o.BackupFile))
                {
                    try
                    {
                        // backup path specified, backup deployment
                        _exitCode = Esp32Operations.BackupFlash(espTool, esp32Device, o.BackupPath, o.BackupFile, verbosityLevel);
                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }
                    }
                    catch (ReadEsp32FlashException ex)
                    {
                        _exitCode = ExitCodes.E4004;
                        _extraMessage = ex.Message;

                        // done here
                        return;
                    }
                }

                // update operation requested?
                if (o.Update)
                {
                    try
                    {
                        // write flash
                        _exitCode = await Esp32Operations.UpdateFirmwareAsync(
                            espTool,
                            esp32Device,
                            o.TargetName,
                            true,
                            o.FwVersion,
                            o.Stable,
                            o.DeploymentImage,
                            null,
                            o.Esp32PartitionTableSize,
                            verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }

                        // done here
                        _exitCode = ExitCodes.OK;
                        return;
                    }
                    catch (ReadEsp32FlashException ex)
                    {
                        _exitCode = ExitCodes.E4004;
                        _extraMessage = ex.Message;
                    }
                    catch (WriteEsp32FlashException ex)
                    {
                        _exitCode = ExitCodes.E4003;
                        _extraMessage = ex.Message;
                    }
                    catch (EspToolExecutionException ex)
                    {
                        _exitCode = ExitCodes.E4000;
                        _extraMessage = ex.Message;
                    }
                }

                // it's OK to deploy after update
                if (o.Deploy)
                {
                    // need to take care of flash address
                    string appFlashAddress = null;

                    if (o.FlashAddress.Any())
                    {
                        // take the first address, it should be the only one valid
                        appFlashAddress = o.FlashAddress.ElementAt(0);
                    }
                    else
                    {
                        _exitCode = ExitCodes.E9009;
                        return;
                    }

                    // this to flash a deployment image without updating the firmware
                    try
                    {
                        // write flash
                        _exitCode = await Esp32Operations.UpdateFirmwareAsync(
                            espTool,
                            esp32Device,
                            null,
                            false,
                            null,
                            false,
                            o.DeploymentImage,
                            appFlashAddress,
                            o.Esp32PartitionTableSize,
                            verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }

                        // done here
                        _exitCode = ExitCodes.OK;
                        return;
                    }
                    catch (ReadEsp32FlashException ex)
                    {
                        _exitCode = ExitCodes.E4004;
                        _extraMessage = ex.Message;
                    }
                    catch (WriteEsp32FlashException ex)
                    {
                        _exitCode = ExitCodes.E4003;
                        _extraMessage = ex.Message;
                    }
                    catch (EspToolExecutionException ex)
                    {
                        _exitCode = ExitCodes.E4000;
                        _extraMessage = ex.Message;
                    }
                }

                // done here
                return;
            }

            #endregion

            #region STM32 platform options

            if (o.Platform == "stm32")
            {
                if (o.ListDevicesInDfuMode)
                {
                    var connecteDevices = StmDfuDevice.ListDfuDevices();

                    if (connecteDevices.Count == 0)
                    {
                        Console.WriteLine("No DFU devices found");
                    }
                    else
                    {
                        Console.WriteLine("-- Connected DFU devices --");

                        foreach (string deviceId in connecteDevices)
                        {
                            Console.WriteLine(deviceId);
                        }

                        Console.WriteLine("---------------------------");
                    }

                    // done here, this command has no further processing
                    _exitCode = ExitCodes.OK;

                    return;
                }

                if (o.ListJtagDevices)
                {
                    try
                    {
                        var connecteDevices = StmJtagDevice.ListDevices();

                        if (connecteDevices.Count == 0)
                        {
                            Console.WriteLine("No JTAG devices found");
                        }
                        else
                        {
                            Console.WriteLine("-- Connected JTAG devices --");

                            foreach (string deviceId in connecteDevices)
                            {
                                Console.WriteLine(deviceId);
                            }

                            Console.WriteLine("---------------------------");
                        }

                        // done here, this command has no further processing
                        _exitCode = ExitCodes.OK;
                    }
                    catch (Exception ex)
                    {
                        // exception with 
                        _exitCode = ExitCodes.E5000;
                        _extraMessage = ex.Message;
                    }

                    return;
                }

                var connectedStDfuDevices = StmDfuDevice.ListDfuDevices();
                var connectedStJtagDevices = StmJtagDevice.ListDevices();

                if (!string.IsNullOrEmpty(o.DfuFile) && connectedStDfuDevices.Count != 0)
                {
                    // there is a DFU file argument, so follow DFU path
                    var dfuDevice = new StmDfuDevice(o.DfuDeviceId);

                    if (!dfuDevice.DevicePresent)
                    {
                        // no DFU device found

                        // done here, this command has no further processing
                        _exitCode = ExitCodes.E1000;

                        return;
                    }

                    if (verbosityLevel >= VerbosityLevel.Normal)
                    {
                        Console.WriteLine($"Connected to DFU device with ID { dfuDevice.DeviceId }");
                    }

                    // set verbosity
                    dfuDevice.Verbosity = verbosityLevel;

                    // get mass erase option
                    dfuDevice.DoMassErase = o.MassErase;

                    try
                    {
                        dfuDevice.FlashDfuFile(o.DfuFile);

                        // done here, this command has no further processing
                        _exitCode = ExitCodes.OK;

                        return;
                    }
                    catch (DfuFileDoesNotExistException)
                    {
                        // DFU file doesn't exist
                        _exitCode = ExitCodes.E1002;
                    }
                    catch (Exception ex)
                    {
                        // exception with DFU operation
                        _exitCode = ExitCodes.E1003;
                        _extraMessage = ex.Message;
                    }
                }
                else if (
                    o.BinFile.Any() &&
                    o.HexFile.Any() &&
                    connectedStJtagDevices.Count != 0
                     )
                {
                    // this has to be a JTAG connected device

#region STM32 JTAG options

                    try
                    {
                        var jtagDevice = new StmJtagDevice(o.JtagDeviceId);

                        if (!jtagDevice.DevicePresent)
                        {
                            // no JTAG device found

                            // done here, this command has no further processing
                            _exitCode = ExitCodes.E5001;

                            return;
                        }

                        if (verbosityLevel >= VerbosityLevel.Normal)
                        {
                            Console.WriteLine($"Connected to JTAG device with ID { jtagDevice.DeviceId }");
                        }

                        // set verbosity
                        jtagDevice.Verbosity = verbosityLevel;

                        // get mass erase option
                        jtagDevice.DoMassErase = o.MassErase;

                        if (o.HexFile.Any())
                        {
                            _exitCode = jtagDevice.FlashHexFiles(o.HexFile);

                            // done here
                            return;
                        }

                        if (o.BinFile.Any())
                        {
                            _exitCode = jtagDevice.FlashBinFiles(o.BinFile, o.FlashAddress);

                            // done here
                            return;
                        }
                    }
                    catch (CantConnectToJtagDeviceException)
                    {
                        // done here, this command has no further processing
                        _exitCode = ExitCodes.E5002;
                    }

#endregion
                }
                else if (!string.IsNullOrEmpty(o.TargetName))
                {
                    // update operation requested?
                    if (o.Update)
                    {
                        // this to update the device with fw from Cloudsmith

                        // need to take care of flash address
                        string appFlashAddress = null;

                        if (o.FlashAddress.Any())
                        {
                            // take the first address, it should be the only one valid
                            appFlashAddress = o.FlashAddress.ElementAt(0);
                        }

                        _exitCode = await Stm32Operations.UpdateFirmwareAsync(
                            o.TargetName,
                            o.FwVersion,
                            o.Stable,
                            true,
                            o.DeploymentImage,
                            appFlashAddress,
                            o.DfuDeviceId,
                            o.JtagDeviceId,
                            verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }
                    }

                    // it's OK to deploy after update
                    if (o.Deploy)
                    {
                        // this to flash a deployment image without updating the firmware

                        // need to take care of flash address
                        string appFlashAddress = null;

                        if (o.FlashAddress.Any())
                        {
                            // take the first address, it should be the only one valid
                            appFlashAddress = o.FlashAddress.ElementAt(0);
                        }
                        else
                        {
                            _exitCode = ExitCodes.E9009;
                            return;
                        }

                        _exitCode = await Stm32Operations.UpdateFirmwareAsync(
                                        o.TargetName,
                                        null,
                                        false,
                                        false,
                                        o.DeploymentImage,
                                        appFlashAddress,
                                        o.DfuDeviceId,
                                        o.JtagDeviceId,
                                        verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }
                    }

                    // reset MCU requested?
                    if (o.ResetMcu)
                    {
                        _exitCode = Stm32Operations.ResetMcu(
                                        o.JtagDeviceId,
                                        verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }
                    }
                }
            }

#endregion


#region TI CC13x2 platform options

            if (o.Platform == "cc13x2")
            {
                if (!string.IsNullOrEmpty(o.TargetName))
                {
                    // update operation requested?
                    if (o.Update)
                    {
                        // this to update the device with fw from Cloudsmith

                        // need to take care of flash address
                        string appFlashAddress = null;

                        if (o.FlashAddress.Any())
                        {
                            // take the first address, it should be the only one valid
                            appFlashAddress = o.FlashAddress.ElementAt(0);
                        }

                        _exitCode = await CC13x26x2Operations.UpdateFirmwareAsync(
                            o.TargetName,
                            o.FwVersion,
                            o.Stable,
                            true,
                            o.DeploymentImage,
                            appFlashAddress,
                            verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }
                    }

                    // it's OK to deploy after update
                    if (o.Deploy)
                    {
                        // this to flash a deployment image without updating the firmware

                        // need to take care of flash address
                        string appFlashAddress = null;

                        if (o.FlashAddress.Any())
                        {
                            // take the first address, it should be the only one valid
                            appFlashAddress = o.FlashAddress.ElementAt(0);
                        }
                        else
                        {
                            _exitCode = ExitCodes.E9009;
                            return;
                        }

                        _exitCode = await CC13x26x2Operations.UpdateFirmwareAsync(
                                        o.TargetName,
                                        null,
                                        false,
                                        false,
                                        o.DeploymentImage,
                                        appFlashAddress,
                                        verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }
                    }

                    // reset MCU requested?
                    if (o.ResetMcu)
                    {
                        // can't reset CC13x2 device without configuration file
                        // would require to specify the exact target name and then had to try parsing that 
                        _exitCode = ExitCodes.E9000;

                        // done here
                        return;
                    }
                }

                if(o.TIInstallXdsDrivers)
                {

                    _exitCode = CC13x26x2Operations.InstallXds110Drivers(verbosityLevel);

                    if (_exitCode != ExitCodes.OK)
                    {
                        // done here
                        return;
                    }
                }
            }

#endregion

        }

        private static void OutputError(ExitCodes errorCode, bool outputMessage, string extraMessage = null)
        {
            if (errorCode != ExitCodes.OK)
            {
                if (outputMessage)
                {
                    Console.Write($"Error {errorCode}");
                }
                else
                {
                    Console.Write($"{errorCode}");
                }

                if (outputMessage)
                {
                    var exitCodeDisplayName = errorCode.GetAttribute<DisplayAttribute>();

                    if (!string.IsNullOrEmpty(exitCodeDisplayName.Name))
                    {
                        Console.Write($": { exitCodeDisplayName.Name }");
                    }

                    if (string.IsNullOrEmpty(extraMessage))
                    {
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.WriteLine($" ({ extraMessage })");
                    }
                }
                else
                {
                    Console.WriteLine("");
                }
            }
        }
    }
}
