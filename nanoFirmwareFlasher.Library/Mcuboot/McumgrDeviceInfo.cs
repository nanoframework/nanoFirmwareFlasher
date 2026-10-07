// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// nanoFramework device information reported by the SMP custom group device-info command.
    /// </summary>
    public record McumgrDeviceInfo
    {
        /// <summary>Target name (e.g. "ESP32_WROVER_KIT").</summary>
        public string? TargetName { get; set; }

        /// <summary>Running nanoCLR version string.</summary>
        public string? ClrVersion { get; set; }

        /// <summary>OEM information string, if available.</summary>
        public string? OemInfo { get; set; }

        /// <summary>True if the device is running under MCUboot.</summary>
        public bool HasMcuboot { get; set; }

        /// <summary>True if a deployment partition is available.</summary>
        public bool DeploymentAvailable { get; set; }

        /// <summary>Upstream MCUboot version (e.g. "2.5.0-rc1"), reported by the MCUboot serial recovery.</summary>
        public string? McubootVersion { get; set; }

        /// <summary>nanoMCUboot (nanoFramework build of the MCUboot bootloader) version, reported by the MCUboot serial recovery.</summary>
        public string? NanoMcubootVersion { get; set; }
    }
}
