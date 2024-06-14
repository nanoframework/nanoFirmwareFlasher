//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class with operations available in ESP32 devices.
    /// </summary>
    public class Esp32Operations
    {
        /// <summary>
        /// Perform backup of current flash content of ESP32 device.
        /// </summary>
        /// <param name="tool"><see cref="EspTool"/> to use when performing update.</param>
        /// <param name="device"><see cref="Esp32DeviceInfo"/> of device to update.</param>
        /// <param name="backupPath">Path to store backup image.</param>
        /// <param name="fileName">Name of backup file.</param>
        /// <param name="verbosity">Set verbosity level of progress and error messages.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public static ExitCodes BackupFlash(
            EspTool tool,
            Esp32DeviceInfo device,
            string backupPath,
            string fileName,
            VerbosityLevel verbosity)
        {
            // check for backup file without backup path
            if (!string.IsNullOrEmpty(fileName) &&
                string.IsNullOrEmpty(backupPath))
            {
                // backup file without backup path
                return ExitCodes.E9004;
            }

            // check if directory exists, if it doesn't, try to create
            if (!Directory.Exists(backupPath))
            {
                try
                {
                    Directory.CreateDirectory(backupPath);
                }
                catch
                {
                    return ExitCodes.E9002;
                }
            }

            // file name specified
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{device.ChipName}_0x{device.MacAddress}_{DateTime.UtcNow.ToShortDateString()}.bin";
            }

            var backupFilePath = Path.Combine(backupPath, fileName);

            // check file existence
            if (File.Exists(fileName))
            {
                try
                {
                    File.Delete(backupFilePath);
                }
                catch
                {
                    return ExitCodes.E9003;
                }
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Backing up the firmware to \r\n{backupFilePath}...");
                Console.ForegroundColor = ConsoleColor.White;
            }

            tool.BackupFlash(backupFilePath, device.FlashSize);

            if (verbosity > VerbosityLevel.Quiet)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"Flash backup saved to {fileName}");
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Perform firmware update on a ESP32 device.
        /// </summary>
        /// <param name="espTool"><see cref="EspTool"/> to use when performing update.</param>
        /// <param name="esp32Device"><see cref="Esp32DeviceInfo"/> of device to update.</param>
        /// <param name="targetName">Name of the target to update.</param>
        /// <param name="updateFw">Set to <see langword="true"/> to force download of firmware package.</param>
        /// <param name="fwVersion">Firmware version to update to.</param>
        /// <param name="preview">Set to <see langword="true"/> to use preview version to update.</param>
        /// <param name="applicationPath">Path to application to update along with the firmware update.</param>
        /// <param name="deploymentAddress">Flash address to use when deploying an aplication.</param>
        /// <param name="clrFile">Path to CLR file to use for firmware update.</param>
        /// <param name="fitCheck"><see langword="true"/> to perform validation of update package against connected target.</param>
        /// <param name="massErase">If <see langword="true"/> perform mass erase on device before updating.</param>
        /// <param name="verbosity">Set verbosity level of progress and error messages.</param>
        /// <param name="partitionTableSize">Size of partition table.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
            EspTool espTool,
            Esp32DeviceInfo esp32Device,
            string targetName,
            bool updateFw,
            string fwVersion,
            bool preview,
            string applicationPath,
            string deploymentAddress,
            string clrFile,
            bool fitCheck,
            bool massErase,
            VerbosityLevel verbosity,
            PartitionTableSize? partitionTableSize)
        {
            var operationResult = ExitCodes.OK;
            uint address = 0;
            bool updateCLRfile = !string.IsNullOrEmpty(clrFile);

            // perform sanity checks for the specified target against the connected device details
            if (esp32Device.ChipType != "ESP32" &&
                esp32Device.ChipType != "ESP32-C3" &&
                esp32Device.ChipType != "ESP32-C6" &&
                esp32Device.ChipType != "ESP32-H2" &&
                esp32Device.ChipType != "ESP32-S2" &&
                esp32Device.ChipType != "ESP32-S3")
            {
                // connected to a device not supported
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("");
                Console.WriteLine("******************************* WARNING *******************************");
                Console.WriteLine("Seems that the connected device is not supported by .NET nanoFramework");
                Console.WriteLine("Most likely it won't boot");
                Console.WriteLine("************************************************************************");
                Console.WriteLine("");
            }

            // if a target name wasn't specified try to guess from the device characteristics 
            if (string.IsNullOrEmpty(targetName))
            {
                if (esp32Device.ChipType == "ESP32")
                {
                    // version schema for ESP32
                    //Previously Used Schemes | Previous Identification   | vM.X
                    //            V0          |           0               | v0.0
                    //         ECO, V1        |           1               | v1.0
                    //         ECO, V3        |           3               | v3.0

                    if (esp32Device.ChipName.Contains("PICO"))
                    {
                        targetName = "ESP32_PICO";
                    }
                    else
                    {
                        var revisionSuffix = "_REV0";
                        var psRamSegment = "";
                        var otherSegment = "";

                        if (esp32Device.ChipName.Contains("revision v3"))
                        {
                            revisionSuffix = "_REV3";
                        }

                        if (esp32Device.PSRamAvailable == PSRamAvailability.Yes)
                        {
                            psRamSegment = "_PSRAM";
                        }

                        if (esp32Device.Crystal.StartsWith("26"))
                        {
                            // this one requires the 26MHz version
                            otherSegment = "_XTAL26";

                            // and we only have a version with PSRAM support, so force that
                            psRamSegment = "_PSRAM";

                            // also need to force rev0 even if that's higher
                            revisionSuffix = "_REV0";
                        }

                        // compose target name
                        targetName = $"ESP32{psRamSegment}{otherSegment}{revisionSuffix}";
                    }

                    if (fitCheck)
                    {
                        if (targetName.EndsWith("REV3") &&
                            (esp32Device.ChipName.Contains("revision v0") ||
                            esp32Device.ChipName.Contains("revision v1") ||
                            esp32Device.ChipName.Contains("revision v2")))
                        {
                            // trying to use a target that's not compatible with the connected device 
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("");
                            Console.WriteLine("***************************************** WARNING ****************************************");
                            Console.WriteLine("Seems that the firmware image that's about to be used is for a revision 3 device, but the");
                            Console.WriteLine($"connected device is {esp32Device.ChipName}.");
                            Console.WriteLine("******************************************************************************************");
                            Console.WriteLine("");
                        }

                        if (targetName.Contains("BLE") &&
                            !esp32Device.Features.Contains(", BT,"))
                        {
                            // trying to use a traget with BT and the connected device doens't have support for it
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("");
                            Console.WriteLine("******************************************* WARNING *******************************************");
                            Console.WriteLine("Seems that the firmware image that's about to be used includes Bluetooth features, but the");
                            Console.WriteLine($"connected device does not have support for it. You should use a target without BLE in the name.");
                            Console.WriteLine("************************************************************************************************");
                            Console.WriteLine("");
                        }
                    }
                }
                else if (esp32Device.ChipType == "ESP32-C3")
                {
                    // version schema for ESP32-C3
                    //Previously Used Schemes | Previous Identification   | vM.X
                    //     Chip Revision 2    |           2               | v0.2
                    //     Chip Revision 3    |           3               | v0.3
                    //     Chip Revision 4    |           4               | v0.4

                    string revisionSuffix;

                    if (esp32Device.ChipName.Contains("revision v0.2"))
                    {
                        // this is the "default" one we're offering
                        revisionSuffix = "";
                    }
                    else if (esp32Device.ChipName.Contains("revision v0.3") || esp32Device.ChipName.Contains("revision v0.4"))
                    {
                        // all the others (rev3 and rev4) will take rev3
                        revisionSuffix = "_REV3";
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine("");
                        Console.WriteLine($"Unsupported ESP32_C3 revision.");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.E9000;
                    }

                    // compose target name
                    targetName = $"ESP32_C3{revisionSuffix}";
                }
                else if (esp32Device.ChipType == "ESP32-C6")
                {
                    // version schema for ESP32-C6

                    string revisionSuffix;

                    // so far we are only offering a single ESP32_C6 build
                    if (esp32Device.ChipName.Contains("revision v0.0") || esp32Device.ChipName.Contains("revision v0.1"))
                    {
                        revisionSuffix = "";
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine("");
                        Console.WriteLine($"Unsupported ESP32_C6 revision.");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.E9000;
                    }

                    // compose target name
                    targetName = $"ESP32_C6{revisionSuffix}";
                }
                else if (esp32Device.ChipType == "ESP32-H2")
                {
                    // version schema for ESP32-H2

                    string revisionSuffix;

                    // so far we are only offering a single ESP32_H2 build
                    if (esp32Device.ChipName.Contains("revision v0.1") || esp32Device.ChipName.Contains("revision v0.2"))
                    {
                        revisionSuffix = "";
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine("");
                        Console.WriteLine($"Unsupported ESP32_H2 revision.");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.E9000;
                    }

                    // compose target name
                    targetName = $"ESP32_H2{revisionSuffix}";
                }
                else if (esp32Device.ChipType == "ESP32-S2")
                {
                    // version schema for ESP32-S2
                    //Previously Used Schemes | Previous Identification   | vM.X
                    //           n/a          |           0               | v0.0
                    //           ECO1         |           1               | v1.0

                    // can't guess with certainty for this series, better request a target name to the user

                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("");
                    Console.WriteLine($"For ESP32-S2 series nanoff isn't able to make an educated guess on the best target to use.");
                    Console.WriteLine($"Please provide a valid target name using this option '--target MY_ESP32_S2_TARGET' instead of '--platform esp32'.");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E9000;
                }
                else if (esp32Device.ChipType == "ESP32-S3")
                {
                    // version schema for ESP32-S3
                    //Previously Used Schemes | Previous Identification   | vM.X
                    //          V000          |           n/a             | v0.0
                    //          V001          |     0 (bug in logs)       | v0.1
                    //          V002          |           n/a             | v0.2

                    string revisionSuffix;

                    // so far we are only offering a single ESP32_S3 build
                    if (esp32Device.ChipName.Contains("revision v0.0") || esp32Device.ChipName.Contains("revision v0.1") || esp32Device.ChipName.Contains("revision v0.2"))
                    {
                        revisionSuffix = "";
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine("");
                        Console.WriteLine($"Unsupported ESP32_S3 revision.");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.E9000;
                    }

                    // compose target name
                    targetName = $"ESP32_S3{revisionSuffix}";
                }

                Console.ForegroundColor = ConsoleColor.Blue;

                Console.WriteLine("");
                Console.WriteLine($"No target name was provided! Using '{targetName}' based on the device characteristics.");
                Console.WriteLine("");

                Console.ForegroundColor = ConsoleColor.White;
            }

            Esp32Firmware firmware = new Esp32Firmware(
                targetName,
                fwVersion,
                preview,
                partitionTableSize)
            {
                Verbosity = verbosity
            };

            // if this is updating with a local CLR file, download the package silently
            if (updateCLRfile)
            {
                // check file
                if (!File.Exists(clrFile))
                {
                    return ExitCodes.E9011;
                }

                // has to be a binary file
                if (Path.GetExtension(clrFile) != ".bin")
                {
                    return ExitCodes.E9012;
                }

                // make sure path is absolute
                clrFile = Utilities.MakePathAbsolute(
                    Environment.CurrentDirectory,
                    clrFile);

                firmware.Verbosity = VerbosityLevel.Quiet;
            }

            // need to download update package?
            if (updateFw)
            {
                operationResult = await firmware.DownloadAndExtractAsync(esp32Device);

                if (operationResult != ExitCodes.OK)
                {
                    return operationResult;
                }
                // download successful
            }

            // if updating with a CRL file, need to have a new fw package
            if (updateCLRfile)
            {
                // remove the CLR file from the image
                firmware.FlashPartitions.Remove(Esp32Firmware.CLRAddress);

                // add it back with the file image from the command line option
                firmware.FlashPartitions.Add(Esp32Firmware.CLRAddress, clrFile);
            }

            // need to include application file?
            if (!string.IsNullOrEmpty(applicationPath))
            {
                // check application file
                if (File.Exists(applicationPath))
                {
                    // this operation includes a deployment image
                    // try parsing the deployment address from parameter, if provided
                    if (!string.IsNullOrEmpty(deploymentAddress))
                    {
                        // need to remove the leading 0x and to specify that hexadecimal values are allowed
                        if (!uint.TryParse(deploymentAddress.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out address))
                        {
                            return ExitCodes.E9009;
                        }
                    }

                    string applicationBinary = new FileInfo(applicationPath).FullName;

                    // check for empty flash partitions
                    if (firmware.FlashPartitions is null)
                    {
                        firmware.FlashPartitions = new Dictionary<int, string>();
                    }

                    // add DEPLOYMENT partition with the address provided in the command OR the address from the partition table
                    firmware.FlashPartitions.Add(
                            address != 0 ? (int)address : firmware.DeploymentPartitionAddress,
                            applicationBinary
                    );

                }
                else
                {
                    return ExitCodes.E9008;
                }
            }

            if (updateFw
                && massErase)
            {
                // erase flash, if masse erase was requested
                // updating fw calls for a flash erase
                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"Erasing flash...");
                }

                operationResult = espTool.EraseFlash();

                if (operationResult == ExitCodes.OK)
                {
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("OK");
                    }
                    else
                    {
                        Console.WriteLine("");
                    }
                }
            }

            if (operationResult == ExitCodes.OK)
            {
                Console.ForegroundColor = ConsoleColor.White;

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write($"Flashing firmware...");
                }

                int configPartitionAddress = 0;
                int configPartitionSize = 0;
                string configPartitionBackup = Path.GetTempFileName();

                // if mass erase wasn't requested, backup config partitition
                if (!massErase)
                {
                    // check if the update file includes a partition table
                    if (File.Exists(Path.Combine(firmware.LocationPath, $"partitions_nanoclr_{Esp32DeviceInfo.GetFlashSizeAsString(esp32Device.FlashSize).ToLowerInvariant()}.csv")))
                    {
                        // can't do this without a partition table


                        // compose path to partition file
                        string partitionCsvFile = Path.Combine(firmware.LocationPath, $"partitions_nanoclr_{Esp32DeviceInfo.GetFlashSizeAsString(esp32Device.FlashSize).ToLowerInvariant()}.csv");

                        var partitionDetails = File.ReadAllText(partitionCsvFile);

                        // grab details for the config partition
                        string pattern = @"config,.*?(0x[0-9A-Fa-f]+),.*?(0x[0-9A-Fa-f]+),";
                        Regex regex = new Regex(pattern);
                        Match match = regex.Match(partitionDetails);

                        if (match.Success)
                        {
                            // just try to parse, ignore failures
                            int.TryParse(match.Groups[1].Value.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out configPartitionAddress);
                            int.TryParse(match.Groups[2].Value.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out configPartitionSize);
                        }

                        // backup config partition
                        // ignore failures
                        _ = espTool.BackupConfigPartition(
                            configPartitionBackup,
                            configPartitionAddress,
                            configPartitionSize);

                        firmware.FlashPartitions.Add(configPartitionAddress, configPartitionBackup);
                    }
                }

                // write to flash
                operationResult = espTool.WriteFlash(firmware.FlashPartitions);

                if (operationResult == ExitCodes.OK)
                {
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("OK".PadRight(110));

                        // warn user if reboot is not possible
                        if (espTool.CouldntResetTarget)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;

                            Console.WriteLine("");
                            Console.WriteLine("**********************************************");
                            Console.WriteLine("The connected device is in 'download mode'.");
                            Console.WriteLine("Please reset the chip manually to run nanoCLR.");
                            Console.WriteLine("**********************************************");
                            Console.WriteLine("");

                            Console.ForegroundColor = ConsoleColor.White;
                        }
                    }
                    else
                    {
                        Console.WriteLine("");
                    }
                }

                // delete config partition backup
                try
                {
                    if (File.Exists(configPartitionBackup))
                    {
                        File.Delete(configPartitionBackup);
                    }
                }
                catch
                {
                    // don't care
                }

                Console.ForegroundColor = ConsoleColor.White;
            }

            return operationResult;
        }

        /// <summary>
        /// Deplay application on a ESP32 device.
        /// </summary>
        /// <param name="espTool"><see cref="EspTool"/> to use when performing update.</param>
        /// <param name="esp32Device"><see cref="Esp32DeviceInfo"/> of device to update.</param>
        /// <param name="targetName">Name of the target to update.</param>
        /// <param name="applicationPath">Path to application to update along with the firmware update.</param>
        /// <param name="deploymentAddress">Flash address to use when deploying an aplication.</param>
        /// <param name="verbosity">Set verbosity level of progress and error messages.</param>
        /// <param name="partitionTableSize">Size of partition table.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public static async System.Threading.Tasks.Task<ExitCodes> DeployApplicationAsync(
            EspTool espTool,
            Esp32DeviceInfo esp32Device,
            string targetName,
            string applicationPath,
            string deploymentAddress,
            VerbosityLevel verbosity,
            PartitionTableSize? partitionTableSize)
        {
            var operationResult = ExitCodes.OK;
            uint address = 0;

            // perform sanity checks for the specified target against the connected device details
            if (esp32Device.ChipType != "ESP32" &&
                esp32Device.ChipType != "ESP32-C3" &&
                esp32Device.ChipType != "ESP32-C6" &&
                esp32Device.ChipType != "ESP32-H2" &&
                esp32Device.ChipType != "ESP32-S2" &&
                esp32Device.ChipType != "ESP32-S3")
            {
                // connected to a device not supported
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("");
                Console.WriteLine("******************************* WARNING *******************************");
                Console.WriteLine("Seems that the connected device is not supported by .NET nanoFramework");
                Console.WriteLine("Most likely it won't boot");
                Console.WriteLine("************************************************************************");
                Console.WriteLine("");
            }

            Esp32Firmware firmware = new Esp32Firmware(
                targetName,
                null,
                false,
                partitionTableSize)
            {
                Verbosity = verbosity
            };

            // include application file?
            if (!string.IsNullOrEmpty(applicationPath))
            {
                // check application file
                if (File.Exists(applicationPath))
                {
                    // this operation includes a deployment image
                    // try parsing the deployment address from parameter, if provided
                    if (!string.IsNullOrEmpty(deploymentAddress))
                    {
                        // need to remove the leading 0x and to specify that hexadecimal values are allowed
                        if (!uint.TryParse(deploymentAddress.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out address))
                        {
                            return ExitCodes.E9009;
                        }
                    }

                    string applicationBinary = new FileInfo(applicationPath).FullName;

                    // check for empty flash partitions
                    if (firmware.FlashPartitions is null)
                    {
                        firmware.FlashPartitions = new Dictionary<int, string>();
                    }

                    // add DEPLOYMENT partition with the address provided in the command OR the address from the partition table
                    firmware.FlashPartitions.Add(
                            address != 0 ? (int)address : firmware.DeploymentPartitionAddress,
                            applicationBinary
                    );
                }
                else
                {
                    return ExitCodes.E9008;
                }
            }

            Console.ForegroundColor = ConsoleColor.White;

            if (verbosity >= VerbosityLevel.Normal)
            {
                Console.Write($"Flashing deployment partition...");
            }

            // write to flash
            operationResult = espTool.WriteFlash(firmware.FlashPartitions);

            if (operationResult == ExitCodes.OK)
            {
                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK".PadRight(110));

                    // warn user if reboot is not possible
                    if (espTool.CouldntResetTarget)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;

                        Console.WriteLine("");
                        Console.WriteLine("**********************************************");
                        Console.WriteLine("The connected device is in 'download mode'.");
                        Console.WriteLine("Please reset the chip manually to run nanoCLR.");
                        Console.WriteLine("**********************************************");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;
                    }
                }
                else
                {
                    Console.WriteLine("");
                }
            }

            Console.ForegroundColor = ConsoleColor.White;

            return operationResult;
        }
    }
}
