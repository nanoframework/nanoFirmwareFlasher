using nanoFramework.Tools.Debugger;
using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    //public class FirmwarePackage<T> : FirmwarePackageBase, IDisposable where T : new()
    //{
    //    public Stm32Firmware DeviceFirmware { get; }

    //    public FirmwarePackage(NanoDeviceBase nanoDevice) : base(nanoDevice)
    //    {
    //        if (nanoDevice is null)
    //        {
    //            throw new ArgumentNullException(nameof(nanoDevice));
    //        }

    //        if (nanoDevice.Platform.StartsWith("STM32"))
    //        {
    //            DeviceFirmware = new Stm32Firmware(
    //                nanoDevice.TargetName,
    //                "",
    //                false);
    //        }
    //        else if (nanoDevice.Platform.StartsWith("STM32"))
    //        {
    //            DeviceFirmware = new JLinkFirmware(
    //                nanoDevice.TargetName,
    //                "",
    //                false);

    //        }
    //    }

    //    public FirmwarePackage(string targetName, string fwVersion, bool preview) : base(targetName, fwVersion, preview)
    //    {
    //    }

    //}

    /// <summary>
    /// Firmware Package Factory.
    /// </summary>
    public class FirmwarePackageFactory
    {
        /// <summary>
        /// Gets the firmware package for the device.
        /// </summary>
        /// <param name="nanoDevice">The device.</param>
        /// <returns>The Firmware package.</returns>
        /// <exception cref="ArgumentNullException">The argument was null.</exception>
        /// <exception cref="NotSupportedException">The command is not supported.</exception>
        public static FirmwarePackage GetFirmwarePackage(NanoDeviceBase nanoDevice)
        {
            if (nanoDevice is null)
            {
                throw new ArgumentNullException(nameof(nanoDevice));
            }

            if (nanoDevice.Platform.StartsWith("STM32"))
            {
                return new Stm32Firmware(
                    nanoDevice.TargetName,
                    "",
                    false);
            }
            else if (nanoDevice.Platform.StartsWith("GGECKO_S1"))
            {
                return new JLinkFirmware(
                    nanoDevice.TargetName,
                    "",
                    false);
            }
            else
            {
                throw new NotSupportedException($"FirmwarePackageFactory doesn't support generating packages for {nanoDevice.Platform} platform");
            }
        }
    }
}
