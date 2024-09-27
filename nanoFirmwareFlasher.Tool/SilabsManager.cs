// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class to manage different operations specific to the Silabs GG11 platform.
    /// </summary>
    public class SilabsManager : IManager
    {
        private readonly Options _options;
        private readonly VerbosityLevel _verbosityLevel;

        public SilabsManager(Options options, VerbosityLevel verbosityLevel)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Platform != SupportedPlatform.gg11)
            {
                throw new NotSupportedException($"{nameof(options)} - {options.Platform}");
            }

            _options = options;
            _verbosityLevel = verbosityLevel;
        }

        /// <inheritdoc />
        public async Task<ExitCodes> ProcessAsync()
        {
            if (_options.ShowFirmwareOnly)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Cannot determine the best matching target for a {SupportedPlatform.gg11} device.");
                OutputWriter.WriteLine();
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.OK;
            }

            if (_options.ListJLinkDevices)
            {
                var connecteDevices = JLinkDevice.ListDevices();

                OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                if (connecteDevices.Count == 0)
                {
                    OutputWriter.WriteLine("No J-Link devices found");
                }
                else
                {
                    OutputWriter.WriteLine("-- Connected USB J-Link devices --");

                    foreach (string deviceId in connecteDevices)
                    {
                        OutputWriter.WriteLine(deviceId);
                    }

                    OutputWriter.WriteLine("----------------------------------");
                }

                OutputWriter.ForegroundColor = ConsoleColor.White;

                // done here, this command has no further processing
                return ExitCodes.OK;
            }

            var connectedJLinkDevices = JLinkDevice.ListDevices();
            bool updateAndDeploy = false;

            if ((_options.BinFile.Any() ||
                !string.IsNullOrEmpty(_options.DeploymentImage))
                && connectedJLinkDevices.Count != 0)
            {
                try
                {
                    var jlinkDevice = new JLinkDevice(_options.JLinkDeviceId);

                    if (!jlinkDevice.DevicePresent)
                    {
                        // no J-Link device found

                        // done here, this command has no further processing
                        return ExitCodes.E8001;
                    }

                    if (_verbosityLevel >= VerbosityLevel.Normal)
                    {
                        OutputWriter.WriteLine($"Connected to J-Link device with ID {jlinkDevice.ProbeId}");
                    }

                    if (_verbosityLevel == VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"Firmware: {jlinkDevice.Firmare}");
                        OutputWriter.WriteLine($"Hardware: {jlinkDevice.Hardware}");
                    }

                    // set VCP baud rate (if requested)
                    if (_options.SetVcpBaudRate.HasValue)
                    {
                        _ = SilinkCli.SetVcpBaudRate(
                            _options.JLinkDeviceId is null ? connectedJLinkDevices.First() : "",
                            _options.SetVcpBaudRate.Value,
                            _verbosityLevel);
                    }

                    // set verbosity
                    jlinkDevice.Verbosity = _verbosityLevel;

                    // get mass erase option
                    jlinkDevice.DoMassErase = _options.MassErase;

                    // is there a bin file to flash?
                    if (_options.BinFile.Any())
                    {
                        var exitCode = jlinkDevice.FlashBinFiles(_options.BinFile, _options.FlashAddress);

                        if (exitCode != ExitCodes.OK)
                        {
                            // done here
                            return exitCode;
                        }

                        updateAndDeploy = true;
                    }
                    else if (!string.IsNullOrEmpty(_options.DeploymentImage) && _options.Deploy)
                    {
                        var exitCode = jlinkDevice.FlashBinFiles(
                            [_options.DeploymentImage],
                            _options.FlashAddress);

                        if (exitCode != ExitCodes.OK)
                        {
                            // done here
                            return exitCode;
                        }

                        updateAndDeploy = true;
                    }
                }
                catch (CantConnectToJLinkDeviceException)
                {
                    // exception too vague for the full list of commands above
                    throw new SilinkExecutionException();
                }
            }
            else if (!string.IsNullOrEmpty(_options.TargetName))
            {
                // update operation requested?
                if (_options.Update)
                {
                    // this to update the device with fw from CloudSmith

                    // need to take care of flash address
                    string appFlashAddress = null;

                    if (_options.FlashAddress.Any())
                    {
                        // take the first address, it should be the only one valid
                        appFlashAddress = _options.FlashAddress.ElementAt(0);
                    }

                    var exitCode = await JLinkOperations.UpdateFirmwareAsync(
                        _options.TargetName,
                        _options.FwVersion,
                        _options.Preview,
                        _options.FromFwArchive ? _options.FwArchivePath : null,
                        true,
                        _options.DeploymentImage,
                        appFlashAddress,
                        _options.JLinkDeviceId,
                        !_options.FitCheck,
                        _verbosityLevel);

                    if (_options.SetVcpBaudRate.HasValue)
                    {
                        // set VCP baud rate (if needed)
                        _ = SilinkCli.SetVcpBaudRate(
                            _options.JLinkDeviceId is null ? connectedJLinkDevices.First() : "",
                            _options.SetVcpBaudRate.Value,
                            _verbosityLevel);
                    }

                    if (exitCode != ExitCodes.OK)
                    {
                        // done here
                        return exitCode;
                    }

                    updateAndDeploy = true;
                }

                // it's OK to deploy after a successful update
                if (_options.Deploy)
                {
                    // this to flash a deployment image without updating the firmware

                    // need to take care of flash address
                    string appFlashAddress;

                    if (_options.FlashAddress.Any())
                    {
                        // take the first address, it should be the only one valid
                        appFlashAddress = _options.FlashAddress.ElementAt(0);
                    }
                    else
                    {
                        return ExitCodes.E9009;
                    }

                    var exitCode = await JLinkOperations.UpdateFirmwareAsync(
                                    _options.TargetName,
                                    null,
                                    false,
                                    null,
                                    false,
                                    _options.DeploymentImage,
                                    appFlashAddress,
                                    _options.JLinkDeviceId,
                                    !_options.FitCheck,
                                    _verbosityLevel);

                    if (exitCode != ExitCodes.OK)
                    {
                        // done here
                        return exitCode;
                    }

                    updateAndDeploy = true;
                }
            }
            else if (_options.MassErase)
            {
                return JLinkOperations.MassErase(
                    _options.JLinkDeviceId,
                    _verbosityLevel);
            }

            if (!updateAndDeploy)
            {
                throw new NoOperationPerformedException();
            }

            return ExitCodes.OK;
        }
    }
}
