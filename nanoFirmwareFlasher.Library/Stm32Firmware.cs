// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of STM32 firmware files from Cloudsmith.
    /// </summary>
    internal class Stm32Firmware : FirmwarePackage
    {
        private readonly string _wifiTargetName;
        [Obsolete("This property is discontinued and it will be removed in a future version.")]
        public bool HasDfuPackage => !string.IsNullOrEmpty(DfuPackage);

        public string DfuPackage { get; private set; }

        internal string WifiFirmwareFile { get; private set; }

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
            _wifiTargetName = targetName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Stm32Firmware"/> class.
        /// </summary>
        /// <param name="nanoDevice"><see cref="NanoDeviceBase"/> that will provide details when instantiating the <see cref="Stm32Firmware"/> object.</param>
        public Stm32Firmware(NanoDeviceBase nanoDevice) : base(nanoDevice)
        {
            _wifiTargetName = nanoDevice.TargetName;
        }

        internal override async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync(string archiveDirectoryPath)
        {
            WifiFirmwareFile = null;
            ExitCodes result = await base.DownloadAndExtractAsync(archiveDirectoryPath);
            if (result == ExitCodes.OK)
            {
                WifiFirmwareFile = FindWifiFirmware(_wifiTargetName, LocationPath);
            }

            return result;
        }

        internal static string FindWifiFirmware(string targetName, string directory)
        {
            string path = Path.Combine(directory, "wifi_firmware.bin");
            return targetName == Stm32WifiImage.TargetName && File.Exists(path) ? path : null;
        }
    }
}
