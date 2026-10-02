// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Information extracted from a MCUboot image header.
    /// </summary>
    public class McubootImageInfo
    {
        /// <summary>Magic number at offset 0 of a valid MCUboot image (0x96f3b83d).</summary>
        public uint HeaderMagic { get; set; }

        /// <summary>Firmware version string (major.minor.revision+build).</summary>
        public string Version { get; set; }

        /// <summary>Image binary size in bytes (excludes header).</summary>
        public uint ImageSize { get; set; }

        /// <summary>Header size in bytes.</summary>
        public uint HeaderSize { get; set; }

        /// <summary>SHA-256 hash of the image, or null if not present.</summary>
        public byte[] Hash { get; set; }

        /// <summary>True if the header magic and basic constraints are valid.</summary>
        public bool IsValid { get; set; }
    }
}
