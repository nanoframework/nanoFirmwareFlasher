//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of STM32 firmware files from Cloudsmith.
    /// </summary>
    internal class Stm32Firmware : FirmwarePackage
    {
        public bool HasDfuPackage => !string.IsNullOrEmpty(DfuPackage);

        public string NanoBooterFile { get; private set; }

        public string NanoClrFile { get; private set; }

        public string DfuPackage { get; private set; }

        public Stm32Firmware(string targetName, string fwVersion, bool stable)
            :base(targetName, fwVersion, stable)
        {
        }

        internal new async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            // perform download and extract
            var executionResult = await base.DownloadAndExtractAsync();

            if (executionResult == ExitCodes.OK)
            {
                var dfuFile = Directory.EnumerateFiles(LocationPath, "*.dfu").ToArray();
                if (dfuFile.Any())
                {
                    DfuPackage = dfuFile.First();
                }
                NanoBooterFile = Directory.EnumerateFiles(LocationPath, "nanoBooter.hex").FirstOrDefault();
                NanoClrFile = Directory.EnumerateFiles(LocationPath, "nanoCLR.hex").FirstOrDefault();
            }

            return executionResult;
        }
    }
}
