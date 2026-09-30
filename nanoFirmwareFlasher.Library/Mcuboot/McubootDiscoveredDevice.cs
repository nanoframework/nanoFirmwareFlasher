// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// A device found answering SMP on a serial port, typically MCUboot in serial recovery mode.
    /// </summary>
    public class McubootDiscoveredDevice
    {
        /// <summary>Serial port where the device answered (e.g. "COM7", "/dev/ttyACM0").</summary>
        public string PortName { get; set; }

        /// <summary>MCUmgr transport parameters reported by the device (the discovery probe reply).</summary>
        public McumgrParameters Parameters { get; set; }

        /// <summary>
        /// Image slots reported by the device, or <see langword="null"/> when the image list was not
        /// requested or could not be read (see <see cref="ImageListError"/>).
        /// </summary>
        public List<McumgrImageInfo> Images { get; set; }

        /// <summary>Reason the image list could not be read, if it was requested and failed.</summary>
        public string ImageListError { get; set; }
    }
}
