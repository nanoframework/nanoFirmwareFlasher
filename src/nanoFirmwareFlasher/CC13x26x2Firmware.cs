//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of STM32 firmware files from Bintray.
    /// </summary>
    internal class CC13x26x2Firmware : FirmwarePackage
    {
        public string nanoCLRFile { get; internal set; }

        public CC13x26x2Firmware(string targetName, string fwVersion, bool stable)
            :base(targetName, fwVersion, stable)
        {
        }

        internal new async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            // perform download and extract
            var executionResult = await base.DownloadAndExtractAsync();

            if (executionResult == ExitCodes.OK)
            {
                nanoCLRFile = Directory.EnumerateFiles(LocationPath, "nanoCLR.hex").FirstOrDefault();
            }

            return executionResult;
        }
    }
}
