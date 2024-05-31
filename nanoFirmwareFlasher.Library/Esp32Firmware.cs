//
// Copyright (c) .NET Foundation and Contributors
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
    /// Class that handles the download of ESP32 firmware files from Cloudsmith.
    /// </summary>
    internal class Esp32Firmware : FirmwarePackage
    {
        /// <summary>
        /// Address of the CLR partition.
        /// </summary>
        public const int CLRAddress = 0x10000;

        /// <summary>
        /// ESP32 nanoCLR is available for 2MB, 4MB, 8MB and 16MB flash sizes
        /// </summary>
        private List<int> SupportedFlashSizes => new() { 0x200000, 0x400000, 0x800000, 0x1000000 };

        internal string BootloaderPath;

        internal Dictionary<int, string> FlashPartitions;

        internal PartitionTableSize? _partitionTableSize;

        /// <summary>
        /// Default address of the deployment partition.
        /// </summary>
        internal int DeploymentPartitionAddress => 0x1B0000;

        public Esp32Firmware(
            string targetName,
            string fwVersion,
            bool stable,
            PartitionTableSize? partitionTableSize)
            : base(
                 targetName,
                 fwVersion,
                 stable)
        {
            _partitionTableSize = partitionTableSize;
        }

        internal async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync(Esp32DeviceInfo deviceInfo)
        {
            int flashSize = deviceInfo.FlashSize;

            if (_partitionTableSize is not null)
            {
                // if specified, partition table size overrides flash size.
                flashSize = (int)_partitionTableSize * 0x100000;
            }

            // check if the option to override the partition table was set

            if (!SupportedFlashSizes.Contains(flashSize))
            {
                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    Console.WriteLine($"There is no firmware available for ESP32 with {Esp32DeviceInfo.GetFlashSizeAsString(flashSize)} flash size!{Environment.NewLine}Only the following flash sizes are supported: {string.Join(", ", SupportedFlashSizes.Select(size => size >= 0x10000 ? $"{size / 0x100000}MB" : $"{size / 0x400}kB."))}");
                }

                return ExitCodes.E4001;
            }

            // perform download and extract
            var executionResult = await DownloadAndExtractAsync();

            if (executionResult == ExitCodes.OK)
            {
                BootloaderPath = "bootloader.bin";

                // get ESP32 partitions
                FlashPartitions = new Dictionary<int, string>
                {
				    // bootloader goes to 0x1000, except for ESP32_C3/C6/H2/S3, which goes to 0x0
				    {
                        deviceInfo.ChipType == "ESP32-C3"
                        || deviceInfo.ChipType == "ESP32-C6"
                        || deviceInfo.ChipType == "ESP32-H2"
                        || deviceInfo.ChipType == "ESP32-S3" ? 0x0 : 0x1000, Path.Combine(LocationPath, BootloaderPath) },

				    // nanoCLR goes to 0x10000
				    { CLRAddress, Path.Combine(LocationPath, "nanoCLR.bin") },

				    // partition table goes to 0x8000; there are partition tables for 4MB, 8MB and 16MB flash sizes (and 2MB for ESP32)
				    { 0x8000, Path.Combine(LocationPath, $"partitions_{Esp32DeviceInfo.GetFlashSizeAsString(flashSize).ToLowerInvariant()}.bin") }
                };
            }

            return executionResult;
        }
    }
}
