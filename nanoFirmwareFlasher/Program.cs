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
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.IO.Ports;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class Program
    {
        private static ExitCodes _exitCode;
        private static string _extraMessage;
        private static VerbosityLevel _verbosityLevel = VerbosityLevel.Normal;
        private static AssemblyInformationalVersionAttribute _informationalVersionAttribute;
        private static string _headerInfo;
        private static CopyrightInfo _copyrightInfo;

        internal static string ExecutingPath;

        public static async Task<int> Main(string[] args)
        {
            // take care of static fields
            _informationalVersionAttribute = Attribute.GetCustomAttribute(
                Assembly.GetEntryAssembly()!,
                typeof(AssemblyInformationalVersionAttribute))
            as AssemblyInformationalVersionAttribute;

            _headerInfo = $".NET nanoFramework Firmware Flasher v{_informationalVersionAttribute.InformationalVersion}";

            _copyrightInfo = new CopyrightInfo(true, $".NET Foundation and nanoFramework project contributors", 2019);

            // need this to be able to use ProcessStart at the location where the .NET Core CLI tool is running from
            string codeBase = Assembly.GetExecutingAssembly().Location;
            var fullPath = Path.GetFullPath(codeBase);
            ExecutingPath = Path.GetDirectoryName(fullPath);

            // check for empty argument collection
            if (!args.Any())
            {
                // no argument provided, show help text and usage examples

                // because of short-comings in CommandLine parsing 
                // need to customize the output to provide a consistent output
                var parser = new Parser(config => config.HelpWriter = null);
                var result = parser.ParseArguments<Options>(new[] { "", "" });

                var helpText = new HelpText(
                    new HeadingInfo(_headerInfo),
                    _copyrightInfo)
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
                .WithParsedAsync(RunOptionsAndReturnExitCodeAsync)
                .WithNotParsedAsync(HandleErrorsAsync);

            if (_verbosityLevel > VerbosityLevel.Quiet)
            {
                OutputError(_exitCode, _verbosityLevel > VerbosityLevel.Normal, _extraMessage);
            }

            return (int)_exitCode;
        }

        private static void CheckVersion()
        {
            Version latestVersion;
            Version currentVersion = Version.Parse(_informationalVersionAttribute.InformationalVersion.Split('+')[0]);

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("nanoff", currentVersion.ToString()));

                HttpResponseMessage response = client.GetAsync("https://api.github.com/repos/nanoframework/nanoFirmwareFlasher/releases/latest").Result;

                dynamic responseContent = JsonConvert.DeserializeObject(response.Content.ReadAsStringAsync().Result);
                string tagName = responseContent.tag_name.ToString();

                latestVersion = Version.Parse(tagName.Substring(1));
            }

            if (latestVersion > currentVersion)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("** There is a new version available, update is recommended **");
                Console.WriteLine("** You should consider updating via the 'dotnet tool update -g nanoff' command **");
                Console.WriteLine("** If you have it installed on a specific path please check the instructions here: https://git.io/JiU0C **");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private static Task HandleErrorsAsync(IEnumerable<Error> errors)
        {
            _exitCode = ExitCodes.E9000;
            return Task.CompletedTask;
        }

        static async Task RunOptionsAndReturnExitCodeAsync(Options o)
        {
            bool operationPerformed = false;

            #region parse verbosity option

            switch (o.Verbosity)
            {
                // quiet
                case "q":
                case "quiet":
                    _verbosityLevel = VerbosityLevel.Quiet;
                    break;

                // minimal
                case "m":
                case "minimal":
                    _verbosityLevel = VerbosityLevel.Minimal;
                    break;

                // normal
                case "n":
                case "normal":
                    _verbosityLevel = VerbosityLevel.Normal;
                    break;

                // detailed
                case "d":
                case "detailed":
                    _verbosityLevel = VerbosityLevel.Detailed;
                    break;

                // diagnostic
                case "diag":
                case "diagnostic":
                    _verbosityLevel = VerbosityLevel.Diagnostic;
                    break;

                default:
                    throw new ArgumentException("Invalid option for Verbosity");
            }

            #endregion

            Console.ForegroundColor = ConsoleColor.White;

            Console.WriteLine(_headerInfo);
            Console.WriteLine(_copyrightInfo);
            Console.WriteLine();

            // perform version check
            CheckVersion();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.White;

            if (o.ClearCache)
            {
                Console.WriteLine();

                if (Directory.Exists(FirmwarePackage.LocationPathBase))
                {
                    Console.WriteLine("Clearing firmware cache location.");

                    try
                    {
                        Directory.Delete(FirmwarePackage.LocationPathBase);
                    }
                    catch (Exception ex)
                    {
                        _exitCode = ExitCodes.E9014;
                        _extraMessage = ex.Message;
                    }
                }
                else
                {
                    Console.WriteLine("Firmware cache location does not exist. Nothing to do.");
                }

                return;
            }

            if (o.ListComPorts)
            {
                var ports = SerialPort.GetPortNames();
                if (ports.Any())
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("Available COM ports:");
                    foreach (var p in ports)
                    {
                        Console.WriteLine($"  {p}");
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No available COM port.");
                }

                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.White;
                return;
            }

            #region list targets

            // First check if we are asked for the list of boards
            if (o.ListTargets ||
                o.ListBoards)
            {
                if (o.ListBoards && _verbosityLevel > VerbosityLevel.Quiet)
                {
                    // warn about deprecated option
                    Console.ForegroundColor = ConsoleColor.Yellow;

                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine("********************************** WARNING **********************************");
                    Console.WriteLine("The --listboards option is deprecated and will be removed in a future version");
                    Console.WriteLine("Please use --listtargets option instead");
                    Console.WriteLine("*****************************************************************************");
                    Console.WriteLine("");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;
                }

                // get list from REFERENCE targets
                var targets = FirmwarePackage.GetTargetList(
                    false,
                    o.Preview,
                    o.Platform,
                    _verbosityLevel);

                // append list from COMMUNITY targets
                targets = targets.Concat(
                    FirmwarePackage.GetTargetList(
                    true,
                    o.Preview,
                    o.Platform,
                    _verbosityLevel)).ToList();

                Console.WriteLine("Available targets:");

                DisplayBoardDetails(targets);

                return;
            }

            #endregion

            #region target processing

            // if a target name was specified, try to be smart and set the platform accordingly (in case it wasn't specified)
            if (o.Platform == null
                && !string.IsNullOrEmpty(o.TargetName))
            {
                // easiest one: ESP32
                if (o.TargetName.StartsWith("ESP")
                    || o.TargetName.StartsWith("M5")
                    || o.TargetName.StartsWith("Pyb")
                    || o.TargetName.StartsWith("FEATHER")
                    || o.TargetName.StartsWith("ESPKALUGA"))
                {
                    o.Platform = SupportedPlatform.esp32;
                }
                else if (
                    o.TargetName.StartsWith("ST")
                    || o.TargetName.StartsWith("MBN_QUAIL")
                    || o.TargetName.StartsWith("NETDUINO3")
                    || o.TargetName.StartsWith("GHI")
                    || o.TargetName.StartsWith("IngenuityMicro")
                    || o.TargetName.StartsWith("WeAct")
                    || o.TargetName.StartsWith("ORGPAL")
                )
                {
                    // candidates for STM32
                    o.Platform = SupportedPlatform.stm32;
                }
                else if (
                   o.TargetName.StartsWith("TI")
                )
                {
                    // candidates for TI CC13x2
                    o.Platform = SupportedPlatform.ti_simplelink;
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
            if (o.Platform == null)
            {
                // JTAG related
                if (
                    o.ListJtagDevices ||
                    !string.IsNullOrEmpty(o.JtagDeviceId) ||
                    o.HexFile.Any() ||
                    o.BinFile.Any())
                {
                    o.Platform = SupportedPlatform.stm32;
                }
                // DFU related
                else if (
                    o.ListDevicesInDfuMode ||
                    o.DfuUpdate ||
                    !string.IsNullOrEmpty(o.DfuDeviceId))
                {
                    o.Platform = SupportedPlatform.stm32;
                }
                // ESP32 related
                else if (
                    !string.IsNullOrEmpty(o.SerialPort) ||
                    (o.BaudRate != 921600) ||
                    (o.Esp32FlashMode != "dio") ||
                    (o.Esp32FlashFrequency != 40) ||
                    !string.IsNullOrEmpty(o.Esp32ClrFile))
                {
                    o.Platform = SupportedPlatform.esp32;
                }
                // drivers install
                else if (o.TIInstallXdsDrivers)
                {
                    o.Platform = SupportedPlatform.ti_simplelink;
                }
                else if (
                    o.InstallDfuDrivers
                    || o.InstallJtagDrivers)
                {
                    o.Platform = SupportedPlatform.stm32;
                }
            }

            #endregion

            #region ESP32 platform options

            if (o.Platform == SupportedPlatform.esp32)
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
                        o.Esp32PartitionTableSize,
                        _verbosityLevel);
                }
                catch (Exception)
                {
                    _exitCode = ExitCodes.E4005;
                    return;
                }

                Esp32DeviceInfo esp32Device;

                if (espTool.ComPortAvailable)
                {
                    try
                    {
                        esp32Device = espTool.GetDeviceDetails(o.TargetName, o.Esp32PartitionTableSize == null);
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

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;

                    Console.WriteLine("");
                    Console.WriteLine($"Connected to:");
                    Console.WriteLine($"{ esp32Device }");

                    Console.ForegroundColor = ConsoleColor.White;

                    // if this is a PICO and baud rate is not 115200 or 1M5, operations will most likely fail
                    // warn user about this
                    if (
                        esp32Device.ChipName.Contains("ESP32-PICO")
                        && (o.BaudRate != 115200
                            && o.BaudRate != 1500000))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;

                        Console.WriteLine("");
                        Console.WriteLine("****************************** WARNING ******************************");
                        Console.WriteLine("The connected device it's an ESP32 PICO which can be picky about the ");
                        Console.WriteLine("baud rate used. Recommendation is to use --baud 115200 ");
                        Console.WriteLine("*********************************************************************");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;
                    }
                }

                // set verbosity
                espTool.Verbosity = _verbosityLevel;

                // backup requested
                if (!string.IsNullOrEmpty(o.BackupPath) ||
                   !string.IsNullOrEmpty(o.BackupFile))
                {
                    try
                    {
                        // backup path specified, backup deployment
                        _exitCode = Esp32Operations.BackupFlash(espTool, esp32Device, o.BackupPath, o.BackupFile, _verbosityLevel);

                        if (_exitCode != ExitCodes.OK)
                        {
                            // done here
                            return;
                        }

                        operationPerformed = true;
                    }
                    catch (ReadEsp32FlashException ex)
                    {
                        _exitCode = ExitCodes.E4004;
                        _extraMessage = ex.Message;

                        // done here
                        return;
                    }
                }

                // show device details
                if (o.DeviceDetails)
                {
                    // device details already output
                    _exitCode = ExitCodes.OK;

                    // done here
                    return;
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
                            o.Preview,
                            o.DeploymentImage,
                            null,
                            o.Esp32ClrFile,
                            !o.FitCheck,
                            _verbosityLevel,
                            o.Esp32PartitionTableSize);

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
                    string appFlashAddress = string.Empty;

                    if (o.FlashAddress.Any())
                    {
                        // take the first address, it should be the only one valid
                        appFlashAddress = o.FlashAddress.ElementAt(0);
                    }

                    // this to flash a deployment image without updating the firmware
                    try
                    {
                        // write flash
                        _exitCode = await Esp32Operations.UpdateFirmwareAsync(
                            espTool,
                            esp32Device,
                            o.TargetName,
                            false,
                            null,
                            false,
                            o.DeploymentImage,
                            appFlashAddress,
                            null,
                            !o.FitCheck,
                            _verbosityLevel,
                            o.Esp32PartitionTableSize);

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

            if (o.Platform == SupportedPlatform.stm32)
            {
                if (o.InstallDfuDrivers)
                {
                    _exitCode = Stm32Operations.InstallDfuDrivers(_verbosityLevel);

                    // done here
                    return;
                }

                if (o.InstallJtagDrivers)
                {
                    _exitCode = Stm32Operations.InstallJtagDrivers(_verbosityLevel);

                    // done here
                    return;
                }

                if (o.ListDevicesInDfuMode)
                {
                    var connecteDevices = StmDfuDevice.ListDevices();

                    Console.ForegroundColor = ConsoleColor.Cyan;

                    if (connecteDevices.Count() == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("No DFU devices found");
                    }
                    else
                    {
                        Console.WriteLine("-- Connected DFU devices --");

                        foreach ((string serial, string device) device in connecteDevices)
                        {
                            Console.WriteLine($"{device.serial} @ {device.device}");
                        }

                        Console.WriteLine("---------------------------");
                    }

                    Console.ForegroundColor = ConsoleColor.White;

                    // done here, this command has no further processing
                    _exitCode = ExitCodes.OK;

                    return;
                }

                if (o.ListJtagDevices)
                {
                    try
                    {
                        var connecteDevices = StmJtagDevice.ListDevices();

                        Console.ForegroundColor = ConsoleColor.Cyan;

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

                    Console.ForegroundColor = ConsoleColor.White;

                    return;
                }

                var connectedStDfuDevices = StmDfuDevice.ListDevices();
                var connectedStJtagDevices = StmJtagDevice.ListDevices();

                if (o.BinFile.Any() &&
                    o.HexFile.Any() &&
                    connectedStDfuDevices.Count != 0)
                {

                    #region STM32 DFU options

                    try
                    {
                        var dfuDevice = new StmDfuDevice(o.DfuDeviceId);

                        if (!dfuDevice.DevicePresent)
                        {
                            // no JTAG device found

                            // done here, this command has no further processing
                            _exitCode = ExitCodes.E5001;

                            return;
                        }

                        if (_verbosityLevel >= VerbosityLevel.Normal)
                        {
                            Console.WriteLine($"Connected to JTAG device with ID { dfuDevice.DfuId }");
                        }

                        // set verbosity
                        dfuDevice.Verbosity = _verbosityLevel;

                        // get mass erase option
                        dfuDevice.DoMassErase = o.MassErase;

                        if (o.HexFile.Any())
                        {
                            _exitCode = dfuDevice.FlashHexFiles(o.HexFile);

                            // done here
                            return;
                        }

                        if (o.BinFile.Any())
                        {
                            _exitCode = dfuDevice.FlashBinFiles(o.BinFile, o.FlashAddress);

                            // done here
                            return;
                        }
                    }
                    catch (CantConnectToDfuDeviceException)
                    {
                        // done here, this command has no further processing
                        _exitCode = ExitCodes.E1005;
                    }

                    #endregion

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

                        if (_verbosityLevel >= VerbosityLevel.Normal)
                        {
                            Console.WriteLine($"Connected to JTAG device with ID { jtagDevice.JtagId }");
                        }

                        // set verbosity
                        jtagDevice.Verbosity = _verbosityLevel;

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
                        // this to update the device with fw from CloudSmith

                        // need to take care of flash address
                        string appFlashAddress = null;

                        if (o.FlashAddress.Any())
                        {
                            // take the first address, it should be the only one valid
                            appFlashAddress = o.FlashAddress.ElementAt(0);
                        }

                        Interface updateInterface = Interface.None;

                        if (o.DfuUpdate && o.JtagUpdate)
                        {
                            // can't select both JTAG and DFU simultaneously
                            _exitCode = ExitCodes.E9000;
                            return;
                        }
                        else if (o.DfuUpdate)
                        {
                            updateInterface = Interface.Dfu;
                        }
                        else if (o.JtagUpdate)
                        {
                            updateInterface = Interface.Jtag;
                        }

                        _exitCode = await Stm32Operations.UpdateFirmwareAsync(
                            o.TargetName,
                            o.FwVersion,
                            o.Preview,
                            true,
                            o.DeploymentImage,
                            appFlashAddress,
                            o.DfuDeviceId,
                            o.JtagDeviceId,
                            !o.FitCheck,
                            updateInterface,
                            _verbosityLevel);

                        operationPerformed = true;

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
                        string appFlashAddress;

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

                        Interface updateInterface = Interface.None;

                        if (o.DfuUpdate && o.JtagUpdate)
                        {
                            // can't select both JTAG and DFU simultaneously
                            _exitCode = ExitCodes.E9000;
                            return;
                        }
                        else if (o.DfuUpdate)
                        {
                            updateInterface = Interface.Dfu;
                        }
                        else if (o.JtagUpdate)
                        {
                            updateInterface = Interface.Jtag;
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
                                        !o.FitCheck,
                                        updateInterface,
                                        _verbosityLevel);

                        operationPerformed = true;

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
                                        _verbosityLevel);

                        // done here
                        return;
                    }
                }
                else if (o.MassErase)
                {
                    _exitCode = Stm32Operations.MassErase(
                                    o.JtagDeviceId,
                                    _verbosityLevel);

                    // done here
                    return;
                }
                else if (o.ResetMcu)
                {
                    _exitCode = Stm32Operations.ResetMcu(
                                    o.JtagDeviceId,
                                    _verbosityLevel);

                    // done here
                    return;
                }
            }

            #endregion


            #region TI CC13x2 platform options

            if (o.Platform == SupportedPlatform.ti_simplelink)
            {
                if (o.TIInstallXdsDrivers)
                {
                    _exitCode = CC13x26x2Operations.InstallXds110Drivers(_verbosityLevel);

                    // done here
                    return;
                }

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
                            o.Preview,
                            true,
                            o.DeploymentImage,
                            appFlashAddress,
                            _verbosityLevel);

                        operationPerformed = true;

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
                                        _verbosityLevel);

                        operationPerformed = true;

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
            }

            #endregion

            // done nothing... or maybe not...
            if (!operationPerformed)
            {
                // because of short-comings in CommandLine parsing 
                // need to customize the output to provide a consistent output
                var parser = new Parser(config => config.HelpWriter = null);
                var result = parser.ParseArguments<Options>(new[] { "", "" });

                var helpText = new HelpText(
                    new HeadingInfo(_headerInfo),
                    _copyrightInfo)
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine("No operation was performed with the options supplied.")
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine(HelpText.RenderUsageText(result))
                        .AddPreOptionsLine("")
                        .AddOptions(result);

                Console.WriteLine(helpText.ToString());
            }
        }

        private static void DisplayBoardDetails(List<CloudSmithPackageDetail> boards)
        {
            foreach (var boardName in boards.Select(m => m.Name).Distinct())
            {
                Console.WriteLine($"  {boardName}");

                foreach (var board in boards.Where(m => m.Name == boardName))
                {
                    Console.WriteLine($"    {board.Version}");
                }
            }
        }

        private static void OutputError(ExitCodes errorCode, bool outputMessage, string extraMessage = null)
        {
            if (errorCode == ExitCodes.OK)
            {
                return;
            }

            Console.ForegroundColor = ConsoleColor.Red;

            if (outputMessage)
            {
                Console.Write($"Error {errorCode}");

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
                Console.Write($"{errorCode}");
                Console.WriteLine();
            }

            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
