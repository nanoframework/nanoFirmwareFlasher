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
        /// Perform a mass erase via UF2 by writing a zero-filled UF2 that covers
        /// the entire flash. The bootloader will write zeros to every sector,
        /// effectively erasing all firmware and application data.
        /// </summary>
        /// <param name="deviceInfo">Device info from the detected Pico device.</param>
        /// <param name="verbosity">Verbosity level.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public static ExitCodes MassEraseViaUf2(
            PicoDeviceInfo deviceInfo,
            VerbosityLevel verbosity)
        {
            uint flashSize = PicoFirmware.DefaultFlashSize;
            uint familyId = deviceInfo.FamilyId;

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Generating erase image for {flashSize / 1024}KB flash...");
            }

            // create a zero-filled binary the size of the entire flash
            byte[] zeroData = new byte[flashSize];
            byte[] uf2Data = PicoUf2Utility.ConvertBinToUf2(zeroData, PicoFirmware.DefaultBaseAddress, familyId);

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Erase UF2: {uf2Data.Length:N0} bytes ({uf2Data.Length / 512} blocks)");
            }

            // find the UF2 drive
            string drivePath = deviceInfo.DrivePath;

            if (string.IsNullOrEmpty(drivePath) || !Directory.Exists(drivePath))
            {
                drivePath = PicoUf2Utility.FindUf2Drive();

                if (drivePath == null)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("UF2 drive not found. Please ensure device is in BOOTSEL mode.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

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
                OutputWriter.WriteLine($"Erasing flash via {drivePath}... This may take a moment.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            ExitCodes operationResult = PicoUf2Utility.DeployUf2File(uf2Data, drivePath, "flash_erase.uf2");

            if (operationResult != ExitCodes.OK)
            {
                return operationResult;
            }

            // wait for device to reboot
            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("Waiting for device to reboot after erase...");
            }

            bool driveRemoved = PicoUf2Utility.WaitForDriveRemoval(drivePath, 30_000, verbosity);

            if (!driveRemoved)
            {
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
                OutputWriter.WriteLine("Mass erase complete.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

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
        /// <param name="massErase">When <c>true</c>, pad the UF2 with zero-filled blocks to erase the entire flash.</param>
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
            bool massErase,
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

            // if mass erase requested, pad the UF2 to cover the entire flash
            if (massErase)
            {
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    OutputWriter.WriteLine($"Mass erase: padding UF2 to cover full {PicoFirmware.DefaultFlashSize / 1024}KB flash...");
                }

                uf2Data = PicoUf2Utility.PadUf2ToFullFlash(uf2Data, PicoFirmware.DefaultBaseAddress, PicoFirmware.DefaultFlashSize, familyId);
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
                // drive still present after 5s - force eject
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
        /// Deploy a managed application to a Raspberry Pi Pico device via UF2 mass storage.
        /// The application binary is converted to UF2 format and written to the deployment region.
        /// </summary>
        /// <param name="deviceInfo">Device info from the detected Pico device.</param>
        /// <param name="applicationPath">Path to the application binary or UF2 file.</param>
        /// <param name="deploymentAddress">Deployment flash address. If <c>null</c> or empty, uses the default deployment address.</param>
        /// <param name="verbosity">Verbosity level.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public static ExitCodes DeployApplication(
            PicoDeviceInfo deviceInfo,
            string applicationPath,
            string deploymentAddress,
            VerbosityLevel verbosity)
        {
            if (string.IsNullOrEmpty(applicationPath))
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("No deployment image specified. Use --image to provide the application binary.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E9008;
            }

            if (!File.Exists(applicationPath))
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Deployment image not found: {applicationPath}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E9008;
            }

            // determine deployment address
            uint address = PicoFirmware.DefaultDeploymentAddress;

            if (!string.IsNullOrEmpty(deploymentAddress))
            {
                string hexStr = deploymentAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? deploymentAddress.Substring(2)
                    : deploymentAddress;

                if (!uint.TryParse(hexStr, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out address))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Invalid deployment address: {deploymentAddress}. Use hexadecimal format (e.g. 0x10080000).");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E9009;
                }
            }

            // read the application file
            byte[] appData;

            try
            {
                appData = File.ReadAllBytes(applicationPath);
            }
            catch (Exception ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Error reading deployment image: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return ExitCodes.E9008;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Deployment image: {applicationPath} ({appData.Length:N0} bytes)");
                OutputWriter.WriteLine($"Target address: 0x{address:X8}");
            }

            byte[] uf2Data;

            // check if the file is already in UF2 format
            if (PicoUf2Utility.IsUf2Data(appData))
            {
                uf2Data = appData;

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine("File is already in UF2 format, skipping conversion.");
                }

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
                // convert to UF2 at the deployment address
                uint familyId = deviceInfo.FamilyId;

                try
                {
                    uf2Data = PicoUf2Utility.ConvertBinToUf2(appData, address, familyId);
                }
                catch (Exception ex)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Error converting application to UF2: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E3003;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Converted to UF2: {uf2Data.Length:N0} bytes ({uf2Data.Length / 512} blocks)");
                }
            }

            // find the UF2 drive
            string drivePath = deviceInfo.DrivePath;

            if (string.IsNullOrEmpty(drivePath) || !Directory.Exists(drivePath))
            {
                drivePath = PicoUf2Utility.FindUf2Drive();

                if (drivePath == null)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine("UF2 drive not found. Please ensure device is in BOOTSEL mode.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;

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
                OutputWriter.WriteLine($"Deploying application to {drivePath}...");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            string deployFileName = Path.GetFileName(applicationPath);
            ExitCodes operationResult = PicoUf2Utility.DeployUf2File(uf2Data, drivePath, deployFileName);

            if (operationResult != ExitCodes.OK)
            {
                return operationResult;
            }

            // wait for device to reboot
            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("Waiting for device to reboot...");
            }

            bool driveRemoved = PicoUf2Utility.WaitForDriveRemoval(drivePath, 10_000, verbosity);

            if (!driveRemoved)
            {
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
                OutputWriter.WriteLine("Application deployed successfully.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Infer target name from detecting device characteristics.
        /// </summary>
        private static string InferTargetName(PicoDeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
            {
                return "RP_PICO_RP2040";
            }

            if (deviceInfo.ChipType == "RP2350")
            {
                return "RP_PICO_RP2350";
            }

            return "RP_PICO_RP2040";
        }
    }
}
