// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
        /// External flash size in bytes.
        /// Defaults to a chip-appropriate value when it can't be read from INFO_UF2.TXT.
        /// </summary>
        public uint FlashSizeBytes { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="chipType">Chip type string.</param>
        /// <param name="boardId">Board identifier.</param>
        /// <param name="bootloaderVersion">Bootloader version.</param>
        /// <param name="drivePath">Path to UF2 drive.</param>
        /// <param name="driveLabel">Volume label.</param>
        /// <param name="flashSizeBytes">Detected external flash size in bytes, or <c>null</c> to use defaults.</param>
        public PicoDeviceInfo(
            string chipType,
            string boardId,
            string bootloaderVersion,
            string drivePath,
            string driveLabel,
            uint? flashSizeBytes = null)
        {
            ChipType = chipType;
            BoardId = boardId;
            BootloaderVersion = bootloaderVersion;
            DrivePath = drivePath;
            DriveLabel = driveLabel;

            // assign family ID based on chip type
            FamilyId = chipType switch
            {
                "RP2040" => PicoUf2Utility.FAMILY_ID_RP2040,
                "RP2350" => PicoUf2Utility.FAMILY_ID_RP2350_ARM,
                _ => throw new NotSupportedException($"Unknown Pico chip type '{chipType}'. Supported types: RP2040, RP2350."),
            };

            FlashSizeBytes = flashSizeBytes.GetValueOrDefault(GetDefaultFlashSize(chipType));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder deviceInfo = new();

            deviceInfo.AppendLine($"{ChipType}");
            deviceInfo.AppendLine($"Board: {BoardId}");
            deviceInfo.AppendLine($"Bootloader: {BootloaderVersion}");
            deviceInfo.AppendLine($"Drive: {DrivePath} ({DriveLabel})");
            deviceInfo.AppendLine($"Flash: {FormatFlashSize(FlashSizeBytes)} ({FlashSizeBytes:N0} bytes)");
            deviceInfo.Append($"UF2 Family ID: 0x{FamilyId:X8}");

            return deviceInfo.ToString();
        }

        private static uint GetDefaultFlashSize(string chipType)
        {
            return chipType switch
            {
                "RP2040" => PicoFirmware.DefaultFlashSize,
                "RP2350" => PicoFirmware.DefaultFlashSizeRp2350,
                _ => PicoFirmware.DefaultFlashSize,
            };
        }

        private static string FormatFlashSize(uint sizeBytes)
        {
            if (sizeBytes % (1024 * 1024) == 0)
            {
                return $"{sizeBytes / (1024 * 1024)}MB";
            }

            if (sizeBytes % 1024 == 0)
            {
                return $"{sizeBytes / 1024}KB";
            }

            return $"{sizeBytes}B";
        }
    }
}
