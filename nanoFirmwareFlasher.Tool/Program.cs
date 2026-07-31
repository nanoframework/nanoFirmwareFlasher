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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Configuration;
using nanoFramework.Tools.FirmwareFlasher.Extensions;
using nanoFramework.Tools.FirmwareFlasher.FileDeployment;
using nanoFramework.Tools.FirmwareFlasher.NetworkDeployment;

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
                        .AddPreOptionsLine("  nanoff flash target ESP_WROVER_KIT")
                        .AddPreOptionsLine("  nanoff flash platform esp32 serialport COM7")
                        .AddPreOptionsLine("  nanoff list targets platform stm32")
                        .AddPreOptionsLine("  nanoff details platform rpi_pico serialport COM11")
                        .AddPreOptionsLine("");

                OutputWriter.WriteLine(helpText.ToString());

#if !VS_CODE_EXTENSION_BUILD
                // perform version check
                CheckVersion();
                OutputWriter.WriteLine();
#endif

                return (int)ExitCodes.OK;
            }

            // verbs + words syntax is the only supported syntax from here on
            // (e.g. "flash target ESP_WROVER_KIT masserase"); the legacy flat
            // "--flag value" syntax was removed as part of the breaking change
            // to nanoff's major version bump.
            await RunVerbAsync(args);

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

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    };

                    JsonNode responseContent = JsonSerializer.Deserialize<JsonNode>(response.Content.ReadAsStringAsync().Result, options);
                    string tagName = responseContent["tag_name"].ToString();

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
                if (_verbosityLevel > VerbosityLevel.Quiet)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.DarkYellow;
                    OutputWriter.WriteLine("** Can't check the version! **");
                    OutputWriter.WriteLine("** Continuing anyway. **");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }
        }

        private static Task HandleErrorsAsync(IEnumerable<Error> errors)
        {
            if (errors.All(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.HelpVerbRequestedError || e.Tag == ErrorType.VersionRequestedError))
            {
                return Task.CompletedTask;
            }
            _exitCode = ExitCodes.E9000;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Example command lines shown in each verb's help screen (<c>nanoff &lt;verb&gt; help</c>
        /// / <c>nanoff &lt;verb&gt; --help</c>), keyed by that verb's option class.
        /// </summary>
        private static readonly IReadOnlyDictionary<Type, string[]> s_verbExamples = new Dictionary<Type, string[]>
        {
            [typeof(FlashOptions)] = new[]
            {
                "nanoff flash target ESP_WROVER_KIT serialport COM31",
                "nanoff flash target ST_STM32F769I_DISCOVERY interface jtag",
                "nanoff flash platform esp32 serialport COM31 masserase",
                "nanoff flash serialport COM9 clrfile C:\\nf-interpreter\\build\\nanoclr.bin",
            },
            [typeof(DeployOptions)] = new[]
            {
                "nanoff deploy target ESP32_PSRAM_REV0 serialport COM31 image app.bin",
                "nanoff deploy target ST_STM32F769I_DISCOVERY image app.bin address 0x08040000",
                "nanoff deploy file C:\\path\\deploy.json",
                "nanoff deploy network C:\\path\\deploy.json",
            },
            [typeof(ListOptions)] = new[]
            {
                "nanoff list ports",
                "nanoff list devices",
                "nanoff list targets platform esp32",
                "nanoff list dfu",
            },
            [typeof(DetailsOptions)] = new[]
            {
                "nanoff details platform esp32 serialport COM31",
                "nanoff details serialport COM9",
            },
            [typeof(IdentifyOptions)] = new[]
            {
                "nanoff identify platform esp32 serialport COM31",
            },
            [typeof(DriversOptions)] = new[]
            {
                "nanoff drivers dfu",
                "nanoff drivers jtag",
                "nanoff drivers xds",
            },
            [typeof(CacheOptions)] = new[]
            {
                "nanoff cache clear",
                "nanoff cache download platform esp32 archivepath c:\\firmware",
            },
        };

        /// <summary>
        /// Builds and prints help for the verb parser, adding an "Examples:" section for the
        /// specific verb being asked about (<c>nanoff &lt;verb&gt; help</c>/<c>--help</c>), or
        /// the list of verbs when no specific verb was requested (<c>nanoff help</c>/<c>--help</c>).
        /// </summary>
        /// <param name="result">The verb parser result (Parsed or NotParsed).</param>
        /// <param name="args">The original (pre-normalization) arguments, used to detect which verb, if any, was requested.</param>
        private static void DisplayVerbHelp(ParserResult<object> result, string[] args)
        {
            Type requestedVerbType = args.Length > 0 && VerbTokenizer.KnownVerbs.TryGetValue(args[0], out Type verbType)
                ? verbType
                : null;

            var helpText = HelpText.AutoBuild(
                result,
                h =>
                {
                    if (requestedVerbType != null
                        && s_verbExamples.TryGetValue(requestedVerbType, out string[] examples))
                    {
                        h.AddPreOptionsLine("");
                        h.AddPreOptionsLine("Examples:");
                        foreach (string example in examples)
                        {
                            h.AddPreOptionsLine($"  {example}");
                        }
                    }

                    return h;
                },
                e => e,
                verbsIndex: true);

            OutputWriter.WriteLine(helpText.ToString());
        }

        /// <summary>
        /// Entry point for the new verbs + words syntax: normalizes the bare-word
        /// arguments, parses them against the per-verb option classes, and dispatches
        /// to the matching <c>Run*Async</c> method.
        /// </summary>
        private static async Task RunVerbAsync(string[] args)
        {
            string[] normalizedArgs = VerbTokenizer.Normalize(args);

            var verbParserResult = new Parser(config => config.HelpWriter = null)
                .ParseArguments<FlashOptions, DeployOptions, ListOptions, DetailsOptions, IdentifyOptions, DriversOptions, CacheOptions>(normalizedArgs);

            if (verbParserResult is Parsed<object> parsed)
            {
                switch (parsed.Value)
                {
                    case FlashOptions flashOptions:
                        await RunFlashAsync(flashOptions);
                        break;

                    case DeployOptions deployOptions:
                        await RunDeployAsync(deployOptions);
                        break;

                    case ListOptions listOptions:
                        await RunListAsync(listOptions);
                        break;

                    case DetailsOptions detailsOptions:
                        await RunDetailsAsync(detailsOptions);
                        break;

                    case IdentifyOptions identifyOptions:
                        await RunIdentifyAsync(identifyOptions);
                        break;

                    case DriversOptions driversOptions:
                        await RunDriversAsync(driversOptions);
                        break;

                    case CacheOptions cacheOptions:
                        await RunCacheAsync(cacheOptions);
                        break;
                }
            }
            else if (verbParserResult is NotParsed<object> notParsed)
            {
                if (notParsed.Errors.Any(e => e.Tag == ErrorType.VersionRequestedError))
                {
                    OutputWriter.WriteLine(_headerInfo);
                }
                else if (notParsed.Errors.Any(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.HelpVerbRequestedError))
                {
                    DisplayVerbHelp(verbParserResult, args);
                }

                await HandleErrorsAsync(notParsed.Errors);
            }
        }

        private static async Task RunFlashAsync(FlashOptions o)
        {
            _verbosityLevel = o.GetVerbosityLevel();

            string validationError = FlashOptions.Validate(o);

            if (validationError != null)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = validationError;
                return;
            }

            await RunOptionsAndReturnExitCodeAsync(o.ToLegacyOptions());
        }

        private static async Task RunDeployAsync(DeployOptions o)
        {
            _verbosityLevel = o.GetVerbosityLevel();

            string validationError = DeployOptions.Validate(o);

            if (validationError != null)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = validationError;
                return;
            }

            await RunOptionsAndReturnExitCodeAsync(o.ToLegacyOptions());
        }

        private static async Task RunListAsync(ListOptions o)
        {
            _verbosityLevel = o.GetVerbosityLevel();

            string validationError = ListOptions.Validate(o);

            if (validationError != null)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = validationError;
                return;
            }

            await RunOptionsAndReturnExitCodeAsync(o.ToLegacyOptions());
        }

        private static async Task RunDetailsAsync(DetailsOptions o)
        {
            await RunOptionsAndReturnExitCodeAsync(o.ToLegacyOptions());
        }

        private static async Task RunIdentifyAsync(IdentifyOptions o)
        {
            await RunOptionsAndReturnExitCodeAsync(o.ToLegacyOptions());
        }

        private static async Task RunCacheAsync(CacheOptions o)
        {
            _verbosityLevel = o.GetVerbosityLevel();

            string validationError = CacheOptions.Validate(o);

            if (validationError != null)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = validationError;
                return;
            }

            await RunOptionsAndReturnExitCodeAsync(o.ToLegacyOptions());
        }

        /// <summary>
        /// The <c>drivers</c> verb is handled directly rather than through the legacy
        /// dispatch: <c>drivers dfu</c>/<c>drivers jtag</c> now print install instructions
        /// instead of running an installer (see proposal doc); only <c>drivers xds</c>
        /// still runs the existing installer.
        /// </summary>
        private static Task RunDriversAsync(DriversOptions o)
        {
            _verbosityLevel = o.GetVerbosityLevel();

            string validationError = DriversOptions.Validate(o);

            if (validationError != null)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = validationError;
                return Task.CompletedTask;
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;
            OutputWriter.WriteLine(_headerInfo);
            OutputWriter.WriteLine(_copyrightInfo);
            OutputWriter.WriteLine();

            if (o.Dfu)
            {
                OutputWriter.WriteLine("To flash STM32 devices via USB DFU, install the WinUSB driver for the device's");
                OutputWriter.WriteLine("DFU bootloader interface: put the device in DFU mode and use Zadig (zadig.akeo.ie)");
                OutputWriter.WriteLine("to install the WinUSB driver for the 'STM32 BOOTLOADER' USB device.");
                OutputWriter.WriteLine("No driver installation is required on Linux or macOS.");
                _exitCode = ExitCodes.OK;
            }
            else if (o.Jtag)
            {
                OutputWriter.WriteLine("To flash STM32 devices via JTAG/SWD, install the ST-LINK USB driver: download the");
                OutputWriter.WriteLine("'STSW-LINK009' ST-LINK driver package from STMicroelectronics' website, or install");
                OutputWriter.WriteLine("it via the STM32CubeProgrammer installer.");
                OutputWriter.WriteLine("No driver installation is required on Linux or macOS.");
                _exitCode = ExitCodes.OK;
            }
            else if (o.Xds)
            {
                _exitCode = CC13x26x2Operations.InstallXds110Drivers(_verbosityLevel);
            }

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
                    var connectedDevices = _nanoDeviceOperations.ListDevices(
                        _verbosityLevel > VerbosityLevel.Normal,
                        _verbosityLevel);

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
                        // candidates for Silabs EFM32 Gecko
                        o.Platform = SupportedPlatform.efm32;
                    }
                    else if (o.TargetName.StartsWith("RP_PICO")
                        || o.TargetName.StartsWith("RP2040")
                        || o.TargetName.StartsWith("RP2350")
                        || o.TargetName.StartsWith("PICO"))
                    {
                        // candidates for Raspberry Pi Pico (RP2040/RP2350)
                        o.Platform = SupportedPlatform.rpi_pico;
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
                    !string.IsNullOrEmpty(o.JtagDeviceId) ||
                    o.HexFile.Any() ||
                    o.BinFile.Any())
                {
                    o.Platform = SupportedPlatform.stm32;
                }
                // DFU related
                else if (!string.IsNullOrEmpty(o.DfuDeviceId))
                {
                    o.Platform = SupportedPlatform.stm32;
                }
                // EFM32 related
                else if (o.ListJLinkDevices)
                {
                    o.Platform = SupportedPlatform.efm32;
                }
                // drivers install
                else if (o.TIInstallXdsDrivers)
                {
                    o.Platform = SupportedPlatform.ti_simplelink;
                }
                // ESP32 related
                else if (
                    !string.IsNullOrEmpty(o.SerialPort) &&
                    // a pure file/network deployment uses the wire protocol and is platform independent:
                    // it must not be misclassified as an ESP32 firmware operation (which would connect through
                    // the esptool bootloader and leave the device unable to answer wire protocol requests)
                    string.IsNullOrEmpty(o.FileDeployment) &&
                    string.IsNullOrEmpty(o.NetworkDeployment) &&
                    ((o.BaudRate != 921600) ||
                    (o.Esp32FlashMode != "dio") ||
                    (o.Esp32FlashFrequency != 40)))
                {
                    o.Platform = SupportedPlatform.esp32;
                }
            }

            #endregion

            // --deploy requires --image
            if (o.Deploy && string.IsNullOrEmpty(o.DeploymentImage))
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = "--deploy requires --image to specify the deployment image path.";
                return;
            }

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

                // The packages can be downloaded without device connection
                _exitCode = await new FirmwareArchiveManager(o.FwArchivePath).DownloadFirmwareFromRepository(
                    o.Preview,
                    o.Platform,
                    o.TargetName,
                    o.FwVersion,
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

            if (o.Platform == SupportedPlatform.efm32)
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

            #region Raspberry Pi Pico platform options

            if (o.Platform == SupportedPlatform.rpi_pico)
            {
                var manager = new PicoManager(o, _verbosityLevel);

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
                    _exitCode = ExitCodes.E3000;
                    _extraMessage = ex.Message;
                }

                operationPerformed = true;
            }

            #endregion

            #region Files and Network deployment

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
                        operationPerformed = true;
                    }
                    catch (Exception ex)
                    {
                        // exception with 
                        _exitCode = ExitCodes.E2003;
                        _extraMessage = ex.Message;
                    }
                }
            }

            // done nothing... or maybe not...
            if (!operationPerformed && string.IsNullOrEmpty(o.NetworkDeployment))
            {
                DisplayNoOperationMessage();
            }
            else
            {
                if ((_exitCode == ExitCodes.OK) && !string.IsNullOrEmpty(o.NetworkDeployment))
                {
                    NetworkDeploymentManager deploy = new NetworkDeploymentManager(o.NetworkDeployment, o.SerialPort, _verbosityLevel);
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

            #endregion
        }

        private static void DisplayNoOperationMessage()
        {
            var helpText = new HelpText(
                new HeadingInfo(_headerInfo),
                _copyrightInfo)
                    .AddPreOptionsLine("")
                    .AddPreOptionsLine("No operation was performed with the options supplied.")
                    .AddPreOptionsLine("")
                    .AddPreOptionsLine("Use 'nanoff help' or 'nanoff <verb> --help' (e.g. 'nanoff flash --help') for usage information.");

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
