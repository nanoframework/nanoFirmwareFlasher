//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;

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
        /// <param name="tool"><see cref="EspTool"/> to use when performing update.</param>
        /// <param name="device"><see cref="Esp32DeviceInfo"/> of device to update.</param>
        /// <param name="targetName">Name of the target to update.</param>
        /// <param name="updateFw">Set to <see langword="true"/> to force download of firmware package.</param>
        /// <param name="fwVersion">Firmware version to update to.</param>
        /// <param name="preview">Set to <see langword="true"/> to use preview version to update.</param>
        /// <param name="applicationPath">Path to application to update along with the firmware update.</param>
        /// <param name="deploymentAddress">Flash address to use when deploying an aplication.</param>
        /// <param name="clrFile">Path to CLR file to use for firmware update.</param>
        /// <param name="fitCheck"><see langword="true"/> to perform validation of update package against connected target.</param>
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
            VerbosityLevel verbosity,
            PartitionTableSize? partitionTableSize)
        {
            var operationResult = ExitCodes.OK;
            uint address = 0;
            bool updateCLRfile = !string.IsNullOrEmpty(clrFile);

            // perform sanity checks for the specified target against the connected device details
            if (esp32Device.ChipType != "ESP32" &&
                esp32Device.ChipType != "ESP32-S2" &&
                esp32Device.ChipType != "ESP32-C3")
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
                    if (esp32Device.ChipName.Contains("PICO"))
                    {
                        targetName = "ESP32_PICO";
                    }
                    else
                    {
                        var revisionSuffix = "REV0";
                        var psRamSegment = "";
                        var otherSegment = "";

                        if (esp32Device.ChipName.Contains("revision 3"))
                        {
                            revisionSuffix = "REV3";
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
                            revisionSuffix = "REV0";
                        }

                        if (esp32Device.ChipName.Contains("ESP32_C3"))
                        {
                            if (esp32Device.ChipName.Contains("revision 2"))
                            {
                                revisionSuffix = "REV2";
                            }
                            else
                            {
                                // all the others (rev3 and rev4) will take rev3
                                revisionSuffix = "REV3";
                            }
                        }

                        // compose target name
                        targetName = $"ESP32{psRamSegment}{otherSegment}_{revisionSuffix}";
                    }
                }
                else if (esp32Device.ChipType == "ESP32-S2")
                {
                    // can't guess with certainty for this series, better request a target name to the user

                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("");
                    Console.WriteLine($"For ESP32-S2 series nanoff isn't able to make an educated guess on the best target to use.");
                    Console.WriteLine($"Please provide a valid target name using this option '--target MY_ESP32_S2_TARGET' instead of '--platform esp32'.");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E9000;
                }
                else if (esp32Device.ChipType == "ESP32-C3")
                {
                    string revisionSuffix;

                    if (esp32Device.ChipName.Contains("revision 2"))
                    {
                        revisionSuffix = "REV2";
                    }
                    else if (esp32Device.ChipName.Contains("revision 3") || esp32Device.ChipName.Contains("revision 4"))
                    {
                        // all the others (rev3 and rev4) will take rev3
                        revisionSuffix = "REV3";
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
                    targetName = $"ESP32_C3_{revisionSuffix}";
                }


                Console.ForegroundColor = ConsoleColor.Blue;

                Console.WriteLine("");
                Console.WriteLine($"No target name was provided! Using '{targetName}' based on the device characteristics.");
                Console.WriteLine("");

                Console.ForegroundColor = ConsoleColor.White;
            }

            if (fitCheck)
            {
                if (targetName.EndsWith("REV3") &&
                    (esp32Device.ChipName.Contains("revision 0") ||
                    esp32Device.ChipName.Contains("revision 1") ||
                    esp32Device.ChipName.Contains("revision 2")))
                {
                    // trying to use a target that's not compatible with the connected device 
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("");
                    Console.WriteLine("***************************************** WARNING ****************************************");
                    Console.WriteLine("Seems that the firmware image that's about to be used is for a revision 3 device, but the");
                    Console.WriteLine($"connected device is {esp32Device.ChipName}. You should use the 'ESP32_WROOM_32' instead.");
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

                    // add DEPLOYMENT partition with the address provided in the command OR the address from the partition table
                    firmware.FlashPartitions = new Dictionary<int, string>()
                    {
                        {
                            address != 0 ? (int)address : firmware.DeploymentPartitionAddress,
                            applicationBinary
                        }
                    };
                }
                else
                {
                    return ExitCodes.E9008;
                }
            }

            if (updateFw)
            {
                // updating fw calls for a flash erase
                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"Erasing flash...");
                }

                // erase flash
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
                    Console.WriteLine($"Flashing firmware...");
                }

                // write to flash
                operationResult = espTool.WriteFlash(firmware.FlashPartitions);

                if (operationResult == ExitCodes.OK)
                {
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("OK");

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
            }

            return operationResult;
        }
    }
}
