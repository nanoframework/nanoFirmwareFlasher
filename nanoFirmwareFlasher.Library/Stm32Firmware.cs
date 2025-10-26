// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nanoFramework.Tools.Debugger;

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

        internal new System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync(string archiveDirectoryPath)
        {
            // perform download and extract
            return base.DownloadAndExtractAsync(archiveDirectoryPath);
        }
    }
}
