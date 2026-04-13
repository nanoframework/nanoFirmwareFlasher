// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of STM32 firmware files from Cloudsmith.
    /// </summary>
    internal class CC13x26x2Firmware : FirmwarePackage
    {
        public CC13x26x2Firmware(
            string targetName,
            string fwVersion,
            bool preview)
            : base(
                 targetName,
                 fwVersion,
                 preview)
        {
        }

        internal new System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync(string archiveDirectoryPath)
        {
            // perform download and extract
            return base.DownloadAndExtractAsync(archiveDirectoryPath);
        }
    }
}
