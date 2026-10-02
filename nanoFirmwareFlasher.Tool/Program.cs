// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
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
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;
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

            TelemetrySetup.Initialize(
                appConfigurationRoot?["iConnectionString"],
                _informationalVersionAttribute.InformationalVersion);

            // one activity per invocation, records the command, options used and outcome
            using Activity commandActivity = CommandTelemetry.Start();

            // check for empty argument collection
            if (!args.Any())
            {
                CommandTelemetry.RecordNoArguments(commandActivity);

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

                return CompleteRun(commandActivity, ExitCodes.OK);
            }

            // verbs + words syntax is the only supported syntax from here on
            // (e.g. "flash target ESP_WROVER_KIT masserase"); the legacy flat
            // "--flag value" syntax was removed as part of the breaking change
            // to nanoff's major version bump.
            try
            {
                await RunVerbAsync(args, commandActivity);
            }
            catch (Exception ex)
            {
                // unexpected exception: report it instead of crashing
                NanoTelemetry.TrackException(ex, "unhandled");

                _exitCode = ExitCodes.E9000;
                _extraMessage = ex.Message;
                _verbosityLevel = VerbosityLevel.Normal;
            }

            if (_verbosityLevel > VerbosityLevel.Quiet)
            {
                OutputError(_exitCode, _verbosityLevel >= VerbosityLevel.Normal, _extraMessage);
            }

            // force clean-up
            _nanoDeviceOperations?.Dispose();

            return CompleteRun(commandActivity, _exitCode);
        }

        /// <summary>
        /// Records the outcome of the command, flushes the telemetry and returns the exit code.
        /// </summary>
        private static int CompleteRun(Activity commandActivity, ExitCodes exitCode)
        {
            CommandTelemetry.Complete(commandActivity, exitCode);

            // stop the activity now so that it's exported before the telemetry pipeline shuts down
            commandActivity?.Stop();

            TelemetrySetup.Shutdown(TimeSpan.FromSeconds(2));

            return (int)exitCode;
        }

        private static void CheckVersion()
        {
            try
            {
                Version latestVersion;
                // keep the preview number as the 4th component: "3.0.0-preview.46" -> 3.0.0.46
                Version currentVersion = Version.Parse(_informationalVersionAttribute.InformationalVersion.Split('+')[0].Replace("-preview.", "."));

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

                    latestVersion = Version.Parse(tagName.Substring(1).Split('+')[0].Replace("-preview.", "."));
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
                "nanoff flash target ST_STM32F769I_DISCOVERY jtag",
                "nanoff flash platform esp32 serialport COM31 masserase",
                "nanoff flash serialport COM9 image C:\\nf-interpreter\\build\\nanoclr.bin",
                "nanoff flash serialport COM31 image nanoCLR.bin mcuboot signkey my-signing-key.pem",
            },
            [typeof(DeployOptions)] = new[]
            {
                "nanoff deploy target ESP32_PSRAM_REV0 serialport COM31 image app.bin",
                "nanoff deploy target ST_STM32F769I_DISCOVERY image app.bin address 0x08040000",
                "nanoff deploy file C:\\path\\deploy.json",
                "nanoff deploy network C:\\path\\deploy.json",
                "nanoff deploy serialport COM31 image deployment-signed.bin mcuboot",
            },
            [typeof(ListOptions)] = new[]
            {
                "nanoff list ports",
                "nanoff list devices",
                "nanoff list targets platform esp32",
                "nanoff list dfu",
                "nanoff list images mcuboot serialport COM31",
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
            [typeof(KeysOptions)] = new[]
            {
                "nanoff keys generate my-signing-key.pem",
                "nanoff keys getpub root-pub-key.c signkey my-signing-key.pem",
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
        private static async Task RunVerbAsync(string[] args, Activity commandActivity)
        {
            string[] normalizedArgs = VerbTokenizer.Normalize(args);

            var verbParserResult = new Parser(config => config.HelpWriter = null)
                .ParseArguments<FlashOptions, DeployOptions, ListOptions, DetailsOptions, IdentifyOptions, DriversOptions, CacheOptions, KeysOptions>(normalizedArgs);

            if (verbParserResult is Parsed<object> parsed)
            {
                CommandTelemetry.RecordParsed(commandActivity, parsed.Value, normalizedArgs);

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

                    case KeysOptions keysOptions:
                        await RunKeysAsync(keysOptions);
                        break;
                }
            }
            else if (verbParserResult is NotParsed<object> notParsed)
            {
                CommandTelemetry.RecordNotParsed(commandActivity, args, normalizedArgs, notParsed.Errors);

                if (notParsed.Errors.Any(e => e.Tag == ErrorType.VersionRequestedError))
                {
                    OutputWriter.WriteLine(_headerInfo);
                }
                else if (notParsed.Errors.Any(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.HelpVerbRequestedError))
                {
                    DisplayVerbHelp(verbParserResult, args);
                }
                else
                {
                    // the parser's HelpWriter is disabled, so parsing errors (e.g. an
                    // unknown option) aren't rendered automatically; do it explicitly
                    DisplayVerbHelp(verbParserResult, args);

                    // make sure Main() actually reports the resulting exit code
                    _verbosityLevel = VerbosityLevel.Normal;
                }

                await HandleErrorsAsync(notParsed.Errors);
            }
        }

        /// <summary>
        /// Common shape for every verb: set verbosity, run its <c>Validate</c> if it has one
        /// and bail out with E9000 on a validation error, otherwise map to <see cref="Options"/> and dispatch.
        /// </summary>
        private static async Task RunVerbOptionsAsync<T>(T o, Func<T, Options> toLegacyOptions, Func<T, string> validate = null) where T : VerbOptionsBase
        {
            try
            {
                _verbosityLevel = o.GetVerbosityLevel();
            }
            catch (ArgumentException)
            {
                _exitCode = ExitCodes.E9000;
                _verbosityLevel = VerbosityLevel.Normal;
                return;
            }

            string validationError = validate?.Invoke(o);

            if (validationError != null)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = validationError;
                return;
            }

            await RunOptionsAndReturnExitCodeAsync(toLegacyOptions(o));
        }

        private static Task RunFlashAsync(FlashOptions o) => RunVerbOptionsAsync(o, x => x.ToLegacyOptions(), FlashOptions.Validate);

        private static Task RunDeployAsync(DeployOptions o) => RunVerbOptionsAsync(o, x => x.ToLegacyOptions(), DeployOptions.Validate);

        private static Task RunListAsync(ListOptions o) => RunVerbOptionsAsync(o, x => x.ToLegacyOptions(), ListOptions.Validate);

        private static Task RunDetailsAsync(DetailsOptions o) => RunVerbOptionsAsync(o, x => x.ToLegacyOptions());

        private static Task RunIdentifyAsync(IdentifyOptions o) => RunVerbOptionsAsync(o, x => x.ToLegacyOptions());

        private static Task RunCacheAsync(CacheOptions o) => RunVerbOptionsAsync(o, x => x.ToLegacyOptions(), CacheOptions.Validate);

        /// <summary>
        /// The <c>drivers</c> verb is handled directly rather than through the legacy
        /// dispatch: <c>drivers dfu</c>/<c>drivers jtag</c> now print install instructions
        /// instead of running an installer (see proposal doc); only <c>drivers xds</c>
        /// still runs the existing installer.
        /// </summary>
        private static Task RunDriversAsync(DriversOptions o)
        {
            try
            {
                _verbosityLevel = o.GetVerbosityLevel();
            }
            catch (ArgumentException)
            {
                _exitCode = ExitCodes.E9000;
                _verbosityLevel = VerbosityLevel.Normal;
                return Task.CompletedTask;
            }

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

        /// <summary>
        /// The <c>keys</c> verb is handled directly rather than through the legacy dispatch:
        /// it only works on local files and never talks to a device.
        /// </summary>
        private static Task RunKeysAsync(KeysOptions o)
        {
            try
            {
                _verbosityLevel = o.GetVerbosityLevel();
            }
            catch (ArgumentException)
            {
                _exitCode = ExitCodes.E9000;
                _verbosityLevel = VerbosityLevel.Normal;
                return Task.CompletedTask;
            }

            string validationError = KeysOptions.Validate(o);

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

            try
            {
                if (!string.IsNullOrEmpty(o.Generate))
                {
                    var imageManager = new McubootImageManager(o.Generate, slotSize: 0) { Verbosity = _verbosityLevel };
                    _exitCode = imageManager.GenerateSigningKey(o.Generate);
                }
                else
                {
                    var imageManager = new McubootImageManager(o.SignKey, slotSize: 0) { Verbosity = _verbosityLevel };
                    _exitCode = imageManager.ExtractPublicKey(o.SignKey, o.GetPub);
                }
            }
            catch (ImgtoolNotFoundException)
            {
                // the exit code description already carries the installation hint
                _exitCode = ExitCodes.E10001;
            }
            catch (Exception ex)
            {
                NanoTelemetry.TrackException(ex, "keys");

                _exitCode = ExitCodes.E10004;
                _extraMessage = ex.Message;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Runs a platform manager's <see cref="IManager.ProcessAsync"/>, mapping specific
        /// exception types to their exit code (and optionally their message), with
        /// <paramref name="defaultExitCode"/>/message used for anything else unmapped.
        /// <see cref="NoOperationPerformedException"/> is always handled the same way.
        /// </summary>
        private static async Task RunManagerAsync(IManager manager, ExitCodes defaultExitCode, params (Type ExceptionType, ExitCodes ExitCode, bool IncludeMessage)[] exceptionMappings)
        {
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
                NanoTelemetry.TrackException(ex, manager.GetType().Name);

                var mapping = Array.Find(exceptionMappings, m => m.ExceptionType == ex.GetType());

                _exitCode = mapping.ExceptionType != null ? mapping.ExitCode : defaultExitCode;

                if (mapping.ExceptionType == null || mapping.IncludeMessage)
                {
                    _extraMessage = ex.Message;
                }
            }
        }

        static async Task RunOptionsAndReturnExitCodeAsync(Options o)
        {
            bool operationPerformed = false;

            try
            {
                _verbosityLevel = VerbOptionsBase.ParseVerbosity(o.Verbosity);
            }
            catch (ArgumentException)
            {
                _exitCode = ExitCodes.E9000;
                _verbosityLevel = VerbosityLevel.Normal;
                return;
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;

            OutputWriter.WriteLine(_headerInfo);
            OutputWriter.WriteLine(_copyrightInfo);
            OutputWriter.WriteLine();

            TelemetrySetup.ShowFirstRunNoticeIfNeeded(_verbosityLevel, _informationalVersionAttribute.InformationalVersion);

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
                        _extraMessage = "fromarchive requires archivepath to specify the firmware archive location.";
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
                // Look for devices in MCUboot serial recovery first
                // SMP probe is quick, and the ports that answer are then excluded from the Wire Protocol scan.
                List<McubootDiscoveredDevice> mcubootDevices = await ListMcubootDevicesAsync(o.SerialPort);

                _nanoDeviceOperations = new NanoDeviceOperations(mcubootDevices.Select(d => d.PortName));

                try
                {
                    // details are needed from Normal verbosity up, to show the nanoCLR/nanoBooter version
                    var connectedDevices = _nanoDeviceOperations.ListDevices(
                        _verbosityLevel >= VerbosityLevel.Normal,
                        _verbosityLevel);

                    if (!connectedDevices.Any()
                        && !mcubootDevices.Any())
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine("No devices found");
                    }
                    else if (connectedDevices.Any())
                    {
                        OutputWriter.WriteLine("-- nanoCLR / nanoBooter --");

                        foreach (var nanoDevice in connectedDevices)
                        {
                            OutputWriter.WriteLine($"{nanoDevice.Description}");

                            if (_verbosityLevel >= VerbosityLevel.Normal)
                            {
                                // the target name is already in the description, no need to repeat it
                                // check that we are in CLR
                                if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                                {
                                    // we have to have a valid device info
                                    if (nanoDevice.DeviceInfo.Valid)
                                    {
                                        OutputWriter.WriteLine($"  nanoCLR:     {nanoDevice.DeviceInfo.SolutionBuildVersion}");

                                        if (_verbosityLevel >= VerbosityLevel.Detailed)
                                        {
                                            OutputWriter.WriteLine($"  Platform:    {nanoDevice.DeviceInfo.Platform?.ToString()}");
                                            OutputWriter.WriteLine($"  Date:        {nanoDevice.DebugEngine.Capabilities.SoftwareVersion.BuildDate ?? "unknown"}");
                                            OutputWriter.WriteLine($"  Type:        {nanoDevice.DebugEngine.Capabilities.SolutionReleaseInfo.VendorInfo ?? "unknown"}");
                                        }
                                    }
                                }
                                else
                                {
                                    // we are in booter, can only get TargetInfo
                                    // we have to have a valid device info
                                    if (nanoDevice.DebugEngine.TargetInfo != null)
                                    {
                                        OutputWriter.WriteLine($"  nanoBooter:  {nanoDevice.DebugEngine.TargetInfo.BooterVersion}");

                                        // nanoBooter reports 0.0.0.0 when it doesn't know the CLR version
                                        Version clrVersion = nanoDevice.DebugEngine.TargetInfo.CLRVersion;

                                        if (clrVersion is not null
                                            && clrVersion != new Version(0, 0, 0, 0))
                                        {
                                            OutputWriter.WriteLine($"  nanoCLR:     {clrVersion}");
                                        }

                                        if (_verbosityLevel >= VerbosityLevel.Detailed)
                                        {
                                            OutputWriter.WriteLine($"  Platform:    {nanoDevice.DebugEngine.TargetInfo.PlatformName}");
                                            OutputWriter.WriteLine($"  Type:        {nanoDevice.DebugEngine.TargetInfo.PlatformInfo}");
                                        }
                                    }
                                }

                                OutputWriter.WriteLine("");
                            }
                        }

                        // separate from the MCUboot list (at Normal and above each device already ends with an empty line)
                        if (mcubootDevices.Any()
                            && _verbosityLevel < VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine("");
                        }
                    }

                    DisplayMcubootDevices(mcubootDevices);

                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
                catch (Exception ex)
                {
                    NanoTelemetry.TrackException(ex, "listDevices");

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
                    await RunManagerAsync(
                        manager,
                        ExitCodes.E2002,
                        (typeof(CantConnectToNanoDeviceException), ExitCodes.E2001, true));
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

            #region MCUboot / SMP

            // Everything MCUboot goes straight through SMP, except a firmware update of an ESP32 target:
            // that one goes through the ESP32 manager, which provisions MCUboot on first use.
            // This has to be handled before the platform guessing below, which would take a plain
            // serial port as an ESP32 device and connect to it through its ROM bootloader.
            if (o.McubootTarget
                && !(o.Update && o.Platform == SupportedPlatform.esp32))
            {
                await RunManagerAsync(
                    new McubootManager(o, _verbosityLevel),
                    ExitCodes.E10005,
                    (typeof(ImgtoolNotFoundException), ExitCodes.E10001, false),
                    (typeof(McubootImageException), ExitCodes.E10002, true),
                    (typeof(McumgrProtocolException), ExitCodes.E10010, true),
                    (typeof(McumgrTimeoutException), ExitCodes.E10007, true));

                // done here, this command has no further processing
                return;
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

            CommandTelemetry.SetPlatform(o.Platform);

            // deploy requires image
            if (o.Deploy && string.IsNullOrEmpty(o.DeploymentImage))
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = "deploy requires image to specify the deployment image path.";
                return;
            }

            #region firmware archive update if no device is required
            if (o.UpdateFwArchive)
            {
                // check for invalid options passed with platform option
                if (o.FromFwArchive)
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = "Incompatible option fromarchive combined with download.";
                    return;
                }
                if (string.IsNullOrEmpty(o.FwArchivePath))
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = "download requires archivepath to specify the firmware archive location.";
                    return;
                }

                if (o.Platform is null && string.IsNullOrEmpty(o.TargetName))
                {
                    _exitCode = ExitCodes.E9000;
                    _extraMessage = "download requires platform or target to specify what firmware to download.";
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
                    _extraMessage = "fromarchive requires archivepath to specify the firmware archive location.";
                    return;
                }
            }
            else if (!o.FromFwArchive)
            {
                _exitCode = ExitCodes.E9000;
                _extraMessage = "archivepath requires fromarchive to be specified.";
                return;
            }
            #endregion

            #region ESP32 platform options

            if (o.Platform == SupportedPlatform.esp32)
            {
                await RunManagerAsync(
                    new Esp32Manager(o, _verbosityLevel),
                    ExitCodes.E4000,
                    (typeof(EspToolExecutionException), ExitCodes.E4000, true),
                    (typeof(ReadEsp32FlashException), ExitCodes.E4004, true),
                    (typeof(WriteEsp32FlashException), ExitCodes.E4003, true));

                operationPerformed = true;
            }

            #endregion

            #region STM32 platform options

            if (o.Platform == SupportedPlatform.stm32)
            {
                await RunManagerAsync(
                    new Stm32Manager(o, _verbosityLevel),
                    ExitCodes.E5000,
                    (typeof(CantConnectToDfuDeviceException), ExitCodes.E1005, false),
                    (typeof(CantConnectToJtagDeviceException), ExitCodes.E5002, false));

                operationPerformed = true;
            }

            #endregion

            #region TI CC13x2 platform options

            if (o.Platform == SupportedPlatform.ti_simplelink)
            {
                await RunManagerAsync(new TIManager(o, _verbosityLevel), ExitCodes.E5000);

                operationPerformed = true;
            }

            #endregion

            #region Silabs Giant Gecko S1 platform options

            if (o.Platform == SupportedPlatform.efm32)
            {
                await RunManagerAsync(
                    new SilabsManager(o, _verbosityLevel),
                    ExitCodes.E8000,
                    (typeof(CantConnectToJLinkDeviceException), ExitCodes.E8001, false),
                    (typeof(SilinkExecutionException), ExitCodes.E8002, false));

                operationPerformed = true;
            }

            #endregion

            #region Raspberry Pi Pico platform options

            if (o.Platform == SupportedPlatform.rpi_pico)
            {
                await RunManagerAsync(new PicoManager(o, _verbosityLevel), ExitCodes.E3000);

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
                        NanoTelemetry.TrackException(ex, "fileDeployment");

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
                        NanoTelemetry.TrackException(ex, "networkDeployment");

                        // exception with 
                        _exitCode = ExitCodes.E2003;
                        _extraMessage = ex.Message;
                    }
                }
            }

            #endregion
        }

        /// <summary>
        /// Probes serial ports for devices in MCUboot serial recovery (SMP). Failures are reported
        /// but never propagated, so they can't spoil the Wire Protocol device listing.
        /// </summary>
        /// <param name="serialPort">When set, only this port is probed.</param>
        private static async Task<List<McubootDiscoveredDevice>> ListMcubootDevicesAsync(string serialPort)
        {
            try
            {
                IEnumerable<string> candidates = string.IsNullOrEmpty(serialPort)
                    ? SerialPort.GetPortNames()
                    : new[] { serialPort };

                return await McubootDeviceDiscovery.ProbeSerialPortsAsync(
                    candidates,
                    readDetails: _verbosityLevel >= VerbosityLevel.Normal,
                    verbosity: _verbosityLevel);
            }
            catch (Exception ex)
            {
                if (_verbosityLevel >= VerbosityLevel.Detailed)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine($"Failed to probe serial ports for MCUboot devices: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                return new List<McubootDiscoveredDevice>();
            }
        }

        private static void DisplayMcubootDevices(List<McubootDiscoveredDevice> devices)
        {
            if (!devices.Any())
            {
                return;
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;
            OutputWriter.WriteLine("-- MCUboot serial recovery --");

            foreach (McubootDiscoveredDevice device in devices)
            {
                OutputWriter.WriteLine(device.DeviceInfo is null
                    ? device.PortName
                    : $"{device.DeviceInfo.TargetName} @ {device.PortName}");

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    if (device.DeviceInfo is not null)
                    {
                        OutputWriter.WriteLine($"  MCUboot:     {device.DeviceInfo.McubootVersion ?? "unknown"}");
                        OutputWriter.WriteLine($"  nanoMCUboot: {device.DeviceInfo.NanoMcubootVersion ?? "unknown"}");
                    }

                    if (device.Images is null)
                    {
                        OutputWriter.WriteLine($"  Images:      unavailable ({device.ImageListError ?? "not read"})");
                    }
                    else if (device.Images.Count == 0)
                    {
                        OutputWriter.WriteLine("  Images:      none");
                    }
                    else
                    {
                        foreach (McumgrImageInfo image in device.Images)
                        {
                            var flags = new List<string>();

                            if (image.Active)
                            {
                                flags.Add("active");
                            }

                            if (image.Confirmed)
                            {
                                flags.Add("confirmed");
                            }

                            if (image.Pending)
                            {
                                flags.Add("pending");
                            }

                            if (image.Permanent)
                            {
                                flags.Add("permanent");
                            }

                            if (image.Bootable)
                            {
                                flags.Add("bootable");
                            }

                            OutputWriter.WriteLine($"  Image {image.Image} slot {image.Slot}: {image.Version ?? "?"}  {string.Join(" ", flags)}".TrimEnd());

                            if (_verbosityLevel >= VerbosityLevel.Detailed
                                && image.Hash is { Length: > 0 })
                            {
                                OutputWriter.WriteLine($"    Hash: {BitConverter.ToString(image.Hash).Replace("-", "")}");
                            }
                        }
                    }

                    OutputWriter.WriteLine("");
                }
            }
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
