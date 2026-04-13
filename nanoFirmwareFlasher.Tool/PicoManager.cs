// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.NFDevice;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class to manage different operations specific to the Raspberry Pi Pico platform.
    /// </summary>
    public class PicoManager : IManager
    {
        /// <summary>
        /// Timeout in milliseconds to wait for a UF2 drive to appear.
        /// </summary>
        private const int DriveWaitTimeoutMs = 30_000;

        /// <summary>
        /// Timeout in milliseconds to acquire exclusive access to the serial port.
        /// </summary>
        private const int AccessSerialPortTimeout = 3000;

        private readonly Options _options;
        private readonly VerbosityLevel _verbosityLevel;

        public PicoManager(Options options, VerbosityLevel verbosityLevel)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Platform != SupportedPlatform.rpi_pico)
            {
                throw new NotSupportedException($"{nameof(options)} - {options.Platform}");
            }

            _options = options;
            _verbosityLevel = verbosityLevel;
        }

        /// <inheritdoc />
        public async Task<ExitCodes> ProcessAsync()
        {
            // warn about unsupported options for the Pico platform
            if (_options.HexFile != null && _options.HexFile.Count > 0)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("WARNING: --hexfile is not supported for Raspberry Pi Pico. Ignoring.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            // update and mass erase require UF2 first, even when combined with deploy
            if (_options.Update || _options.MassErase)
            {
                var uf2Result = await ProcessUf2OperationsAsync();

                if (uf2Result != ExitCodes.OK)
                {
                    return uf2Result;
                }
            }

            // deployment via wire protocol (default) or UF2
            if (_options.Deploy)
            {
                if (_options.Uf2Deploy)
                {
                    // UF2 mass storage deployment — requires BOOTSEL mode
                    return await DeployViaUf2Async();
                }
                else
                {
                    // wire protocol deployment — requires serial port, like other devices
                    return DeployViaWireProtocol();
                }
            }

            // if update or mass erase already ran, we're done
            if (_options.Update || _options.MassErase)
            {
                return ExitCodes.OK;
            }

            // device details via UF2 drive if available, or via serial
            if (_options.DeviceDetails)
            {
                return await GetDeviceDetailsAsync();
            }

            throw new NoOperationPerformedException();
        }

        /// <summary>
        /// Deploy application via wire protocol (serial port), like other devices.
        /// </summary>
        private ExitCodes DeployViaWireProtocol()
        {
            if (string.IsNullOrEmpty(_options.SerialPort))
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("A serial port (--serialport) is required for wire protocol deployment.");
                OutputWriter.WriteLine("Use --uf2deploy to deploy via UF2 mass storage instead.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E6001;
            }

            NanoDeviceOperations nanoDeviceOperations = new NanoDeviceOperations();

            using (var access = GlobalExclusiveDeviceAccess.TryGet(_options.SerialPort, AccessSerialPortTimeout))
            {
                if (access is null)
                {
                    return ExitCodes.E6002;
                }

                return nanoDeviceOperations.DeployApplication(
                    _options.SerialPort,
                    _options.DeploymentImage,
                    _verbosityLevel);
            }
        }

        /// <summary>
        /// Deploy application via UF2 mass storage (requires BOOTSEL mode).
        /// </summary>
        private async Task<ExitCodes> DeployViaUf2Async()
        {
            string uf2Drive = PicoUf2Utility.FindUf2Drive();

            if (uf2Drive == null)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("No Pico device found in BOOTSEL mode.");
                OutputWriter.WriteLine("Please hold the BOOTSEL button and plug in the device.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                uf2Drive = PicoUf2Utility.WaitForDrive(DriveWaitTimeoutMs, _verbosityLevel);

                if (uf2Drive == null)
                {
                    return ExitCodes.E3005;
                }
            }

            // reject ambiguous target when multiple devices are in BOOTSEL mode
            List<string> allDrives = PicoUf2Utility.FindAllUf2Drives();

            if (allDrives.Count > 1)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"{allDrives.Count} Pico devices found in BOOTSEL mode. Cannot determine deploy target.");
                OutputWriter.WriteLine("Disconnect extra devices so only the intended target remains.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E3006;
            }

            PicoDeviceInfo deviceInfo = PicoUf2Utility.DetectDevice(uf2Drive);

            if (deviceInfo == null)
            {
                return ExitCodes.E3004;
            }

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                OutputWriter.WriteLine("");
                OutputWriter.WriteLine("Connected to:");
                OutputWriter.WriteLine($"{deviceInfo}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            string deployAddress = _options.FlashAddress?.Count > 0
                ? _options.FlashAddress[0]
                : null;

            return PicoOperations.DeployApplication(
                deviceInfo,
                _options.DeploymentImage,
                deployAddress,
                _verbosityLevel);
        }

        /// <summary>
        /// Process update and mass erase operations via UF2 mass storage.
        /// </summary>
        private async Task<ExitCodes> ProcessUf2OperationsAsync()
        {
            string uf2Drive = PicoUf2Utility.FindUf2Drive();

            if (uf2Drive == null)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("No Pico device found in BOOTSEL mode.");
                OutputWriter.WriteLine("Please hold the BOOTSEL button and plug in the device.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                uf2Drive = await WaitForUf2DriveAsync();

                if (uf2Drive == null)
                {
                    return ExitCodes.E3005;
                }
            }

            return await ProcessUf2Async(uf2Drive);
        }

        /// <summary>
        /// Wait for a UF2 drive to appear.
        /// </summary>
        /// <returns>The drive path, or null if timed out.</returns>
        private async Task<string> WaitForUf2DriveAsync()
        {
            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("Waiting for Pico device in BOOTSEL mode...");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            int elapsed = 0;
            const int pollInterval = 500;

            while (elapsed < DriveWaitTimeoutMs)
            {
                string uf2Drive = PicoUf2Utility.FindUf2Drive();

                if (uf2Drive != null)
                {
                    return uf2Drive;
                }

                await Task.Delay(pollInterval);
                elapsed += pollInterval;
            }

            return null;
        }

        /// <summary>
        /// Get device details — tries UF2 drive first, falls back to serial port.
        /// </summary>
        private async Task<ExitCodes> GetDeviceDetailsAsync()
        {
            // try UF2 drive first
            string uf2Drive = PicoUf2Utility.FindUf2Drive();

            if (uf2Drive != null)
            {
                PicoDeviceInfo deviceInfo = PicoUf2Utility.DetectDevice(uf2Drive);

                if (deviceInfo != null)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine("Connected to:");
                    OutputWriter.WriteLine($"{deviceInfo}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.OK;
                }
            }

            // fall back to serial port
            if (!string.IsNullOrEmpty(_options.SerialPort))
            {
                NanoDeviceOperations nanoDeviceOperations = new NanoDeviceOperations();

                using (var access = GlobalExclusiveDeviceAccess.TryGet(_options.SerialPort, AccessSerialPortTimeout))
                {
                    if (access is null)
                    {
                        return ExitCodes.E6002;
                    }

                    NanoDeviceBase nanoDevice = null;

                    return nanoDeviceOperations.GetDeviceDetails(
                        _options.SerialPort,
                        ref nanoDevice);
                }
            }

            OutputWriter.ForegroundColor = ConsoleColor.Yellow;
            OutputWriter.WriteLine("No Pico device found in BOOTSEL mode and no serial port specified.");
            OutputWriter.ForegroundColor = ConsoleColor.White;

            return ExitCodes.E3004;
        }

        /// <summary>
        /// Process UF2 operations (update, mass erase) on a detected UF2 drive.
        /// </summary>
        /// <param name="drivePath">The already-detected UF2 drive path.</param>
        private async Task<ExitCodes> ProcessUf2Async(string drivePath)
        {
            // check for multiple devices
            List<string> allDrives = PicoUf2Utility.FindAllUf2Drives();

            if (allDrives.Count > 1)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine($"WARNING: {allDrives.Count} Pico devices found in BOOTSEL mode. Using first: {drivePath}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            // detect device
            PicoDeviceInfo deviceInfo = PicoUf2Utility.DetectDevice(drivePath);

            if (deviceInfo == null)
            {
                return ExitCodes.E3004;
            }

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                OutputWriter.WriteLine("");
                OutputWriter.WriteLine("Connected to:");
                OutputWriter.WriteLine($"{deviceInfo}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            bool operationPerformed = false;

            // update operation requested?
            if (_options.Update)
            {
                var exitCode = await PicoOperations.UpdateFirmwareAsync(
                    deviceInfo,
                    _options.TargetName,
                    true,
                    _options.FwVersion,
                    _options.Preview,
                    _options.FromFwArchive ? _options.FwArchivePath : null,
                    _options.ClrFile,
                    _options.MassErase,
                    _verbosityLevel);

                if (exitCode != ExitCodes.OK)
                {
                    return exitCode;
                }

                operationPerformed = true;
            }

            // standalone mass erase (no update)
            if (_options.MassErase && !operationPerformed)
            {
                var exitCode = PicoOperations.MassEraseViaUf2(deviceInfo, _verbosityLevel);

                if (exitCode != ExitCodes.OK)
                {
                    return exitCode;
                }

                operationPerformed = true;
            }

            if (!operationPerformed)
            {
                throw new NoOperationPerformedException();
            }

            return ExitCodes.OK;
        }
    }
}
