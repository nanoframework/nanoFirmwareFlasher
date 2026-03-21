// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class with operations available for Raspberry Pi Pico (RP2040/RP2350) devices.
    /// </summary>
    public class PicoOperations
    {
        /// <summary>
        /// Timeout in milliseconds to wait for a UF2 drive to appear.
        /// </summary>
        private const int DriveWaitTimeoutMs = 30_000;

        /// <summary>
        /// Perform firmware update on a Raspberry Pi Pico device via UF2 mass storage.
        /// </summary>
        /// <param name="deviceInfo">Device info from the detected Pico device.</param>
        /// <param name="targetName">Name of the target to update.</param>
        /// <param name="updateFw">Set to <see langword="true"/> to force download of firmware package.</param>
        /// <param name="fwVersion">Firmware version to update to.</param>
        /// <param name="preview">Set to <see langword="true"/> to use preview version to update.</param>
        /// <param name="archiveDirectoryPath">Path to the archive directory. Pass <c>null</c> if there is no archive.</param>
        /// <param name="clrFile">Path to a custom CLR binary file. Pass <c>null</c> to download from Cloudsmith.</param>
        /// <param name="verbosity">Set verbosity level of progress and error messages.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
            PicoDeviceInfo deviceInfo,
            string targetName,
            bool updateFw,
            string fwVersion,
            bool preview,
            string archiveDirectoryPath,
            string clrFile,
            VerbosityLevel verbosity)
        {
            // if target name was not provided, try to infer from device
            if (string.IsNullOrEmpty(targetName))
            {
                targetName = InferTargetName(deviceInfo);

                if (string.IsNullOrEmpty(targetName))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine("Could not determine target name from connected device. Please specify --target.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3004;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.WriteLine($"Inferred target: {targetName}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }

            // determine UF2 family ID from device info
            uint familyId = deviceInfo.FamilyId;

            string binFilePath;

            ExitCodes operationResult;

            // if a custom CLR file was provided, use it directly
            if (!string.IsNullOrEmpty(clrFile))
            {
                if (!File.Exists(clrFile))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"CLR file not found: {clrFile}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3003;
                }

                binFilePath = clrFile;

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine($"Using custom CLR file: {binFilePath}");
                }
            }
            else
            {
                // download and extract firmware
                PicoFirmware firmware = new PicoFirmware(
                    targetName,
                    fwVersion,
                    preview);

                firmware.Verbosity = verbosity;

                operationResult = await firmware.DownloadAndExtractAsync(archiveDirectoryPath);

                if (operationResult != ExitCodes.OK)
                {
                    return operationResult;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine($"Firmware: {firmware.Version}");
                    OutputWriter.WriteLine($"Binary: {firmware.BinFilePath}");
                }

                binFilePath = firmware.BinFilePath;
            }

            // read the binary and convert to UF2
            byte[] binData;

            try
            {
                binData = File.ReadAllBytes(binFilePath);
            }
            catch (Exception ex)
            {
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Error reading binary file: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                return ExitCodes.E3003;
            }

            byte[] uf2Data;

            // check if the file is already in UF2 format
            if (PicoUf2Utility.IsUf2Data(binData))
            {
                uf2Data = binData;

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine("File is already in UF2 format, skipping conversion.");
                }

                // validate the UF2 file integrity
                if (!PicoUf2Utility.ValidateUf2Data(uf2Data, verbosity))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine("UF2 validation failed. The file may be corrupt.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3003;
                }
            }
            else
            {
                try
                {
                    uf2Data = PicoUf2Utility.ConvertBinToUf2(binData, PicoFirmware.DefaultBaseAddress, familyId);
                }
                catch (Exception ex)
                {
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"Error converting firmware to UF2: {ex.Message}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    return ExitCodes.E3003;
                }
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"UF2 size: {uf2Data.Length} bytes ({uf2Data.Length / 512} blocks)");
            }

            // deploy to UF2 drive
            string drivePath = deviceInfo.DrivePath;

            if (string.IsNullOrEmpty(drivePath) || !Directory.Exists(drivePath))
            {
                // try to find drive again
                drivePath = PicoUf2Utility.FindUf2Drive();

                if (drivePath == null)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("UF2 drive not found. Please ensure device is in BOOTSEL mode.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    // wait for it
                    drivePath = PicoUf2Utility.WaitForDrive(DriveWaitTimeoutMs, verbosity);

                    if (drivePath == null)
                    {
                        return ExitCodes.E3005;
                    }
                }
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine($"Deploying firmware to {drivePath}...");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            string deployFileName = Path.GetFileName(binFilePath);
            operationResult = PicoUf2Utility.DeployUf2File(uf2Data, drivePath, deployFileName);

            if (operationResult != ExitCodes.OK)
            {
                return operationResult;
            }

            // wait for device to reboot (drive should disappear)
            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("Waiting for device to reboot...");
            }

            bool driveRemoved = PicoUf2Utility.WaitForDriveRemoval(drivePath, 10_000, verbosity);

            if (!driveRemoved)
            {
                // drive still present after 5s — force eject
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("Drive still present. Ejecting...");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                bool ejected = PicoUf2Utility.EjectDrive(drivePath);

                if (ejected)
                {
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine("Drive ejected successfully. Please unplug and replug the device to reboot it.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }
                else
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("WARNING: Could not eject drive. Please safely remove the device manually.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }

            if (verbosity > VerbosityLevel.Quiet)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Firmware updated successfully.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return operationResult;
        }

        /// <summary>
        /// Infer target name from detecting device characteristics.
        /// </summary>
        private static string InferTargetName(PicoDeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
            {
                return "RASPBERRY_PI_PICO";
            }

            if (deviceInfo.ChipType == "RP2350")
            {
                return "RASPBERRY_PI_PICO2";
            }

            return "RASPBERRY_PI_PICO";
        }

        /// <summary>
        /// Perform firmware update via the PICOBOOT USB protocol (direct flash, no UF2 conversion).
        /// </summary>
        /// <param name="picobootDevice">Connected PICOBOOT device.</param>
        /// <param name="targetName">Target name to update.</param>
        /// <param name="fwVersion">Firmware version.</param>
        /// <param name="preview"><see langword="true"/> for preview version.</param>
        /// <param name="archiveDirectoryPath">Path to firmware archive, or <c>null</c>.</param>
        /// <param name="clrFile">Path to a custom CLR binary file. Pass <c>null</c> to download from Cloudsmith.</param>
        /// <param name="verify">Set to <see langword="true"/> to verify after writing.</param>
        /// <param name="reboot">Set to <see langword="true"/> to reboot device after flashing.</param>
        /// <param name="verbosity">Verbosity level.</param>
        /// <returns>Exit code indicating result.</returns>
        public static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareViaPicoBootAsync(
            PicoBootDevice picobootDevice,
            string targetName,
            string fwVersion,
            bool preview,
            string archiveDirectoryPath,
            string clrFile,
            bool verify,
            bool reboot,
            VerbosityLevel verbosity)
        {
            if (picobootDevice == null)
            {
                return ExitCodes.E3001;
            }

            // infer target name from chip type if not provided
            if (string.IsNullOrEmpty(targetName))
            {
                targetName = picobootDevice.ChipType == "RP2350"
                    ? "RASPBERRY_PI_PICO2"
                    : "RASPBERRY_PI_PICO";

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.WriteLine($"Inferred target: {targetName}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }

            string binFilePath;
            ExitCodes result;

            // if a custom CLR file was provided, use it directly
            if (!string.IsNullOrEmpty(clrFile))
            {
                if (!File.Exists(clrFile))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"CLR file not found: {clrFile}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3003;
                }

                binFilePath = clrFile;

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine($"Using custom CLR file: {binFilePath}");
                }
            }
            else
            {
                // download and extract firmware
                PicoFirmware firmware = new PicoFirmware(targetName, fwVersion, preview);
                firmware.Verbosity = verbosity;

                ExitCodes downloadResult = await firmware.DownloadAndExtractAsync(archiveDirectoryPath);

                if (downloadResult != ExitCodes.OK)
                {
                    return downloadResult;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine($"Firmware: {firmware.Version}");
                    OutputWriter.WriteLine($"Binary: {firmware.BinFilePath}");
                }

                binFilePath = firmware.BinFilePath;
            }

            // read the raw binary (no UF2 conversion needed for PICOBOOT)
            byte[] binData;

            try
            {
                binData = File.ReadAllBytes(binFilePath);
            }
            catch (Exception ex)
            {
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Error reading firmware binary: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                return ExitCodes.E3003;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Binary size: {binData.Length} bytes");
                OutputWriter.WriteLine("Using PICOBOOT direct flash...");
            }

            // flash directly via PICOBOOT protocol
            result = picobootDevice.UpdateFirmware(binData, PicoFirmware.DefaultBaseAddress, verbosity);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            // verify if requested
            if (verify)
            {
                result = picobootDevice.VerifyFirmware(binData, PicoFirmware.DefaultBaseAddress, verbosity);

                if (result != ExitCodes.OK)
                {
                    return result;
                }
            }

            // reboot the device if requested
            if (reboot)
            {
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("Rebooting device...");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                picobootDevice.Reboot();
            }

            if (verbosity > VerbosityLevel.Quiet)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Firmware updated successfully via PICOBOOT.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }
    }
}
