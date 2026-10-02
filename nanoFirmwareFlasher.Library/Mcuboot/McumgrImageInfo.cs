// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Describes a single MCUboot image slot as reported by the SMP image-state command.
    /// </summary>
    public class McumgrImageInfo
    {
        /// <summary>Image index (multi-image devices; 0 for the primary MCU image).</summary>
        public int Image { get; set; }

        /// <summary>Slot number (0 = primary/running, 1 = secondary/upgrade).</summary>
        public int Slot { get; set; }

        /// <summary>Firmware version string.</summary>
        public string Version { get; set; }

        /// <summary>SHA-256 hash of the image.</summary>
        public byte[] Hash { get; set; }

        /// <summary>True if this image is currently executing.</summary>
        public bool Active { get; set; }

        /// <summary>True if this image has been permanently confirmed.</summary>
        public bool Confirmed { get; set; }

        /// <summary>True if this image is marked pending (will be swapped on next boot).</summary>
        public bool Pending { get; set; }

        /// <summary>True if the image is bootable.</summary>
        public bool Bootable { get; set; }

        /// <summary>True if the image will permanently stay in the primary slot after the next boot.</summary>
        public bool Permanent { get; set; }
    }
}
