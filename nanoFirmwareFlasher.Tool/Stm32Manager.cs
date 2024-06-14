//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Linq;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class to manage different operations specific to the STM32 platform.
    /// </summary>
    public class Stm32Manager : IManager
    {
        private readonly Options _options;
        private readonly VerbosityLevel _verbosityLevel;

        public Stm32Manager(Options options, VerbosityLevel verbosityLevel)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Platform != SupportedPlatform.stm32)
            {
                throw new NotSupportedException($"{nameof(options)} - {options.Platform}");
            }

            _options = options;
            _verbosityLevel = verbosityLevel;
        }

        /// <inheritdoc />
        public async Task<ExitCodes> ProcessAsync()
        {
            if (_options.InstallDfuDrivers)
            {
                return Stm32Operations.InstallDfuDrivers(_verbosityLevel);
            }

            if (_options.InstallJtagDrivers)
            {
                return Stm32Operations.InstallJtagDrivers(_verbosityLevel);
            }

            if (_options.ListDevicesInDfuMode)
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
                return ExitCodes.OK;
            }

            if (_options.ListJtagDevices)
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

                Console.ForegroundColor = ConsoleColor.White;

                // done here, this command has no further processing
                return ExitCodes.OK;
            }

            var connectedStDfuDevices = StmDfuDevice.ListDevices();
            var connectedStJtagDevices = StmJtagDevice.ListDevices();
            bool updateAndDeploy = false;

            if (connectedStDfuDevices.Count != 0 &&
                (_options.BinFile.Any() ||
                 _options.HexFile.Any()))
            {

                #region STM32 DFU options

                var dfuDevice = new StmDfuDevice(_options.DfuDeviceId);

                if (!dfuDevice.DevicePresent)
                {
                    // no JTAG device found

                    // done here, this command has no further processing
                    return ExitCodes.E5001;
                }

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.WriteLine($"Connected to JTAG device with ID {dfuDevice.DfuId}");
                }

                // set verbosity
                dfuDevice.Verbosity = _verbosityLevel;

                // get mass erase option
                dfuDevice.DoMassErase = _options.MassErase;

                if (_options.HexFile.Any())
                {
                    return dfuDevice.FlashHexFiles(_options.HexFile);
                }

                if (_options.BinFile.Any())
                {
                    return dfuDevice.FlashBinFiles(_options.BinFile, _options.FlashAddress);
                }

                #endregion

            }
            else if (connectedStJtagDevices.Count != 0 &&
                    (_options.BinFile.Any() ||
                     _options.HexFile.Any()))
            {
                // this has to be a JTAG connected device

                #region STM32 JTAG options

                var jtagDevice = new StmJtagDevice(_options.JtagDeviceId);

                if (!jtagDevice.DevicePresent)
                {
                    // no JTAG device found

                    // done here, this command has no further processing
                    return ExitCodes.E5001;
                }

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
                }

                // set verbosity
                jtagDevice.Verbosity = _verbosityLevel;

                // get mass erase option
                jtagDevice.DoMassErase = _options.MassErase;

                if (_options.HexFile.Any())
                {
                    return jtagDevice.FlashHexFiles(_options.HexFile);
                }

                if (_options.BinFile.Any())
                {
                    return jtagDevice.FlashBinFiles(_options.BinFile, _options.FlashAddress);
                }

                #endregion
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

                    Interface updateInterface = Interface.None;

                    if (_options.DfuUpdate && _options.JtagUpdate)
                    {
                        // can't select both JTAG and DFU simultaneously
                        return ExitCodes.E9000;
                    }
                    else if (_options.DfuUpdate)
                    {
                        updateInterface = Interface.Dfu;
                    }
                    else if (_options.JtagUpdate)
                    {
                        updateInterface = Interface.Jtag;
                    }

                    var exitCode = await Stm32Operations.UpdateFirmwareAsync(
                        _options.TargetName,
                        _options.FwVersion,
                        _options.Preview,
                        true,
                        _options.DeploymentImage,
                        appFlashAddress,
                        _options.DfuDeviceId,
                        _options.JtagDeviceId,
                        !_options.FitCheck,
                        updateInterface,
                        _verbosityLevel);

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

                    Interface updateInterface = Interface.None;

                    if (_options.DfuUpdate && _options.JtagUpdate)
                    {
                        // can't select both JTAG and DFU simultaneously
                        return ExitCodes.E9000;
                    }
                    else if (_options.DfuUpdate)
                    {
                        updateInterface = Interface.Dfu;
                    }
                    else if (_options.JtagUpdate)
                    {
                        updateInterface = Interface.Jtag;
                    }

                    var exitCode = await Stm32Operations.UpdateFirmwareAsync(
                                    _options.TargetName,
                                    null,
                                    false,
                                    false,
                                    _options.DeploymentImage,
                                    appFlashAddress,
                                    _options.DfuDeviceId,
                                    _options.JtagDeviceId,
                                    !_options.FitCheck,
                                    updateInterface,
                                    _verbosityLevel);

                    if (exitCode != ExitCodes.OK)
                    {
                        // done here
                        return exitCode;
                    }

                    updateAndDeploy = true;
                }

                // reset MCU requested?
                if (_options.ResetMcu)
                {
                    return Stm32Operations.ResetMcu(
                                    _options.JtagDeviceId,
                                    _verbosityLevel);
                }
            }
            else if (_options.MassErase)
            {
                return Stm32Operations.MassErase(
                                _options.JtagDeviceId,
                                _verbosityLevel);
            }
            else if (_options.ResetMcu)
            {
                return Stm32Operations.ResetMcu(
                                _options.JtagDeviceId,
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
