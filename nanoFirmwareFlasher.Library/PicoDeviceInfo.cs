// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Information about a connected Raspberry Pi Pico device in BOOTSEL mode.
    /// </summary>
    public class PicoDeviceInfo
    {
        /// <summary>
        /// Chip type: "RP2040" or "RP2350".
        /// </summary>
        public string ChipType { get; }

        /// <summary>
        /// Board identifier from INFO_UF2.TXT.
        /// </summary>
        public string BoardId { get; }

        /// <summary>
        /// UF2 bootloader version.
        /// </summary>
        public string BootloaderVersion { get; }

        /// <summary>
        /// Path to the mounted UF2 volume.
        /// </summary>
        public string DrivePath { get; }

        /// <summary>
        /// Volume label of the UF2 drive (e.g. "RPI-RP2", "RP2350").
        /// </summary>
        public string DriveLabel { get; }

        /// <summary>
        /// UF2 family ID for this chip.
        /// RP2040: 0xE48BFF56, RP2350 ARM: 0xE48BFF59.
        /// </summary>
        public uint FamilyId { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="chipType">Chip type string.</param>
        /// <param name="boardId">Board identifier.</param>
        /// <param name="bootloaderVersion">Bootloader version.</param>
        /// <param name="drivePath">Path to UF2 drive.</param>
        /// <param name="driveLabel">Volume label.</param>
        public PicoDeviceInfo(
            string chipType,
            string boardId,
            string bootloaderVersion,
            string drivePath,
            string driveLabel)
        {
            ChipType = chipType;
            BoardId = boardId;
            BootloaderVersion = bootloaderVersion;
            DrivePath = drivePath;
            DriveLabel = driveLabel;

            // assign family ID based on chip type
            FamilyId = chipType switch
            {
                "RP2350" => 0xE48BFF59,
                _ => 0xE48BFF56, // RP2040
            };
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder deviceInfo = new();

            deviceInfo.AppendLine($"{ChipType}");
            deviceInfo.AppendLine($"Board: {BoardId}");
            deviceInfo.AppendLine($"Bootloader: {BootloaderVersion}");
            deviceInfo.AppendLine($"Drive: {DrivePath} ({DriveLabel})");
            deviceInfo.Append($"UF2 Family ID: 0x{FamilyId:X8}");

            return deviceInfo.ToString();
        }
    }
}
