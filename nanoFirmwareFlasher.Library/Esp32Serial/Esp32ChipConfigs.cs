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

        /// <summary>Address of the bootloader flash start. 0x1000 for ESP32, 0x0 for ESP32-S3/C3/C6/H2.</summary>
        internal int BootloaderAddress { get; }

        /// <summary>ROM flash write block size.</summary>
        internal int FlashWriteBlockSize { get; }

        /// <summary>Whether this chip supports the old-style SPI MOSI/MISO length (ESP32 only).</summary>
        internal bool UsesOldSpiRegisters { get; }

        internal Esp32ChipConfig(
            string name,
            string chipType,
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

        // ======================== ESP32-C6 ========================
        internal static Esp32ChipConfig ESP32_C6 { get; } = new(
            name: "ESP32-C6",
            chipType: "esp32c6",
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

        /// <summary>Secondary magic value for ESP32-C3 (ESP8685 variant).</summary>
        private const uint ESP32_C3_AltMagic = 0x1B31506F;

        /// <summary>
        /// All known chip configurations indexed by their primary magic value.
        /// </summary>
        private static readonly Dictionary<uint, Esp32ChipConfig> s_configsByMagic = new()
        {
            { ESP32.MagicValue, ESP32 },
            { ESP32_S2.MagicValue, ESP32_S2 },
            { ESP32_S3.MagicValue, ESP32_S3 },
            { ESP32_C3.MagicValue, ESP32_C3 },
            { ESP32_C3_AltMagic, ESP32_C3 },  // ESP8685 variant
            { ESP32_C6.MagicValue, ESP32_C6 },
            { ESP32_H2.MagicValue, ESP32_H2 },
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
            { "esp32c6", ESP32_C6 },
            { "esp32h2", ESP32_H2 },
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
                yield return ESP32_C6;
                yield return ESP32_H2;
            }
        }
    }
}
