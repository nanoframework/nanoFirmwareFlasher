//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

#define FEATHER_S2_DELAY

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class Esp32Operations
    {
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
            if(string.IsNullOrEmpty(fileName))
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

            if(verbosity >= VerbosityLevel.Normal)
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

        internal static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
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
                esp32Device.ChipType != "ESP32-S2")
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

                        if (esp32Device.ChipName.Contains("revision 3"))
                        {
                            revisionSuffix = "REV3";
                        }

                        if (esp32Device.PSRamAvailable == PSRamAvailability.Yes)
                        {
                            psRamSegment = "_PSRAM";
                        }

                        // compose target name
                        targetName = $"ESP32{psRamSegment}_{revisionSuffix}";
                    }
                }
                else if (esp32Device.ChipType == "ESP32-S2")
                {
                    // can't guess with certainty for this series, better have the user provide 
                    // a target name

                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("");
                    Console.WriteLine($"No target name was provided! Please provide an appropriate one adding this option '--target MY_ESP32_S2_TARGET'.");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E9000;
                }

                Console.ForegroundColor = ConsoleColor.Blue;

                Console.WriteLine("");
                Console.WriteLine($"No target name was provided! Using '{targetName}' based on the device characteristics.");
                Console.WriteLine("");

                Console.ForegroundColor = ConsoleColor.White;
            }

            if (fitCheck)
            {
                if (targetName.Contains("ESP32_WROOM_32_V3") &&
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
                operationResult = await firmware.DownloadAndExtractAsync(esp32Device.FlashSize);
                
                if (operationResult != ExitCodes.OK)
                {
                    return operationResult;
                }
                // download successful
            }

            // if updating with a CRL file, need to have a new fw package
            if(updateCLRfile)
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
                    if (!updateFw)
                    {
                        // this is a deployment operation only
                        // try parsing the deployment address from parameter
                        // need to remove the leading 0x and to specify that hexadecimal values are allowed
                        if (!uint.TryParse(deploymentAddress.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out address))
                        {
                            return ExitCodes.E9009;
                        }
                    }

                    string applicationBinary = new FileInfo(applicationPath).FullName;
                    firmware.FlashPartitions = new Dictionary<int, string>()
                    {
                        {
                            updateFw ? firmware.DeploymentPartitionAddress : (int)address,
                            applicationBinary
                        }
                    };
                }
                else
                {
                    return ExitCodes.E9008;
                }
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"Erasing flash...");
            }

            if (updateFw)
            {
#if FEATHER_S2_DELAY
                if (targetName.Contains("FEATHER_S2"))
                {
                    Thread.Sleep(TimeSpan.FromSeconds(15));
                }
#endif

                // erase flash
                operationResult = espTool.EraseFlash();
            }
            else
            {
                // erase flash segment

                // need to get deployment address here
                // length must both be multiples of the SPI flash erase sector size. This is 0x1000 (4096) bytes for supported flash chips.

                var fileStream = File.OpenRead(firmware.BootloaderPath);

                uint fileLength = (uint)Math.Ceiling((decimal)fileStream.Length / 0x1000) * 0x1000;

                operationResult = espTool.EraseFlashSegment(address, fileLength);
            }

            if (operationResult == ExitCodes.OK)
            {
#if FEATHER_S2_DELAY
                if (targetName.Contains("FEATHER_S2"))
                {
                    Thread.Sleep(TimeSpan.FromSeconds(15));
                }
#endif

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK");
                }
                else
                {
                    Console.WriteLine("");
                }

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
