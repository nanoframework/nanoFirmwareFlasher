// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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

            if (_options.BinFile != null && _options.BinFile.Count > 0 && !_options.UsePicoBoot)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("WARNING: --binfile requires --picoboot for Raspberry Pi Pico. Ignoring.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            // warn if PICOBOOT-only options are used without --picoboot
            if (!_options.UsePicoBoot)
            {
                if (_options.VerifyAfterFlash)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("WARNING: --verify requires --picoboot. Ignoring.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                if (!string.IsNullOrEmpty(_options.ReadFlashFile))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("WARNING: --readflash requires --picoboot. Ignoring.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                if (!string.IsNullOrEmpty(_options.OtpDumpFile))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("WARNING: --otpdump requires --picoboot. Ignoring.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }

            // handle force-reboot into BOOTSEL if requested
            if (_options.ForceBootsel)
            {
                ExitCodes forceResult = HandleForceBootsel();

                if (forceResult != ExitCodes.OK)
                {
                    return forceResult;
                }
            }

            // PICOBOOT path — direct USB protocol
            if (_options.UsePicoBoot)
            {
                return await ProcessPicoBootAsync();
            }

            // UF2 mass storage path (default)
            return await ProcessUf2Async();
        }

        /// <summary>
        /// Process operations using UF2 mass storage (default path).
        /// </summary>
        private async Task<ExitCodes> ProcessUf2Async()
        {
            // find UF2 drive
            string drivePath = PicoUf2Utility.FindUf2Drive();

            if (drivePath == null)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("No Pico device found in BOOTSEL mode. Please hold the BOOTSEL button and plug in the device.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                // wait for device
                drivePath = PicoUf2Utility.WaitForDrive(DriveWaitTimeoutMs, _verbosityLevel);

                if (drivePath == null)
                {
                    return ExitCodes.E3005;
                }
            }
            else
            {
                // check for multiple devices
                List<string> allDrives = PicoUf2Utility.FindAllUf2Drives();

                if (allDrives.Count > 1)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine($"WARNING: {allDrives.Count} Pico devices found in BOOTSEL mode. Using first: {drivePath}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
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

            // show device details only
            if (_options.DeviceDetails)
            {
                return ExitCodes.OK;
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
                    _verbosityLevel);

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

        /// <summary>
        /// Process operations using PICOBOOT USB protocol.
        /// </summary>
        private async Task<ExitCodes> ProcessPicoBootAsync()
        {
            PicoBootDevice device = PicoBootDevice.OpenFirst();

            if (device == null)
            {
                throw new PicoUf2DriveNotFoundException(
                    "No Pico device found via PICOBOOT USB interface. " +
                    "Ensure the device is in BOOTSEL mode and USB drivers are installed.");
            }

            using (device)
            {
                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.WriteLine($"PICOBOOT device: {device.ChipType}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // show device details
                if (_options.DeviceDetails)
                {
                    PicoDeviceExtendedInfo extInfo = device.QueryExtendedInfo();

                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.WriteLine(extInfo.ToString());
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    if (device.ChipType == "RP2350" && extInfo.RawSysInfo == null)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine("WARNING: GET_INFO command failed. Extended device info may be incomplete.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.E3006;
                    }

                    if (device.ChipType != "RP2350")
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine("Note: Extended info (flash JEDEC, OTP, partitions) is only available on RP2350 devices.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    return ExitCodes.OK;
                }

                bool operationPerformed = false;
                bool needsReboot = false;

                // mass erase
                if (_options.MassErase)
                {
                    ExitCodes eraseResult = device.MassErase(verbosity: _verbosityLevel);

                    if (eraseResult != ExitCodes.OK)
                    {
                        return eraseResult;
                    }

                    operationPerformed = true;
                }

                // read flash backup
                if (!string.IsNullOrEmpty(_options.ReadFlashFile))
                {
                    ExitCodes readResult = ReadFlashToFile(device);

                    if (readResult != ExitCodes.OK)
                    {
                        return readResult;
                    }

                    operationPerformed = true;
                }

                // OTP dump
                if (!string.IsNullOrEmpty(_options.OtpDumpFile))
                {
                    ExitCodes otpResult = DumpOtpToFile(device);

                    if (otpResult != ExitCodes.OK)
                    {
                        return otpResult;
                    }

                    operationPerformed = true;
                }

                // update firmware
                if (_options.Update)
                {
                    ExitCodes updateResult = await PicoOperations.UpdateFirmwareViaPicoBootAsync(
                        device,
                        _options.TargetName,
                        _options.FwVersion,
                        _options.Preview,
                        _options.FromFwArchive ? _options.FwArchivePath : null,
                        _options.ClrFile,
                        _options.VerifyAfterFlash,
                        reboot: false,
                        _verbosityLevel);

                    if (updateResult != ExitCodes.OK)
                    {
                        return updateResult;
                    }

                    operationPerformed = true;
                    needsReboot = true;
                }

                // flash raw binary file(s) at address
                if (_options.BinFile != null && _options.BinFile.Count > 0)
                {
                    ExitCodes binResult = FlashBinFiles(device);

                    if (binResult != ExitCodes.OK)
                    {
                        return binResult;
                    }

                    operationPerformed = true;
                    needsReboot = true;
                }

                // reboot device after flash operations
                if (needsReboot)
                {
                    device.Reboot();
                }

                if (!operationPerformed)
                {
                    throw new NoOperationPerformedException();
                }
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Force-reboot a running Pico device into BOOTSEL mode.
        /// </summary>
        private ExitCodes HandleForceBootsel()
        {
            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("Searching for running Pico device to force into BOOTSEL mode...");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            var runningDevices = PicoBootDevice.FindRunningDevices();

            if (runningDevices.Count == 0)
            {
                // device might already be in BOOTSEL — not an error, just continue
                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("No running Pico device found. Device may already be in BOOTSEL mode.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                return ExitCodes.OK;
            }

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Found running device: {runningDevices[0]}");
                OutputWriter.WriteLine("Forcing reboot into BOOTSEL mode...");
            }

            ExitCodes result = PicoBootDevice.ForceBootsel(runningDevices[0]);

            if (result == ExitCodes.OK && _verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Device rebooted into BOOTSEL mode.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return result;
        }

        /// <summary>
        /// Read flash contents to a file for backup.
        /// </summary>
        private ExitCodes ReadFlashToFile(PicoBootDevice device)
        {
            string filePath = _options.ReadFlashFile;

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Reading flash contents to {filePath}...");
            }

            ExitCodes result = device.ExclusiveAccess(true);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            result = device.ExitXip();

            if (result != ExitCodes.OK)
            {
                device.ExclusiveAccess(false);
                return result;
            }

            // read 2MB by default (standard Pico flash size)
            const uint flashSize = 2 * 1024 * 1024;
            byte[] flashData = device.FlashRead(0x10000000, flashSize);

            device.ExclusiveAccess(false);

            if (flashData == null)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("Failed to read flash contents.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E3002;
            }

            try
            {
                File.WriteAllBytes(filePath, flashData);
            }
            catch (Exception ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Failed to write flash backup: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E3002;
            }

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine($"Flash backup saved to {filePath} ({flashData.Length} bytes).");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Dump OTP memory contents to a file (RP2350 only).
        /// Reads all OTP rows in ECC-corrected mode (2 bytes per row, 8192 rows).
        /// </summary>
        private ExitCodes DumpOtpToFile(PicoBootDevice device)
        {
            if (device.ChipType != "RP2350")
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("OTP dump is only supported on RP2350 devices.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E3007;
            }

            string filePath = _options.OtpDumpFile;

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Reading OTP memory to {filePath}...");
            }

            // read OTP in chunks (256 rows per batch to stay within USB buffer limits)
            const ushort totalRows = 8192;
            const ushort chunkSize = 256;

            using (var ms = new MemoryStream(totalRows * 2))
            {
                for (ushort row = 0; row < totalRows; row += chunkSize)
                {
                    ushort remaining = (ushort)Math.Min(chunkSize, totalRows - row);
                    byte[] chunk = device.OtpRead(row, remaining, ecc: true);

                    if (chunk == null)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"Failed to read OTP rows {row}-{row + remaining - 1}.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.E3007;
                    }

                    ms.Write(chunk, 0, chunk.Length);
                }

                try
                {
                    File.WriteAllBytes(filePath, ms.ToArray());
                }
                catch (Exception ex)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Failed to write OTP dump: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3007;
                }
            }

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine($"OTP dump saved to {filePath} ({totalRows} rows, {totalRows * 2} bytes).");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Flash raw binary files at specified addresses via PICOBOOT.
        /// </summary>
        private ExitCodes FlashBinFiles(PicoBootDevice device)
        {
            var binFiles = _options.BinFile;
            var addresses = _options.FlashAddress;

            if (addresses == null || addresses.Count != binFiles.Count)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("ERROR: --binfile requires matching --address values for each file.");
                OutputWriter.WriteLine($"  Got {binFiles.Count} file(s) but {addresses?.Count ?? 0} address(es).");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E3003;
            }

            for (int i = 0; i < binFiles.Count; i++)
            {
                string filePath = binFiles[i];
                string addressStr = addresses[i];

                if (!File.Exists(filePath))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"BIN file not found: {filePath}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3003;
                }

                if (!uint.TryParse(
                    addressStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? addressStr.Substring(2)
                        : addressStr,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out uint flashAddress))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Invalid flash address: {addressStr}. Use hex format (e.g., 0x10000000).");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3003;
                }

                byte[] binData;

                try
                {
                    binData = File.ReadAllBytes(filePath);
                }
                catch (Exception ex)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Error reading BIN file '{filePath}': {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3003;
                }

                if (_verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine($"Flashing {filePath} ({binData.Length} bytes) to 0x{flashAddress:X8}...");
                }

                ExitCodes result = device.UpdateFirmware(binData, flashAddress, _verbosityLevel);

                if (result != ExitCodes.OK)
                {
                    return result;
                }

                // verify if requested
                if (_options.VerifyAfterFlash)
                {
                    result = device.VerifyFirmware(binData, flashAddress, _verbosityLevel);

                    if (result != ExitCodes.OK)
                    {
                        return result;
                    }
                }
            }

            return ExitCodes.OK;
        }
    }
}
