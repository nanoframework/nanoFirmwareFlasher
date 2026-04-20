// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of Raspberry Pi Pico firmware files from Cloudsmith.
    /// </summary>
    internal class PicoFirmware : FirmwarePackage
    {
        /// <summary>
        /// Default base flash address for RP2040/RP2350 XIP.
        /// </summary>
        internal const uint DefaultBaseAddress = 0x10000000;

        /// <summary>
        /// Default flash size for RP2040 Pico (2 MB).
        /// </summary>
        internal const uint DefaultFlashSize = 2 * 1024 * 1024;

        /// <summary>
        /// Default deployment region address for managed application.
        /// This must match the deployment region start defined in the nanoFramework target's flash map.
        /// </summary>
        internal const uint DefaultDeploymentAddress = 0x10080000;

        /// <summary>
        /// Path to the nanoCLR binary file for this firmware package.
        /// </summary>
        internal string BinFilePath { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PicoFirmware"/> class.
        /// </summary>
        /// <param name="targetName">Name of the target for this <see cref="PicoFirmware"/> object.</param>
        /// <param name="fwVersion">Firmware version that will be used for this <see cref="PicoFirmware"/> object.</param>
        /// <param name="preview"><see langword="true"/> to use preview version. <see langword="false"/> to use stable version.</param>
        public PicoFirmware(
            string targetName,
            string fwVersion,
            bool preview)
            : base(
                 targetName,
                 fwVersion,
                 preview)
        {
        }

        /// <summary>
        /// Downloads and extracts the firmware package, then locates the nanoCLR binary.
        /// </summary>
        /// <param name="archiveDirectoryPath">Path for the archive cache directory.</param>
        /// <returns>Exit code indicating result.</returns>
        internal override async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync(string archiveDirectoryPath)
        {
            // perform download and extract
            ExitCodes result = await base.DownloadAndExtractAsync(archiveDirectoryPath);

            if (result == ExitCodes.OK)
            {
                // find nanoCLR.bin in the extracted content
                string binFile = Directory.GetFiles(LocationPath, "nanoCLR.bin", SearchOption.AllDirectories).FirstOrDefault();

                if (binFile == null)
                {
                    return ExitCodes.E3003;
                }

                BinFilePath = binFile;
            }

            return result;
        }

        /// <summary>
        /// Returns the firmware binary content converted to UF2 format.
        /// </summary>
        /// <param name="familyId">UF2 family ID for the target chip.</param>
        /// <returns>UF2 formatted byte array.</returns>
        internal byte[] GetUf2Bytes(uint familyId)
        {
            if (string.IsNullOrEmpty(BinFilePath))
            {
                throw new InvalidOperationException("Binary file path not set. Call DownloadAndExtractAsync first.");
            }

            byte[] binData = File.ReadAllBytes(BinFilePath);
            return PicoUf2Utility.ConvertBinToUf2(binData, DefaultBaseAddress, familyId);
        }
    }
}
