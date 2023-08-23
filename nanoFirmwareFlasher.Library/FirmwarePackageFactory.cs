//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Firmware Package Factory.
    /// </summary>
    public class FirmwarePackageFactory
    {
        /// <summary>
        /// Gets the firmware package for the device.
        /// </summary>
        /// <param name="nanoDevice">The device.</param>
        /// <param name="fwVersion">The firmware Version.</param>
        /// <returns>The Firmware package.</returns>
        /// <exception cref="NotSupportedException">The command is not supported.</exception>
        public static FirmwarePackage GetFirmwarePackage(
            NanoDeviceBase nanoDevice,
            string fwVersion)
        {
            if (nanoDevice is null)
            {
                throw new ArgumentNullException(nameof(nanoDevice));
            }

            if (nanoDevice.Platform.StartsWith("STM32"))
            {
                return new Stm32Firmware(
                    nanoDevice.TargetName,
                    fwVersion,
                    false);
            }
            else if (nanoDevice.Platform.StartsWith("GGECKO_S1"))
            {
                return new JLinkFirmware(
                    nanoDevice.TargetName,
                    fwVersion,
                    false);
            }
            else
            {
                throw new NotSupportedException($"FirmwarePackageFactory doesn't support generating packages for {nanoDevice.Platform} platform");
            }
        }
    }
}
