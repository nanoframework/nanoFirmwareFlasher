// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of J-Link firmware files from Cloudsmith.
    /// </summary>
    internal class JLinkFirmware : FirmwarePackage
    {
        public JLinkFirmware(
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
