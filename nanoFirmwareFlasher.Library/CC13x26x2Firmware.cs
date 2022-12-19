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

        internal new System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            // perform download and extract
           return base.DownloadAndExtractAsync();
        }
    }
}
