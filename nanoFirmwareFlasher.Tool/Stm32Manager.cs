// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

        /// <summary>
        /// Configures a flash-capable device and dispatches hex/bin file flashing.
        /// Sets Verbosity, DoMassErase, and Verify (for supported devices).
        /// </summary>
        private ExitCodes FlashDeviceFiles(IStmFlashableDevice device)
        {
            device.Verbosity = _verbosityLevel;
            device.DoMassErase = _options.MassErase;

            // Set Verify for devices that support read-back verification
            if (device is StmSwdDevice swd)
            {
                swd.Verify = _options.Verify;
            }
            else if (device is StmStLinkDevice stlink)
            {
                stlink.Verify = _options.Verify;
            }

            if (_options.HexFile.Any())
            {
                return device.FlashHexFiles(_options.HexFile);
            }

            if (_options.BinFile.Any())
            {
                return device.FlashBinFiles(_options.BinFile, _options.FlashAddress);
            }

            return ExitCodes.OK;
        }

        /// <inheritdoc />
        public async Task<ExitCodes> ProcessAsync()
        {
            if (_options.IdentifyFirmware)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Cannot determine the best matching target for a {SupportedPlatform.stm32} device.");
                OutputWriter.WriteLine();
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.OK;
            }
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

                OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                if (connecteDevices.Count() == 0)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("No DFU devices found");
                }
                else
                {
                    OutputWriter.WriteLine("-- Connected DFU devices --");

                    foreach ((string serial, string device) device in connecteDevices)
                    {
                        OutputWriter.WriteLine($"{device.serial} @ {device.device}");
                    }

                    OutputWriter.WriteLine("---------------------------");
                }

                OutputWriter.ForegroundColor = ConsoleColor.White;

                // done here, this command has no further processing
                return ExitCodes.OK;
            }

            if (_options.ListNativeDfuDevices)
            {
                var connectedDevices = StmNativeDfuDevice.ListDevices();

                OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                if (connectedDevices.Count == 0)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("No DFU devices found via native USB enumeration");
                }
                else
                {
                    OutputWriter.WriteLine("-- Connected DFU devices (native USB) --");

                    foreach ((string serial, string device) device in connectedDevices)
                    {
                        OutputWriter.WriteLine($"{device.serial} @ {device.device}");
                    }

                    OutputWriter.WriteLine("----------------------------------------");
                }

                OutputWriter.ForegroundColor = ConsoleColor.White;

                // done here, this command has no further processing
                return ExitCodes.OK;
            }

            if (_options.ListJtagDevices)
            {
                var connecteDevices = StmJtagDevice.ListDevices();

                OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                if (connecteDevices.Count == 0)
                {
                    OutputWriter.WriteLine("No JTAG devices found");
                }
                else
                {
                    OutputWriter.WriteLine("-- Connected JTAG devices --");

                    foreach (string deviceId in connecteDevices)
                    {
                        OutputWriter.WriteLine(deviceId);
                    }

                    OutputWriter.WriteLine("---------------------------");
                }

                OutputWriter.ForegroundColor = ConsoleColor.White;

                // done here, this command has no further processing
                return ExitCodes.OK;
            }

            if (_options.ListNativeSwdDevices)
            {
                var connectedDevices = StmSwdDevice.ListDevices();

                OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                if (connectedDevices.Count == 0)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("No CMSIS-DAP probes found via native USB HID enumeration");
                }
                else
                {
                    OutputWriter.WriteLine("-- Connected CMSIS-DAP probes (native SWD) --");

                    foreach (string serial in connectedDevices)
                    {
                        OutputWriter.WriteLine(serial);
                    }

                    OutputWriter.WriteLine("---------------------------------------------");
                }

                OutputWriter.ForegroundColor = ConsoleColor.White;

                // done here, this command has no further processing
                return ExitCodes.OK;
            }

            if (_options.ListNativeStLinkDevices)
            {
                var connectedDevices = StmStLinkDevice.ListDevices();

                OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                if (connectedDevices.Count == 0)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("No ST-LINK probes found via native USB enumeration");
                }
                else
                {
                    OutputWriter.WriteLine("-- Connected ST-LINK probes (native) --");

                    foreach (string serial in connectedDevices)
                    {
                        OutputWriter.WriteLine(serial);
                    }

                    OutputWriter.WriteLine("---------------------------------------");
                }

                OutputWriter.ForegroundColor = ConsoleColor.White;

                // done here, this command has no further processing
                return ExitCodes.OK;
            }

            var connectedStDfuDevices = new System.Collections.Generic.List<(string serial, string device)>();
            var connectedStJtagDevices = new System.Collections.Generic.List<string>();

            try
            {
                connectedStDfuDevices = StmDfuDevice.ListDevices();
            }
            catch
            {
                // CLI tool not available
            }

            try
            {
                connectedStJtagDevices = StmJtagDevice.ListDevices();
            }
            catch
            {
                // CLI tool not available
            }

            bool updateAndDeploy = false;

            if (_options.NativeDfuUpdate &&
                (_options.BinFile.Any() ||
                 _options.HexFile.Any()))
            {
                #region STM32 Native DFU options

                using var nativeDfuDevice = new StmNativeDfuDevice(_options.DfuDeviceId);

                if (!nativeDfuDevice.DevicePresent)
                {
                    return ExitCodes.E1000;
                }

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Connected to DFU device with ID {nativeDfuDevice.DfuId} (native USB)");
                }

                return FlashDeviceFiles(nativeDfuDevice);

                #endregion
            }
            else if (_options.NativeSwdUpdate &&
                (_options.BinFile.Any() ||
                 _options.HexFile.Any()))
            {
                #region STM32 Native SWD (CMSIS-DAP) options

                using var swdDevice = new StmSwdDevice(_options.JtagDeviceId);

                if (!swdDevice.DevicePresent)
                {
                    return ExitCodes.E5001;
                }

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Connected to target via CMSIS-DAP probe {swdDevice.ProbeId} (native SWD)");
                }

                return FlashDeviceFiles(swdDevice);

                #endregion
            }
            else if (_options.NativeStLinkUpdate &&
                (_options.BinFile.Any() ||
                 _options.HexFile.Any()))
            {
                #region STM32 Native ST-LINK options

                using var stLinkDevice = new StmStLinkDevice(_options.JtagDeviceId);

                if (!stLinkDevice.DevicePresent)
                {
                    return ExitCodes.E5001;
                }

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Connected to target via ST-LINK probe {stLinkDevice.ProbeId} (native)");
                }

                return FlashDeviceFiles(stLinkDevice);

                #endregion
            }
            else if (!_options.NativeDfuUpdate && !_options.NativeSwdUpdate && !_options.NativeStLinkUpdate &&
                (_options.BinFile.Any() || _options.HexFile.Any()) &&
                connectedStDfuDevices.Count == 0 && connectedStJtagDevices.Count == 0)
            {
                // No explicit interface and no CLI devices found — try native auto-detection
                try
                {
                    var nativeStLinkProbes = StmStLinkDevice.ListDevices();

                    if (nativeStLinkProbes.Count > 0)
                    {
                        using var stLinkDevice = new StmStLinkDevice(_options.JtagDeviceId);

                        if (stLinkDevice.DevicePresent)
                        {
                            if (_verbosityLevel >= VerbosityLevel.Normal)
                            {
                                OutputWriter.WriteLine($"Auto-detected ST-LINK probe {stLinkDevice.ProbeId} — using native transport");
                            }

                            return FlashDeviceFiles(stLinkDevice);
                        }
                    }
                }
                catch
                {
                    // Native ST-LINK enumeration not available
                }

                try
                {
                    var nativeSwdProbes = StmSwdDevice.ListDevices();

                    if (nativeSwdProbes.Count > 0)
                    {
                        using var swdDevice = new StmSwdDevice(_options.JtagDeviceId);

                        if (swdDevice.DevicePresent)
                        {
                            if (_verbosityLevel >= VerbosityLevel.Normal)
                            {
                                OutputWriter.WriteLine($"Auto-detected CMSIS-DAP probe {swdDevice.ProbeId} — using native SWD");
                            }

                            return FlashDeviceFiles(swdDevice);
                        }
                    }
                }
                catch
                {
                    // Native SWD enumeration not available
                }

                try
                {
                    var nativeDfuDevices = StmNativeDfuDevice.ListDevices();

                    if (nativeDfuDevices.Count > 0)
                    {
                        using var nativeDfuDevice = new StmNativeDfuDevice(_options.DfuDeviceId);

                        if (nativeDfuDevice.DevicePresent)
                        {
                            if (_verbosityLevel >= VerbosityLevel.Normal)
                            {
                                OutputWriter.WriteLine($"Auto-detected DFU device {nativeDfuDevice.DfuId} — using native USB DFU");
                            }

                            return FlashDeviceFiles(nativeDfuDevice);
                        }
                    }
                }
                catch
                {
                    // Native DFU enumeration not available
                }
            }
            else if (connectedStDfuDevices.Count != 0 &&
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
                    OutputWriter.WriteLine($"Connected to JTAG device with ID {dfuDevice.DfuId}");
                }

                return FlashDeviceFiles(dfuDevice);

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
                    OutputWriter.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
                }

                return FlashDeviceFiles(jtagDevice);

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

                    int selectedInterfaces = (_options.DfuUpdate ? 1 : 0) + (_options.JtagUpdate ? 1 : 0) + (_options.NativeDfuUpdate ? 1 : 0) + (_options.NativeSwdUpdate ? 1 : 0) + (_options.NativeStLinkUpdate ? 1 : 0);

                    if (selectedInterfaces > 1)
                    {
                        // can't select multiple interfaces simultaneously
                        return ExitCodes.E9000;
                    }
                    else if (_options.NativeStLinkUpdate)
                    {
                        updateInterface = Interface.NativeStLink;
                    }
                    else if (_options.NativeSwdUpdate)
                    {
                        updateInterface = Interface.NativeSwd;
                    }
                    else if (_options.NativeDfuUpdate)
                    {
                        updateInterface = Interface.NativeDfu;
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
                        _options.FromFwArchive ? _options.FwArchivePath : null,
                        true,
                        _options.DeploymentImage,
                        appFlashAddress,
                        _options.DfuDeviceId,
                        _options.JtagDeviceId,
                        !_options.FitCheck,
                        updateInterface,
                        _verbosityLevel,
                        _options.Verify);

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

                    int selectedInterfacesDeploy = (_options.DfuUpdate ? 1 : 0) + (_options.JtagUpdate ? 1 : 0) + (_options.NativeDfuUpdate ? 1 : 0) + (_options.NativeSwdUpdate ? 1 : 0) + (_options.NativeStLinkUpdate ? 1 : 0);

                    if (selectedInterfacesDeploy > 1)
                    {
                        // can't select multiple interfaces simultaneously
                        return ExitCodes.E9000;
                    }
                    else if (_options.NativeStLinkUpdate)
                    {
                        updateInterface = Interface.NativeStLink;
                    }
                    else if (_options.NativeSwdUpdate)
                    {
                        updateInterface = Interface.NativeSwd;
                    }
                    else if (_options.NativeDfuUpdate)
                    {
                        updateInterface = Interface.NativeDfu;
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
                                    null,
                                    false,
                                    _options.DeploymentImage,
                                    appFlashAddress,
                                    _options.DfuDeviceId,
                                    _options.JtagDeviceId,
                                    !_options.FitCheck,
                                    updateInterface,
                                    _verbosityLevel,
                                    _options.Verify);

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
                if (_options.NativeDfuUpdate)
                {
                    // Native USB DFU mass erase
                    using var nativeDfuDevice = new StmNativeDfuDevice(_options.DfuDeviceId);

                    if (!nativeDfuDevice.DevicePresent)
                    {
                        return ExitCodes.E1000;
                    }

                    nativeDfuDevice.Verbosity = _verbosityLevel;
                    return nativeDfuDevice.MassErase();
                }
                else if (_options.NativeStLinkUpdate)
                {
                    // Native ST-LINK mass erase
                    using var stLinkDevice = new StmStLinkDevice(_options.JtagDeviceId);

                    if (!stLinkDevice.DevicePresent)
                    {
                        return ExitCodes.E5001;
                    }

                    stLinkDevice.Verbosity = _verbosityLevel;
                    return stLinkDevice.MassErase();
                }
                else if (_options.NativeSwdUpdate)
                {
                    // Native SWD (CMSIS-DAP) mass erase
                    using var swdDevice = new StmSwdDevice(_options.JtagDeviceId);

                    if (!swdDevice.DevicePresent)
                    {
                        return ExitCodes.E5001;
                    }

                    swdDevice.Verbosity = _verbosityLevel;
                    return swdDevice.MassErase();
                }
            

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
