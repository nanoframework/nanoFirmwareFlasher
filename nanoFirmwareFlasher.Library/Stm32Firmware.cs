//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
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
        [Obsolete("This property is discontinued and it will be removed in a future version.")]
        public bool HasDfuPackage => !string.IsNullOrEmpty(DfuPackage);

        public string DfuPackage { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Stm32Firmware"/> class.
        /// </summary>
        /// <param name="targetName">Name of the target for this <see cref="Stm32Firmware"/> object.</param>
        /// <param name="fwVersion">Firmware version that will be used for this <see cref="Stm32Firmware"/> object.</param>
        /// <param name="preview"><see langword="true"/> to use preview version. <see langword="false"/> to use stable version.</param>
        public Stm32Firmware(
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
        /// Initializes a new instance of the <see cref="Stm32Firmware"/> class.
        /// </summary>
        /// <param name="nanoDevice"><see cref="NanoDeviceBase"/> that will provide details when instantiating the <see cref="Stm32Firmware"/> object.</param>
        public Stm32Firmware(NanoDeviceBase nanoDevice) : base(nanoDevice)
        {

        }

        internal new System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            // perform download and extract
           return base.DownloadAndExtractAsync();
        }
    }
}
