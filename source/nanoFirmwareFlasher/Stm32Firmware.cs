//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of STM32 firmware files from Bintray.
    /// </summary>
    internal class Stm32Firmware : FirmwarePackage
    {
        public bool HasDfuPackage => !string.IsNullOrEmpty(DfuPackage);

        public string nanoBooterFile { get; internal set; }

        public string nanoCLRFile { get; internal set; }

        public string DfuPackage { get; internal set; }

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
                var dfuFile = Directory.EnumerateFiles(LocationPath, "*.dfu");
                if (dfuFile.Count() > 0)
                {
                    DfuPackage = dfuFile.FirstOrDefault();
                }
                else
                {
                    nanoBooterFile = Directory.EnumerateFiles(LocationPath, "nanoBooter.hex").FirstOrDefault();
                    nanoCLRFile = Directory.EnumerateFiles(LocationPath, "nanoCLR.hex").FirstOrDefault();
                }
            }

            return ExitCodes.OK;
        }
    }
}
