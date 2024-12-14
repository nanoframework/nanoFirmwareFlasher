// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Configuration;
using nanoFramework.Tools.FirmwareFlasher.Extensions;
using nanoFramework.Tools.FirmwareFlasher.FileDeployment;
using Newtonsoft.Json;

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
        private static NanoDeviceOperations _nanoDeviceOperations;

        public static async Task<int> Main(string[] args)
        {
            // take care of static fields
            _informationalVersionAttribute = Attribute.GetCustomAttribute(
                // Cannot be Assembly.GetEntryAssembly()! as that fails in tests
                typeof(Program).Assembly,
                typeof(AssemblyInformationalVersionAttribute))
            as AssemblyInformationalVersionAttribute;

            _headerInfo = $".NET nanoFramework Firmware Flasher v{_informationalVersionAttribute.InformationalVersion}";

            _copyrightInfo = new CopyrightInfo(true, $".NET Foundation and nanoFramework project contributors", 2019);

            // for tests
            _exitCode = ExitCodes.OK;
            _extraMessage = null;
            _verbosityLevel = VerbosityLevel.Quiet;

            // need this to be able to use ProcessStart at the location where the .NET Core CLI tool is running from
            string codeBase = Assembly.GetExecutingAssembly().Location;
            var fullPath = Path.GetFullPath(codeBase);
            var ExecutingPath = Path.GetDirectoryName(fullPath);

            // grab AppInsights connection string to setup telemetry client
            IConfigurationRoot appConfigurationRoot = new ConfigurationBuilder()
                .SetBasePath(ExecutingPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            NanoTelemetryClient.ConnectionString = appConfigurationRoot?["iConnectionString"];

            // check for empty argument collection
            if (!args.Any())
            {
                // no argument provided, show help text and usage examples

                // because of short-comings in CommandLine parsing 
                // need to customize the output to provide a consistent output
                var parser = new Parser(config => config.HelpWriter = null);
                var result = parser.ParseArguments<Options>(new string[] { "", "" });

                var helpText = new HelpText(
                    new HeadingInfo(_headerInfo),
                    _copyrightInfo)
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine("INFO: No command was provided.")
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine("For the full list of commands and options use --help.")
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine("Follows some examples on how to use nanoff. For more detailed explanations please check:")
                        .AddPreOptionsLine("https://github.com/nanoframework/nanoFirmwareFlasher#usage")
                        .AddPreOptionsLine("")
                        .AddPreOptionsLine(HelpText.RenderUsageText(result))
                        .AddPreOptionsLine("");

                OutputWriter.WriteLine(helpText.ToString());

#if !VS_CODE_EXTENSION_BUILD
                // perform version check
                CheckVersion();
                OutputWriter.WriteLine();
#endif

                return (int)ExitCodes.OK;
            }

            var parsedArguments = Parser.Default.ParseArguments<Options>(args);

            await parsedArguments
                .WithParsedAsync(RunOptionsAndReturnExitCodeAsync)
                .WithNotParsedAsync(HandleErrorsAsync);

            if (_verbosityLevel > VerbosityLevel.Quiet)
            {
                OutputError(_exitCode, _verbosityLevel >= VerbosityLevel.Normal, _extraMessage);
            }

            // force clean-up
            _nanoDeviceOperations?.Dispose();

            return (int)_exitCode;
        }

        private static void CheckVersion()
        {
            try
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
            catch (Exception)
            {
                OutputWriter.ForegroundColor = ConsoleColor.DarkYellow;
                OutputWriter.WriteLine("** Can't check the version! **");
                OutputWriter.WriteLine("** Continuing anyway. **");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }
        }

        private static Task HandleErrorsAsync(IEnumerable<Error> errors)
        {
            if (errors.All(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.VersionRequestedError))
            {
                return Task.CompletedTask;
            }
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

            OutputWriter.ForegroundColor = ConsoleColor.White;

            OutputWriter.WriteLine(_headerInfo);
            OutputWriter.WriteLine(_copyrightInfo);
            OutputWriter.WriteLine();


#if !VS_CODE_EXTENSION_BUILD
            if (!o.SuppressNanoFFVersionCheck)
            {
                // perform version check
                CheckVersion();
                OutputWriter.WriteLine();
            }
#endif

            OutputWriter.ForegroundColor = ConsoleColor.White;

            if (o.ClearCache)
            {
                OutputWriter.WriteLine();

                if (Directory.Exists(FirmwarePackage.LocationPathBase))
                {
                    OutputWriter.WriteLine("Clearing firmware cache location.");

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
                    OutputWriter.WriteLine("Firmware cache location does not exist. Nothing to do.");
                }

                return;
            }

            if (o.ListComPorts)
            {
                var ports = SerialPort.GetPortNames();
                if (ports.Any())
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine("Available COM ports:");
                    foreach (var p in ports)
                    {
                        OutputWriter.WriteLine($"  {p}");
                    }
                }
                else
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("No available COM port.");
                }

                OutputWriter.WriteLine();

                OutputWriter.ForegroundColor = ConsoleColor.White;
                return;
            }

            #region list targets

            // First check if we are asked for the list of available targets
            if (o.ListTargets)
            {
                List<CloudSmithPackageDetail> targets;
                if (o.FromFwArchive)
                {
                    if (string.IsNullOrEmpty(o.FwArchivePath))
                    {
                        _exitCode = ExitCodes.E9000;
                        _extraMessage = "--archivepath is required when --fromarchive is specified.";
                        return;
                    }

                    // get the list from the archive
                    targets = new FirmwareArchiveManager(o.FwArchivePath).GetTargetList(
                        o.Preview,
                        o.Platform,
                        _verbosityLevel);
                }
                else
                {
                    // get list from REFERENCE targets
                    targets = FirmwarePackage.GetTargetList(
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
                }
                OutputWriter.WriteLine("Available targets:");

                DisplayBoardDetails(targets);

                return;
            }

            #endregion

            #region nano device management

            if (o.ListDevices)
            {
                _nanoDeviceOperations = new NanoDeviceOperations();

                try
                {
                    var connectedDevices = _nanoDeviceOperations.ListDevices(_verbosityLevel > VerbosityLevel.Normal);

                    if (!connectedDevices.Any())
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine("No devices found");
                    }
                    else
                    {
                        OutputWriter.WriteLine("-- Connected .NET nanoFramework devices --");

                        foreach (var nanoDevice in connectedDevices)
                        {
                            OutputWriter.WriteLine($"{nanoDevice.Description}");

                            if (_verbosityLevel >= VerbosityLevel.Normal)
                            {
                                // check that we are in CLR
                                if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                                {
                                    // we have to have a valid device info
                                    if (nanoDevice.DeviceInfo.Valid)
                                    {
                                        OutputWriter.WriteLine($"  Target:      {nanoDevice.DeviceInfo.TargetName?.ToString()}");
                                        OutputWriter.WriteLine($"  Platform:    {nanoDevice.DeviceInfo.Platform?.ToString()}");
                                        OutputWriter.WriteLine($"  Date:        {nanoDevice.DebugEngine.Capabilities.SoftwareVersion.BuildDate ?? "unknown"}");
                                        OutputWriter.WriteLine($"  Type:        {nanoDevice.DebugEngine.Capabilities.SolutionReleaseInfo.VendorInfo ?? "unknown"}");
                                        OutputWriter.WriteLine($"  CLR Version: {nanoDevice.DeviceInfo.SolutionBuildVersion}");
                                    }
                                }
                                else
                                {
                                    // we are in booter, can only get TargetInfo
                                    // we have to have a valid device info
                                    if (nanoDevice.DebugEngine.TargetInfo != null)
                                    {
                                        OutputWriter.WriteLine($"  Target:         {nanoDevice.DebugEngine.TargetInfo.TargetName}");
                                        OutputWriter.WriteLine($"  Platform:       {nanoDevice.DebugEngine.TargetInfo.PlatformName}");
                                        OutputWriter.WriteLine($"  Type:           {nanoDevice.DebugEngine.TargetInfo.PlatformInfo}");
                                        OutputWriter.WriteLine($"  CLR Version:    {nanoDevice.DebugEngine.TargetInfo.CLRVersion}");
                                        OutputWriter.WriteLine($"  Booter Version: {nanoDevice.DebugEngine.TargetInfo.CLRVersion}");
                                    }
                                }

                                OutputWriter.WriteLine("");
                            }
                        }

                        OutputWriter.WriteLine("------------------------------------------");
                    }

                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
                catch (Exception ex)
                {
                    _exitCode = ExitCodes.E2001;
                    _extraMessage = ex.Message;
                }

                // done here, this command has no further processing
                _exitCode = ExitCodes.OK;

                return;
            }

            if (o.NanoDevice)
            {
                // check for invalid options passed with nano device operations
                if (o.Platform.HasValue
                    || !string.IsNullOrEmpty(o.TargetName))
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = "Incompatible options combined with --nanodevice.";
                    return;
                }

                var manager = new NanoDeviceManager(o, _verbosityLevel);

                // COM port is mandatory for nano device operations
                if (string.IsNullOrEmpty(o.SerialPort))
                {
                    _exitCode = ExitCodes.E6001;
                }
                else
                {
                    try
                    {
                        _exitCode = await manager.ProcessAsync();
                    }
                    catch (CantConnectToNanoDeviceException ex)
                    {
                        _exitCode = ExitCodes.E2001;
                        _extraMessage = ex.Message;
                    }
                    catch (NoOperationPerformedException)
                    {
                        DisplayNoOperationMessage();
                    }
                    catch (Exception ex)
                    {
                        _exitCode = ExitCodes.E2002;
                        _extraMessage = ex.Message;
                    }
                }

                return;
            }

            #endregion

            #region target processing

            // if a target name was specified, try to be smart and set the platform accordingly (in case it wasn't specified)
            if (!string.IsNullOrEmpty(o.TargetName))
            {
                // check for invalid options passed with platform option
                if (o.NanoDevice || o.IdentifyFirmware)
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = "Incompatible options combined with --targetname.";
                    return;
                }
                if (o.Platform == null)
                {
                    // easiest one: ESP32
                    if (o.TargetName.StartsWith("ESP")
                        || o.TargetName.StartsWith("M5")
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
                        || o.TargetName.StartsWith("Pyb")
                        || o.TargetName.StartsWith("NESHTEC_NESHNODE_V")
                    )
                    {
                        // candidates for STM32
                        o.Platform = SupportedPlatform.stm32;
                    }
                    else if (o.TargetName.StartsWith("TI"))
                    {
                        // candidates for TI CC13x2
                        o.Platform = SupportedPlatform.ti_simplelink;
                    }
                    else if (o.TargetName.StartsWith("SL"))
                    {
                        // candidates for Silabs GG11
                        o.Platform = SupportedPlatform.gg11;
                    }
                    else
                    {
                        // other supported platforms will go here
                        // in case a wacky target is entered by the user, the package name will be checked against Cloudsmith repo
                    }
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
                // GG11 related
                else if (o.ListJLinkDevices)
                {
                    o.Platform = SupportedPlatform.gg11;
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
                // ESP32 related
                else if (
                    !string.IsNullOrEmpty(o.SerialPort) &&
                    ((o.BaudRate != 921600) ||
                    (o.Esp32FlashMode != "dio") ||
                    (o.Esp32FlashFrequency != 40)))
                {
                    o.Platform = SupportedPlatform.esp32;
                }
            }

            #endregion

            #region firmware archive update if no device is required
            if (o.UpdateFwArchive)
            {
                // check for invalid options passed with platform option
                if (o.FromFwArchive)
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = "Incompatible option --fromarchive combined with --updatearchive.";
                    return;
                }
                if (string.IsNullOrEmpty(o.FwArchivePath))
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = $"--archivepath is required when --updatearchive is specified.";
                    return;
                }

                if (o.Platform is null && string.IsNullOrEmpty(o.TargetName))
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = $"--platform or --target is required when --updatearchive is specified.";
                    return;
                }

                if (o.KeepAllFwVersions && string.IsNullOrEmpty(o.TargetName))
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = $"--keepallversions can only be used with --target and not when --platform is specified.";
                    return;
                }

                // The packages can be downloaded without device connection
                _exitCode = await new FirmwareArchiveManager(o.FwArchivePath).DownloadFirmwareFromRepository(
                    o.Preview,
                    o.Platform,
                    o.TargetName,
                    o.FwVersion,
                    o.KeepAllFwVersions,
                    _verbosityLevel);
                return;
            }

            // From now on the archive can only be used as a source of firmware
            if (string.IsNullOrWhiteSpace(o.FwArchivePath))
            {
                if (o.FromFwArchive)
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = $"--archivepath is required when --fromarchive is specified.";
                    return;
                }
            }
            else if (!o.FromFwArchive)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = $"--fromarchive is required when --archivepath is specified.";
                return;
            }
            #endregion

            #region ESP32 platform options

            if (o.Platform == SupportedPlatform.esp32)
            {
                var manager = new Esp32Manager(o, _verbosityLevel);

                try
                {
                    _exitCode = await manager.ProcessAsync();
                }
                catch (EspToolExecutionException ex)
                {
                    _exitCode = ExitCodes.E4000;
                    _extraMessage = ex.Message;
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
                catch (NoOperationPerformedException)
                {
                    DisplayNoOperationMessage();
                }
                catch (Exception ex)
                {
                    // exception with 
                    _exitCode = ExitCodes.E4000;
                    _extraMessage = ex.Message;
                }

                operationPerformed = true;
            }

            #endregion

            #region STM32 platform options

            if (o.Platform == SupportedPlatform.stm32)
            {
                var manager = new Stm32Manager(o, _verbosityLevel);

                try
                {
                    _exitCode = await manager.ProcessAsync();
                }
                catch (CantConnectToDfuDeviceException)
                {
                    // done here, this command has no further processing
                    _exitCode = ExitCodes.E1005;
                }
                catch (CantConnectToJtagDeviceException)
                {
                    // done here, this command has no further processing
                    _exitCode = ExitCodes.E5002;
                }
                catch (NoOperationPerformedException)
                {
                    DisplayNoOperationMessage();
                }
                catch (Exception ex)
                {
                    // exception with 
                    _exitCode = ExitCodes.E5000;
                    _extraMessage = ex.Message;
                }

                operationPerformed = true;
            }

            #endregion

            #region TI CC13x2 platform options

            if (o.Platform == SupportedPlatform.ti_simplelink)
            {
                var manager = new TIManager(o, _verbosityLevel);

                try
                {
                    _exitCode = await manager.ProcessAsync();
                }
                catch (NoOperationPerformedException)
                {
                    DisplayNoOperationMessage();
                }
                catch (Exception ex)
                {
                    // exception with 
                    _exitCode = ExitCodes.E5000;
                    _extraMessage = ex.Message;
                }

                operationPerformed = true;
            }

            #endregion

            #region Silabs Giant Gecko S1 platform options

            if (o.Platform == SupportedPlatform.gg11)
            {
                var manager = new SilabsManager(o, _verbosityLevel);

                try
                {
                    _exitCode = await manager.ProcessAsync();
                }
                catch (CantConnectToJLinkDeviceException)
                {
                    // done here, this command has no further processing
                    _exitCode = ExitCodes.E8001;
                }
                catch (SilinkExecutionException)
                {
                    // done here, this command has no further processing
                    _exitCode = ExitCodes.E8002;
                }
                catch (NoOperationPerformedException)
                {
                    DisplayNoOperationMessage();
                }
                catch (Exception ex)
                {
                    // exception with 
                    _exitCode = ExitCodes.E8000;
                    _extraMessage = ex.Message;
                }

                operationPerformed = true;
            }

            #endregion

            // done nothing... or maybe not...
            if (!operationPerformed && string.IsNullOrEmpty(o.FileDeployment))
            {
                DisplayNoOperationMessage();
            }
            else
            {
                if ((_exitCode == ExitCodes.OK) && !string.IsNullOrEmpty(o.FileDeployment))
                {
                    FileDeploymentManager deploy = new FileDeploymentManager(o.FileDeployment, o.SerialPort, _verbosityLevel);
                    try
                    {
                        _exitCode = await deploy.DeployAsync();
                    }
                    catch (Exception ex)
                    {
                        // exception with 
                        _exitCode = ExitCodes.E2003;
                        _extraMessage = ex.Message;
                    }
                }
            }
        }

        private static void DisplayNoOperationMessage()
        {
            // because of short-comings in CommandLine parsing 
            // need to customize the output to provide a consistent output
            var parser = new Parser(config => config.HelpWriter = null);
            var result = parser.ParseArguments<Options>(new string[] { "", "" });

            var helpText = new HelpText(
                new HeadingInfo(_headerInfo),
                _copyrightInfo)
                    .AddPreOptionsLine("")
                    .AddPreOptionsLine("No operation was performed with the options supplied.")
                    .AddPreOptionsLine("")
                    .AddPreOptionsLine(HelpText.RenderUsageText(result))
                    .AddPreOptionsLine("")
                    .AddOptions(result);

            OutputWriter.WriteLine(helpText.ToString());
        }

        private static void DisplayBoardDetails(List<CloudSmithPackageDetail> boards)
        {
            foreach (var boardName in boards.Select(m => m.Name).Distinct())
            {
                OutputWriter.WriteLine($"  {boardName}");

                foreach (var board in boards.Where(m => m.Name == boardName).OrderBy(m => m.Name).Take(3))
                {
                    OutputWriter.WriteLine($"    {board.Version}");
                }
            }
        }

        private static void OutputError(ExitCodes errorCode, bool outputMessage, string extraMessage = null)
        {
            if (errorCode == ExitCodes.OK)
            {
                return;
            }

            OutputWriter.ForegroundColor = ConsoleColor.Red;

            if (outputMessage)
            {
                OutputWriter.Write($"Error {errorCode}");

                var exitCodeDisplayName = errorCode.GetAttribute<DisplayAttribute>();

                if (!string.IsNullOrEmpty(exitCodeDisplayName.Name))
                {
                    OutputWriter.Write($": {exitCodeDisplayName.Name}");
                }

                if (string.IsNullOrEmpty(extraMessage))
                {
                    OutputWriter.WriteLine();
                }
                else
                {
                    OutputWriter.WriteLine($" ({extraMessage})");
                }
            }
            else
            {
                OutputWriter.Write($"{errorCode}");
                OutputWriter.WriteLine();
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;
        }
    }
}
