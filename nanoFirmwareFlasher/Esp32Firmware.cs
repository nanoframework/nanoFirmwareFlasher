﻿//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of ESP32 firmware files from Bintray.
    /// </summary>
    internal class Esp32Firmware : FirmwarePackage
    {
        /// <summary>
        /// ESP32 nanoCLR is available for 2MB, 4MB, 8MB and 16MB flash sizes
        /// </summary>
        internal List<int> SupportedFlashSizes => new List<int> { 0x200000, 0x400000, 0x800000, 0x1000000 };

        internal Dictionary<int, string> FlashPartitions;


        /// <summary>
        /// Address of the deployment partition.
        /// </summary>
        internal int DeploymentPartionAddress =>  0x110000;

        public Esp32Firmware(string targetName, string fwVersion, bool stable)
            :base(targetName, fwVersion, stable)
        {
        }

        internal async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync(int flashSize)
        {
            string humanReadable = flashSize >= 0x10000 ? $"{ flashSize / 0x100000 }MB" : $"{ flashSize / 0x400 }kB";

            if (!SupportedFlashSizes.Contains(flashSize))
            {
                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    Console.WriteLine($"There is no firmware available for ESP32 with {humanReadable} flash size!{Environment.NewLine}Only the following flash sizes are supported: {string.Join(", ", SupportedFlashSizes.Select(size => size >= 0x10000 ? $"{ size / 0x100000 }MB" : $"{ size / 0x400 }kB."))}");
                }

                return ExitCodes.E4001;
            }

            // perform download and extract
            var executionResult = await DownloadAndExtractAsync();

            if (executionResult == ExitCodes.OK)
            {
                // get ESP32 partitions
                FlashPartitions = new Dictionary<int, string>()
                {
				    // bootloader goes to 0x1000
				    { 0x1000, Path.Combine(LocationPath, "bootloader.bin") },

				    // nanoCLR goes to 0x10000
				    { 0x10000, Path.Combine(LocationPath, "nanoCLR.bin") },

				    // partition table goes to 0x8000; there are partition tables for 2MB, 4MB, 8MB and 16MB flash sizes
				    { 0x8000, Path.Combine(LocationPath, $"partitions_{humanReadable.ToLowerInvariant()}.bin") }
                };
            }

            return executionResult;
        }
    }
}
