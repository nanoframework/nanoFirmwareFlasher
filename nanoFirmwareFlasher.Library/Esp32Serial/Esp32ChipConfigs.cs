// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Chip-specific configuration for ESP32 family variants.
    /// Contains register addresses and parameters needed for bootloader communication.
    /// </summary>
    internal class Esp32ChipConfig
    {
        /// <summary>Display name (e.g. "ESP32", "ESP32-S3").</summary>
        internal string Name { get; }

        /// <summary>Internal chip type string matching existing convention (e.g. "esp32", "esp32s3").</summary>
        internal string ChipType { get; }

        /// <summary>Numeric chip ID used with ESP32_S3 and later.</summary>
        internal uint ChipId { get; }

        /// <summary>Use magic value for detection. Esp32_S3 and later don't require it, but older chips do.</summary>
        internal bool UseMagicValue { get; }

        /// <summary>Chip detection magic value from register at <see cref="MagicRegAddr"/>.</summary>
        internal uint MagicValue { get; }

        /// <summary>Address of the magic value register (typically 0x40001000).</summary>
        internal uint MagicRegAddr { get; }

        /// <summary>eFuse register base address for MAC word 0.</summary>
        internal uint EfuseMacWord0Addr { get; }

        /// <summary>eFuse register address for MAC word 1.</summary>
        internal uint EfuseMacWord1Addr { get; }

        /// <summary>SPI controller base register address.</summary>
        internal uint SpiRegBase { get; }

        /// <summary>Offset of the SPI USR register from <see cref="SpiRegBase"/>.</summary>
        internal uint SpiUsrOffset { get; }

        /// <summary>Offset of the SPI W0 (data) register from <see cref="SpiRegBase"/>.</summary>
        internal uint SpiW0Offset { get; }

        /// <summary>Offset of the SPI USR1 register from <see cref="SpiRegBase"/>.</summary>
        internal uint SpiUsr1Offset { get; }

        /// <summary>Offset of the SPI USR2 register from <see cref="SpiRegBase"/>.</summary>
        internal uint SpiUsr2Offset { get; }

        /// <summary>Offset of the SPI MOSI DLEN register (for non-ESP32 chips).</summary>
        internal uint SpiMosiDlenOffset { get; }

        /// <summary>Offset of the SPI MISO DLEN register (for non-ESP32 chips).</summary>
        internal uint SpiMisoDlenOffset { get; }

        /// <summary>Base address of the eFuse read registers.</summary>
        internal uint EfuseBaseAddr { get; }

        /// <summary>Crystal clock divider (1 for ESP32, 2 for ESP8266).</summary>
        internal int XtalClkDivider { get; }

        /// <summary>Address of the boot loader flash start. 0x1000 for ESP32, 0x0 for ESP32-S3/C3/C6/H2.</summary>
        internal int BootloaderAddress { get; }

        /// <summary>ROM flash write block size.</summary>
        internal int FlashWriteBlockSize { get; }

        /// <summary>Whether this chip supports the old-style SPI MOSI/MISO length (ESP32 only).</summary>
        internal bool UsesOldSpiRegisters { get; }

        /// <summary>Stub variant for the chips when loading a specific stub based on chip rev version. </summary>
        public string StubVariant { get; set; } = null;

        /// <summary>Whether the flasher stub must be skipped for this detected runtime variant.</summary>
        internal bool DisableStub { get; set; }

        /// <summary>Whether the current runtime path is using native USB-OTG for this chip.</summary>
        internal bool UsesUsbOtg { get; set; }

        internal Esp32ChipConfig(
            string name,
            string chipType,
            uint chipId,
            bool useMagicValue,
            uint magicValue,
            uint magicRegAddr,
            uint efuseMacWord0Addr,
            uint efuseMacWord1Addr,
            uint spiRegBase,
            uint spiUsrOffset,
            uint spiW0Offset,
            uint spiUsr1Offset,
            uint spiUsr2Offset,
            uint spiMosiDlenOffset,
            uint spiMisoDlenOffset,
            uint efuseBaseAddr,
            int xtalClkDivider,
            int bootloaderAddress,
            int flashWriteBlockSize,
            bool usesOldSpiRegisters)
        {
            Name = name;
            ChipType = chipType;
            ChipId = chipId;
            UseMagicValue = useMagicValue;
            MagicValue = magicValue;
            MagicRegAddr = magicRegAddr;
            EfuseMacWord0Addr = efuseMacWord0Addr;
            EfuseMacWord1Addr = efuseMacWord1Addr;
            SpiRegBase = spiRegBase;
            SpiUsrOffset = spiUsrOffset;
            SpiW0Offset = spiW0Offset;
            SpiUsr1Offset = spiUsr1Offset;
            SpiUsr2Offset = spiUsr2Offset;
            SpiMosiDlenOffset = spiMosiDlenOffset;
            SpiMisoDlenOffset = spiMisoDlenOffset;
            EfuseBaseAddr = efuseBaseAddr;
            XtalClkDivider = xtalClkDivider;
            BootloaderAddress = bootloaderAddress;
            FlashWriteBlockSize = flashWriteBlockSize;
            UsesOldSpiRegisters = usesOldSpiRegisters;
        }

        /// <summary>Full address of the SPI USR register.</summary>
        internal uint SpiUsrAddr => SpiRegBase + SpiUsrOffset;

        /// <summary>Full address of the SPI W0 register.</summary>
        internal uint SpiW0Addr => SpiRegBase + SpiW0Offset;

        /// <summary>Full address of the SPI USR1 register.</summary>
        internal uint SpiUsr1Addr => SpiRegBase + SpiUsr1Offset;

        /// <summary>Full address of the SPI USR2 register.</summary>
        internal uint SpiUsr2Addr => SpiRegBase + SpiUsr2Offset;

        /// <summary>Full address of the SPI MOSI DLEN register.</summary>
        internal uint SpiMosiDlenAddr => SpiRegBase + SpiMosiDlenOffset;

        /// <summary>Full address of the SPI MISO DLEN register.</summary>
        internal uint SpiMisoDlenAddr => SpiRegBase + SpiMisoDlenOffset;

        /// <summary>Full address of the SPI CMD register (base + 0x00).</summary>
        internal uint SpiCmdAddr => SpiRegBase;
    }

    /// <summary>
    /// Static registry of chip-specific configurations for all supported ESP32 variants.
    /// Register addresses are from Espressif Technical Reference Manuals.
    /// </summary>
    internal static class Esp32ChipConfigs
    {
        // ======================== ESP32 ========================
        internal static Esp32ChipConfig ESP32 { get; } = new(
            name: "ESP32",
            chipType: "esp32",
            chipId: 0x00000000, // Not used for ESP32
            useMagicValue: true,
            magicValue: 0x00F01D83,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x3FF5A004,
            efuseMacWord1Addr: 0x3FF5A008,
            spiRegBase: 0x3FF42000,
            spiUsrOffset: 0x1C,
            spiW0Offset: 0x80,
            spiUsr1Offset: 0x20,
            spiUsr2Offset: 0x24,
            spiMosiDlenOffset: 0x28,
            spiMisoDlenOffset: 0x2C,
            efuseBaseAddr: 0x3FF5A000,
            xtalClkDivider: 1,
            bootloaderAddress: 0x1000,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-S2 ========================
        internal static Esp32ChipConfig ESP32_S2 { get; } = new(
            name: "ESP32-S2",
            chipType: "esp32s2",
            chipId: 0x00000000, // Not used for ESP32-S2
            useMagicValue: true,
            magicValue: 0x000007C6,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x60007044,
            efuseMacWord1Addr: 0x60007048,
            spiRegBase: 0x3F402000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x3F41A000,
            xtalClkDivider: 1,
            bootloaderAddress: 0x1000,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-S3 ========================
        internal static Esp32ChipConfig ESP32_S3 { get; } = new(
            name: "ESP32-S3",
            chipType: "esp32s3",
            chipId: 9,
            useMagicValue: false,
            magicValue: 0x00000009,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x60007044,
            efuseMacWord1Addr: 0x60007048,
            spiRegBase: 0x60002000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x60007000,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-C3 ========================
        internal static Esp32ChipConfig ESP32_C3 { get; } = new(
            name: "ESP32-C3",
            chipType: "esp32c3",
            chipId: 5,
            useMagicValue: false,
            magicValue: 0x6921506F,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x60008844,
            efuseMacWord1Addr: 0x60008848,
            spiRegBase: 0x60002000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x60008800,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-C2 ========================
        internal static Esp32ChipConfig ESP32_C2 { get; } = new(
            name: "ESP32-C2",
            chipType: "esp32c2",
            chipId: 12,
            useMagicValue: false,
            magicValue: 0,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x60008840,
            efuseMacWord1Addr: 0x60008844,
            spiRegBase: 0x60002000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x60008800,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-C6 ========================
        internal static Esp32ChipConfig ESP32_C6 { get; } = new(
            name: "ESP32-C6",
            chipType: "esp32c6",
            chipId: 13,
            useMagicValue: false,
            magicValue: 0x2CE0806F,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x600B0844,
            efuseMacWord1Addr: 0x600B0848,
            spiRegBase: 0x60003000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x600B0800,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-H2 ========================
        internal static Esp32ChipConfig ESP32_H2 { get; } = new(
            name: "ESP32-H2",
            chipType: "esp32h2",
            chipId: 16,
            useMagicValue: false,
            magicValue: 0xD7B73E80,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x600B0844,
            efuseMacWord1Addr: 0x600B0848,
            spiRegBase: 0x60003000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x600B0800,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-H21 ========================
        internal static Esp32ChipConfig ESP32_H21 { get; } = new(
            name: "ESP32-H21",
            chipType: "esp32h21",
            chipId: 25,
            useMagicValue: false,
            magicValue: 0,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x600B4044,
            efuseMacWord1Addr: 0x600B4048,
            spiRegBase: 0x60003000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x600B4000,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-H4 ========================
        internal static Esp32ChipConfig ESP32_H4 { get; } = new(
            name: "ESP32-H4",
            chipType: "esp32h4",
            chipId: 28,
            useMagicValue: false,
            magicValue: 0,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x600B1844,
            efuseMacWord1Addr: 0x600B1848,
            spiRegBase: 0x60099000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x600B1800,
            xtalClkDivider: 1,
            bootloaderAddress: 0x2000,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-C5 ========================
        internal static Esp32ChipConfig ESP32_C5 { get; } = new(
            name: "ESP32-C5",
            chipType: "esp32c5",
            chipId: 23,
            useMagicValue: false,
            magicValue: 0x5FD1406F, 
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x600B4844, 
            efuseMacWord1Addr: 0x600B4848, 
            spiRegBase: 0x60003000, 
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x600B4800, 
            xtalClkDivider: 1,
            bootloaderAddress: 0x2000,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-C61 ========================
        internal static Esp32ChipConfig ESP32_C61 { get; } = new(
            name: "ESP32-C61",
            chipType: "esp32c61",
            chipId: 20,
            useMagicValue: false,
            magicValue: 0x6F51316F,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x600B4844,
            efuseMacWord1Addr: 0x600B4848,
            spiRegBase: 0x60003000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x600B4800,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-E22 ========================
        internal static Esp32ChipConfig ESP32_E22 { get; } = new(
            name: "ESP32-E22",
            chipType: "esp32e22",
            chipId: 31,
            useMagicValue: false,
            magicValue: 0,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0xC4008044,
            efuseMacWord1Addr: 0xC4008048,
            spiRegBase: 0xC3003000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0xC4008000,
            xtalClkDivider: 1,
            bootloaderAddress: 0x0,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-P4 ========================
        internal static Esp32ChipConfig ESP32_P4 { get; } = new(
            name: "ESP32-P4",
            chipType: "esp32p4",
            chipId: 18,
            useMagicValue: false,
            magicValue: 0,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x5012D044, 
            efuseMacWord1Addr: 0x5012D048, 
            spiRegBase: 0x5008D000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x5012D000,
            xtalClkDivider: 1,
            bootloaderAddress: 0x2000,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        // ======================== ESP32-S31 ========================
        internal static Esp32ChipConfig ESP32_S31 { get; } = new(
            name: "ESP32-S31",
            chipType: "esp32s31",
            chipId: 32,
            useMagicValue: false,
            magicValue: 0,
            magicRegAddr: 0x40001000,
            efuseMacWord0Addr: 0x20715050,
            efuseMacWord1Addr: 0x20715054,
            spiRegBase: 0x20501000,
            spiUsrOffset: 0x18,
            spiW0Offset: 0x58,
            spiUsr1Offset: 0x1C,
            spiUsr2Offset: 0x20,
            spiMosiDlenOffset: 0x24,
            spiMisoDlenOffset: 0x28,
            efuseBaseAddr: 0x20715000,
            xtalClkDivider: 1,
            bootloaderAddress: 0x2000,
            flashWriteBlockSize: 0x4000,
            usesOldSpiRegisters: false
        );

        /// <summary>
        /// All known chip configurations indexed by their primary magic value.
        /// </summary>
        private static readonly Dictionary<uint, Esp32ChipConfig> s_configsByMagic = new()
        {
            { ESP32.MagicValue, ESP32 },
            { ESP32_S2.MagicValue, ESP32_S2 }
        };

        /// <summary>
        /// All configurations indexed by chip type string.
        /// </summary>
        private static readonly Dictionary<string, Esp32ChipConfig> s_configsByType = new(StringComparer.OrdinalIgnoreCase)
        {
            { "esp32", ESP32 },
            { "esp32s2", ESP32_S2 },
            { "esp32s3", ESP32_S3 },
            { "esp32c3", ESP32_C3 },
            { "esp32c2", ESP32_C2 },
            { "esp32c6", ESP32_C6 },
            { "esp32h2", ESP32_H2 },
            { "esp32h21", ESP32_H21 },
            { "esp32h4", ESP32_H4 },
            { "esp32c5", ESP32_C5 },
            { "esp32c61", ESP32_C61 },
            { "esp32e22", ESP32_E22 },
            { "esp32p4", ESP32_P4 },
            { "esp32s31", ESP32_S31 },
        };

        /// <summary>
        /// Get chip configuration by magic value read from the chip.
        /// </summary>
        /// <param name="magic">Magic value from the chip.</param>
        /// <returns>Chip configuration, or null if not recognized.</returns>
        internal static Esp32ChipConfig GetByMagicValue(uint magic)
        {
            return s_configsByMagic.TryGetValue(magic, out var config) ? config : null;
        }

        /// <summary>
        /// Get chip configuration by chip type string.
        /// </summary>
        /// <param name="chipType">Chip type string (e.g. "esp32", "esp32s3").</param>
        /// <returns>Chip configuration, or null if not recognized.</returns>
        internal static Esp32ChipConfig GetByType(string chipType)
        {
            return s_configsByType.TryGetValue(chipType, out var config) ? config : null;
        }

        /// <summary>
        /// Get chip configuration by numeric chip ID (for ESP32-S3 and later).
        /// </summary>
        /// <param name="chipId">Numeric chip ID.</param>
        /// <returns>Chip configuration, or null if not recognized.</returns>
        internal static Esp32ChipConfig GetByChipId(uint? chipId)
        {
            if (chipId == null)
            {
                return null;
            }

            foreach (var config in All)
            {
                if (config.ChipId == chipId)
                {
                    return config;
                }
            }
            return null;
        }

        /// <summary>
        /// Get all known chip configurations.
        /// </summary>
        internal static IEnumerable<Esp32ChipConfig> All
        {
            get
            {
                yield return ESP32;
                yield return ESP32_S2;
                yield return ESP32_S3;
                yield return ESP32_C3;
                yield return ESP32_C2;
                yield return ESP32_C6;
                yield return ESP32_H2;
                yield return ESP32_H21;
                yield return ESP32_H4;
                yield return ESP32_C5;
                yield return ESP32_C61;
                yield return ESP32_E22;
                yield return ESP32_P4;
                yield return ESP32_S31;
            }
        }
    }
}
