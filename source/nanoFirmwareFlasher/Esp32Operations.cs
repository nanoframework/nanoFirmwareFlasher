//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.IO;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class Esp32Operations
    {
        // This is the only official ESP32 target available, so it's OK to use this as the target 
        // name whenever ESP32 is the specified platform
        private const string _esp32TargetName = "ESP32_WROOM_32";

        public static ExitCodes BackupFlash(
            EspTool tool, 
            EspTool.DeviceInfo device,
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
                fileName = $"{device.ChipName}_0x{device.MacAddress:X}_{DateTime.UtcNow.ToShortDateString()}.bin";
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
                Console.WriteLine($"Backing up the firmware to \r\n{backupFilePath}...");
            }

            tool.BackupFlash(backupFilePath, device.FlashSize);

            if (verbosity > VerbosityLevel.Quiet)
            {
                Console.WriteLine($"Flash backup saved to {fileName}");
            }

            return ExitCodes.OK;
        }

        internal static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
            EspTool espTool, 
            EspTool.DeviceInfo esp32Device, 
            string targetName, 
            string fwVersion, 
            bool stable, 
            string applicationPath, 
            VerbosityLevel verbosity)
        {
            // if a target name wasn't specified use the default (and only available) ESP32 target
            if(string.IsNullOrEmpty(targetName))
            {
                targetName = _esp32TargetName;
            }

            Esp32Firmware firmware = new Esp32Firmware(
                targetName, 
                fwVersion, 
                stable)
            {
                Verbosity = verbosity
            };

            var operationResult = await firmware.DownloadAndExtractAsync(esp32Device.FlashSize);
            if (operationResult == ExitCodes.OK)
            {
                // download successful

                // need to include application file?
                if(!string.IsNullOrEmpty(applicationPath))
                {
                    // check application file
                    if (File.Exists(applicationPath))
                    {
                        string applicationBinary = new FileInfo(applicationPath).FullName;
                        firmware.FlashPartitions.Add(
                            firmware.DeploymentPartionAddress, 
                            applicationBinary);
                    }
                    else
                    {
                        return ExitCodes.E9008;
                    }
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write($"Erasing flash...");
                }

                // erase flash
                espTool.EraseFlash();

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine("OK");
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write($"Flashing firmware...");
                }

                // write to flash
                espTool.WriteFlash(firmware.FlashPartitions);

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine("OK");
                }
            }

            return operationResult;
        }
    }
}
