//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of STM32 firmware files from Cloudsmith.
    /// </summary>
    internal class CC13x26x2Firmware : FirmwarePackage
    {
        public string nanoCLRFile { get; private set; }

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
