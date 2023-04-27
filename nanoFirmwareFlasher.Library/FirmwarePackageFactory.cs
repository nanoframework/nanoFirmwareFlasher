//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    public class FirmwarePackageFactory
    {
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
