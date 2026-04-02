// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Detects chip type, revision, features, MAC address, and flash info
    /// by reading hardware registers and eFuse blocks via the bootloader protocol.
    /// Replaces the text-parsing of "esptool flash_id" output.
    /// </summary>
    internal class Esp32ChipDetector
    {
        private readonly Esp32BootloaderClient _client;
        private Esp32ChipConfig _config;

        internal Esp32ChipDetector(Esp32BootloaderClient client)
        {
            _client = client;
        }

        /// <summary>
        /// The detected chip configuration. Available after <see cref="DetectChipType"/>.
        /// </summary>
        internal Esp32ChipConfig Config => _config;

        /// <summary>
        /// Detect the chip family by reading the magic value register.
        /// Must be called first before other detection methods.
        /// </summary>
        /// <returns>Chip type string (e.g. "esp32", "esp32s3").</returns>
        /// <exception cref="EspToolExecutionException">Chip type not recognized.</exception>
        internal string DetectChipType()
        {
            // Try reading magic from the standard address first (0x40001000)
            uint magic = _client.ReadRegister(0x40001000);

            _config = Esp32ChipConfigs.GetByMagicValue(magic);

            if (_config == null)
            {
                throw new EspToolExecutionException(
                    $"Unknown ESP32 chip magic value: 0x{magic:X8}. Device may not be supported.");
            }

            return _config.ChipType;
        }

        /// <summary>
        /// Read the nth eFuse word (EFUSE_RD_REG_BASE + 4*n).
        /// Matches esptool.py's read_efuse(n). Only valid for original ESP32.
        /// </summary>
        private uint ReadEfuse(int n)
        {
            return _client.ReadRegister(_config.EfuseBaseAddr + (uint)(4 * n));
        }

        /// <summary>
        /// Read a word from EFUSE BLOCK1 for non-ESP32 chips (S2/S3/C3/C6/H2).
        /// EFUSE_BLOCK1_ADDR = EfuseMacWord0Addr = EFUSE_BASE + 0x44 for these chips.
        /// Matches esptool.py pattern: read_reg(EFUSE_BLOCK1_ADDR + (4 * num_word)).
        /// </summary>
        private uint ReadBlock1Word(int wordNum)
        {
            return _client.ReadRegister(_config.EfuseMacWord0Addr + (uint)(4 * wordNum));
        }

        /// <summary>
        /// Read the MAC address from eFuse registers.
        /// </summary>
        /// <returns>MAC address as colon-separated uppercase hex string (e.g. "AA:BB:CC:DD:EE:FF").</returns>
        internal string ReadMacAddress()
        {
            EnsureConfig();

            byte[] mac;

            if (_config.ChipType == "esp32")
            {
                // esptool.py: words = [read_efuse(2), read_efuse(1)]
                //             bitstring = struct.pack(">II", *words)[2:8]
                uint word2 = ReadEfuse(2);  // 0x3FF5A008
                uint word1 = ReadEfuse(1);  // 0x3FF5A004

                // Pack big-endian: [word2 bytes 0-3][word1 bytes 4-7], take [2:8]
                mac = new byte[]
                {
                    (byte)((word2 >> 8) & 0xFF),
                    (byte)(word2 & 0xFF),
                    (byte)((word1 >> 24) & 0xFF),
                    (byte)((word1 >> 16) & 0xFF),
                    (byte)((word1 >> 8) & 0xFF),
                    (byte)(word1 & 0xFF),
                };
            }
            else
            {
                // ESP32-S2/S3/C3/C6/H2: MAC from dedicated eFuse registers
                uint word0 = _client.ReadRegister(_config.EfuseMacWord0Addr);
                uint word1 = _client.ReadRegister(_config.EfuseMacWord1Addr);

                mac = new byte[]
                {
                    (byte)((word1 >> 8) & 0xFF),
                    (byte)(word1 & 0xFF),
                    (byte)((word0 >> 24) & 0xFF),
                    (byte)((word0 >> 16) & 0xFF),
                    (byte)((word0 >> 8) & 0xFF),
                    (byte)(word0 & 0xFF),
                };
            }

            return $"{mac[0]:X2}:{mac[1]:X2}:{mac[2]:X2}:{mac[3]:X2}:{mac[4]:X2}:{mac[5]:X2}";
        }

        /// <summary>
        /// Read the flash chip JEDEC ID via SPI commands through the bootloader.
        /// </summary>
        /// <returns>Tuple of (manufacturerId, deviceId).</returns>
        internal (byte manufacturerId, short deviceId) ReadFlashId()
        {
            EnsureConfig();

            // Step 1: Send SPI_ATTACH command to attach SPI flash
            // ROM expects 8 bytes: [hspi_arg:4][is_legacy:1][0:3]
            // Stub expects 4 bytes: [hspi_arg:4]
            var attachData = _client.IsStubRunning ? new byte[4] : new byte[8];
            var attachResponse = _client.SendCommand(Esp32Command.SpiAttach, attachData);
            attachResponse.ThrowIfError();

            // Step 2: Execute SPI flash JEDEC ID command (0x9F) via register manipulation
            uint spibase = _config.SpiRegBase;
            uint jedecRawValue = RunSpiFlashCommand(0x9F, 0, 24);

            // Step 3: Parse JEDEC ID
            // The result comes back as: byte0=manufacturer, byte1=memType, byte2=capacity
            byte manufacturer = (byte)(jedecRawValue & 0xFF);
            short deviceId = (short)((jedecRawValue >> 8) & 0xFFFF);

            return (manufacturer, deviceId);
        }

        /// <summary>
        /// Detect flash size based on the JEDEC device ID.
        /// </summary>
        /// <param name="deviceId">Device ID from JEDEC read.</param>
        /// <returns>Flash size in bytes.</returns>
        internal static int DetectFlashSizeFromId(short deviceId)
        {
            // The capacity is encoded in the second byte of the device ID
            // Standard JEDEC encoding: 2^capacity = size in bytes
            int capacityByte = (deviceId >> 8) & 0xFF;

            return capacityByte switch
            {
                0x12 => 256 * 1024,         // 256KB
                0x13 => 512 * 1024,         // 512KB
                0x14 => 1 * 1024 * 1024,    // 1MB
                0x15 => 2 * 1024 * 1024,    // 2MB
                0x16 => 4 * 1024 * 1024,    // 4MB
                0x17 => 8 * 1024 * 1024,    // 8MB
                0x18 => 16 * 1024 * 1024,   // 16MB
                0x19 => 32 * 1024 * 1024,   // 32MB
                0x1A => 64 * 1024 * 1024,   // 64MB
                0x20 => 64 * 1024 * 1024,   // 64MB (alternate)
                _ => -1, // Unknown
            };
        }

        /// <summary>
        /// Read the crystal frequency. Returns "40MHz" or "26MHz".
        /// Estimated from UART clock divisor and current baud rate, matching esptool.py behavior.
        /// </summary>
        internal string ReadCrystalFrequency()
        {
            EnsureConfig();

            uint uartClkDivAddr;

            if (_config.ChipType == "esp32")
            {
                uartClkDivAddr = 0x3FF40014;
            }
            else
            {
                uartClkDivAddr = 0x60000014;
            }

            uint uartClkDiv = _client.ReadRegister(uartClkDivAddr);

            // The value is the integer part of the divisor in bits [19:0]
            uint divisor = uartClkDiv & 0xFFFFF;

            // esptool.py formula: est_xtal = (baud * divisor) / 1e6 / XTAL_CLK_DIVIDER
            double estXtal = ((double)_client.CurrentBaudRate * divisor) / 1_000_000.0 / _config.XtalClkDivider;

            if (estXtal > 45)
            {
                return "48MHz";
            }
            else if (estXtal > 33)
            {
                return "40MHz";
            }
            else
            {
                return "26MHz";
            }
        }

        /// <summary>
        /// Read the chip name/description including package type and silicon revision.
        /// Matches esptool.py's get_chip_description().
        /// </summary>
        internal string ReadChipName()
        {
            EnsureConfig();

            try
            {
                return _config.ChipType switch
                {
                    "esp32" => ReadEsp32ChipDescription(),
                    "esp32s3" => ReadEsp32S3ChipDescription(),
                    "esp32c3" => ReadEsp32C3ChipDescription(),
                    "esp32c6" => ReadEsp32C6ChipDescription(),
                    "esp32h2" => ReadEsp32H2ChipDescription(),
                    _ => _config.Name
                };
            }
            catch
            {
                return _config.Name;
            }
        }

        /// <summary>
        /// Read chip features string. This varies by chip family.
        /// </summary>
        internal string ReadFeatures()
        {
            EnsureConfig();

            // Feature detection requires reading chip-specific eFuse registers.
            // For now, return basic features based on chip family.
            // The detailed feature string will match what the existing code expects.
            return _config.ChipType switch
            {
                "esp32" => ReadEsp32Features(),
                "esp32s2" => ReadEsp32S2Features(),
                "esp32s3" => ReadEsp32S3Features(),
                "esp32c3" => ReadEsp32C3Features(),
                "esp32c6" => "Wi-Fi 6, BLE 5, 802.15.4",
                "esp32h2" => "BLE 5, 802.15.4",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Build a complete <see cref="Esp32DeviceInfo"/> from all detected values.
        /// </summary>
        /// <param name="flashSize">Flash size in bytes (already detected).</param>
        /// <param name="psramAvailability">PSRAM availability.</param>
        /// <param name="psRamSize">PSRAM size in MB.</param>
        internal Esp32DeviceInfo BuildDeviceInfo(
            int flashSize,
            PSRamAvailability psramAvailability,
            int psRamSize)
        {
            EnsureConfig();

            string chipName = ReadChipName();
            string features = ReadFeatures();
            string crystal = ReadCrystalFrequency();
            string mac = ReadMacAddress();
            var (manufacturerId, deviceId) = ReadFlashId();

            return new Esp32DeviceInfo(
                _config.Name,
                chipName,
                features,
                crystal,
                mac,
                manufacturerId,
                deviceId,
                flashSize,
                psramAvailability,
                psRamSize);
        }

        #region SPI Flash Commands

        /// <summary>
        /// Execute a SPI flash command and read back data.
        /// This works by writing to SPI controller registers via the bootloader's READ_REG/WRITE_REG.
        /// </summary>
        /// <param name="spiFlashCommand">SPI flash command byte (e.g. 0x9F for JEDEC ID).</param>
        /// <param name="writeBits">Number of bits to write (0 for read-only commands).</param>
        /// <param name="readBits">Number of bits to read back.</param>
        /// <returns>Raw value read from the SPI W0 register.</returns>
        private uint RunSpiFlashCommand(byte spiFlashCommand, int writeBits, int readBits)
        {
            // Save SPI registers (esptool.py restores these after the command)
            uint oldSpiUsr = _client.ReadRegister(_config.SpiUsrAddr);
            uint oldSpiUsr2 = _client.ReadRegister(_config.SpiUsr2Addr);

            // Set up the SPI command
            // USR2: command length (7 = 8 bits - 1) in [31:28], command value in [27:0]
            uint usr2Value = (7u << 28) | spiFlashCommand;
            _client.WriteRegister(_config.SpiUsr2Addr, usr2Value);

            // USR: set flags for command, read, and optionally write phases
            // Bit 31: USR_COMMAND (always set)
            // Bit 28: USR_MISO (set if reading)
            // Bit 27: USR_MOSI (set if writing)
            uint usrValue = (1u << 31);  // USR_COMMAND

            if (readBits > 0)
            {
                usrValue |= (1u << 28);  // USR_MISO
            }

            if (writeBits > 0)
            {
                usrValue |= (1u << 27);  // USR_MOSI
            }

            _client.WriteRegister(_config.SpiUsrAddr, usrValue);

            // Set read/write lengths
            if (_config.UsesOldSpiRegisters)
            {
                // ESP32: lengths are in USR1 register
                // USR1 bits [31:26] = MISO bit length - 1, bits [25:17] = MOSI bit length - 1
                uint usr1Value = 0;

                if (readBits > 0)
                {
                    usr1Value |= (uint)((readBits - 1) << 26);
                }

                if (writeBits > 0)
                {
                    usr1Value |= (uint)((writeBits - 1) << 17);
                }

                _client.WriteRegister(_config.SpiUsr1Addr, usr1Value);
            }
            else
            {
                // ESP32-S2/S3/C3/C6/H2: separate MOSI_DLEN and MISO_DLEN registers
                if (readBits > 0)
                {
                    _client.WriteRegister(_config.SpiMisoDlenAddr, (uint)(readBits - 1));
                }

                if (writeBits > 0)
                {
                    _client.WriteRegister(_config.SpiMosiDlenAddr, (uint)(writeBits - 1));
                }
            }

            // Clear W0 register before read (esptool.py does this when data_bits == 0)
            if (writeBits == 0)
            {
                _client.WriteRegister(_config.SpiW0Addr, 0);
            }

            // Trigger the SPI transaction by writing to SPI_CMD register (bit 18 = USR)
            _client.WriteRegister(_config.SpiCmdAddr, 1u << 18);

            // Poll until the SPI transaction completes (bit 18 cleared)
            for (int i = 0; i < 10; i++)
            {
                uint cmdVal = _client.ReadRegister(_config.SpiCmdAddr);

                if ((cmdVal & (1u << 18)) == 0)
                {
                    break;
                }

                Thread.Sleep(1);
            }

            // Read the result from W0 register
            uint result = _client.ReadRegister(_config.SpiW0Addr);

            // Restore SPI registers
            _client.WriteRegister(_config.SpiUsrAddr, oldSpiUsr);
            _client.WriteRegister(_config.SpiUsr2Addr, oldSpiUsr2);

            return result;
        }

        #endregion

        #region Chip-Specific Feature Detection

        private string ReadEsp32Features()
        {
            // Matches esptool.py ESP32ROM.get_chip_features()
            try
            {
                uint word3 = ReadEfuse(3);

                var features = new System.Collections.Generic.List<string> { "Wi-Fi" };

                // Bit 1: chip_ver_dis_bt (1 = BT disabled)
                if ((word3 & (1 << 1)) == 0)
                {
                    features.Add("BT");
                }

                // Bit 0: chip_ver_dis_app_cpu (1 = single core)
                if ((word3 & (1 << 0)) != 0)
                {
                    features.Add("Single Core + LP Core");
                }
                else
                {
                    features.Add("Dual Core + LP Core");
                }

                // Bit 13: chip_cpu_freq_rated
                if ((word3 & (1 << 13)) != 0)
                {
                    // Bit 12: chip_cpu_freq_low (1 = 160MHz, 0 = 240MHz)
                    if ((word3 & (1 << 12)) != 0)
                    {
                        features.Add("160MHz");
                    }
                    else
                    {
                        features.Add("240MHz");
                    }
                }

                // Package version for embedded flash/PSRAM
                int pkgVersion = GetEsp32PkgVersion();

                if (pkgVersion == 2 || pkgVersion == 4 || pkgVersion == 5 || pkgVersion == 6)
                {
                    features.Add("Embedded Flash");
                }

                if (pkgVersion == 6)
                {
                    features.Add("Embedded PSRAM");
                }

                // Vref calibration from word4
                uint word4 = ReadEfuse(4);
                uint adcVref = (word4 >> 8) & 0x1F;

                if (adcVref != 0)
                {
                    features.Add("Vref calibration in eFuse");
                }

                // BLK3 partially reserved
                if (((word3 >> 14) & 0x1) != 0)
                {
                    features.Add("BLK3 partially reserved");
                }

                // Coding scheme from word6
                uint word6 = ReadEfuse(6);
                uint codingScheme = word6 & 0x3;

                string codingSchemeName = codingScheme switch
                {
                    0 => "None",
                    1 => "3/4",
                    2 => "Repeat (UNSUPPORTED)",
                    3 => "None (may contain encoding data)",
                    _ => "Unknown"
                };

                features.Add($"Coding Scheme {codingSchemeName}");

                return string.Join(", ", features);
            }
            catch
            {
                return "Wi-Fi";
            }
        }

        /// <summary>
        /// Get ESP32 package version from eFuse. Matches esptool.py get_pkg_version().
        /// </summary>
        private int GetEsp32PkgVersion()
        {
            uint word3 = ReadEfuse(3);
            int pkgVersion = (int)((word3 >> 9) & 0x07);
            pkgVersion += (int)(((word3 >> 2) & 0x1) << 3);
            return pkgVersion;
        }

        /// <summary>
        /// Get ESP32 chip description with package name and revision.
        /// Matches esptool.py ESP32ROM.get_chip_description().
        /// </summary>
        private string ReadEsp32ChipDescription()
        {
            int pkgVersion = GetEsp32PkgVersion();

            // Major revision
            uint word3 = ReadEfuse(3);
            uint word5 = ReadEfuse(5);
            uint revBit0 = (word3 >> 15) & 0x1;
            uint revBit1 = (word5 >> 20) & 0x1;

            // APB_CTL_DATE register at 0x3FF66000 + 0x7C
            uint apbCtlDate = _client.ReadRegister(0x3FF6607C);
            uint revBit2 = (apbCtlDate >> 31) & 0x1;

            uint combineValue = (revBit2 << 2) | (revBit1 << 1) | revBit0;
            int majorRev = combineValue switch
            {
                0 => 0,
                1 => 1,
                3 => 2,
                7 => 3,
                _ => 0
            };

            // Minor revision
            int minorRev = (int)((word5 >> 24) & 0x3);

            bool rev3 = majorRev == 3;
            bool singleCore = (word3 & (1 << 0)) != 0;

            string chipName = pkgVersion switch
            {
                0 => singleCore ? "ESP32-S0WDQ6" : rev3 ? "ESP32-D0WDQ6-V3" : "ESP32-D0WDQ6",
                1 => singleCore ? "ESP32-S0WD" : rev3 ? "ESP32-D0WD-V3" : "ESP32-D0WD",
                2 => "ESP32-D2WD",
                4 => "ESP32-U4WDH",
                5 => rev3 ? "ESP32-PICO-V3" : "ESP32-PICO-D4",
                6 => "ESP32-PICO-V3-02",
                7 => "ESP32-D0WDR2-V3",
                _ => "Unknown ESP32"
            };

            return $"{chipName} (revision v{majorRev}.{minorRev})";
        }

        private string ReadEsp32S2Features()
        {
            return "Wi-Fi";
        }

        /// <summary>
        /// Read common chip revision from BLOCK1 eFuse for RISC-V and Xtensa-S3 chips.
        /// Common layout: pkg_version at word3[23:21], minor at word5[23]+word3[20:18], major at word5[25:24].
        /// Matches esptool.py get_minor_chip_version/get_major_chip_version for C3/C6/H2.
        /// </summary>
        private (int major, int minor) ReadRiscVChipRevision()
        {
            uint block1Word3 = ReadBlock1Word(3);
            uint block1Word5 = ReadBlock1Word(5);

            int hi = (int)((block1Word5 >> 23) & 0x01);
            int low = (int)((block1Word3 >> 18) & 0x07);
            int minor = (hi << 3) + low;
            int major = (int)((block1Word5 >> 24) & 0x03);

            return (major, minor);
        }

        /// <summary>
        /// Read package version from BLOCK1 word3 bits [23:21].
        /// Common across S3/C3/C6/H2.
        /// </summary>
        private int ReadBlock1PkgVersion()
        {
            uint block1Word3 = ReadBlock1Word(3);
            return (int)((block1Word3 >> 21) & 0x07);
        }

        /// <summary>
        /// ESP32-S3 chip description. Has eco0 workaround for early silicon.
        /// Matches esptool.py ESP32S3ROM.get_chip_description().
        /// </summary>
        private string ReadEsp32S3ChipDescription()
        {
            uint block1Word3 = ReadBlock1Word(3);
            uint block1Word5 = ReadBlock1Word(5);

            int pkgVersion = (int)((block1Word3 >> 21) & 0x07);

            // Raw revision values
            int hiMinor = (int)((block1Word5 >> 23) & 0x01);
            int lowMinor = (int)((block1Word3 >> 18) & 0x07);
            int rawMinor = (hiMinor << 3) + lowMinor;
            int rawMajor = (int)((block1Word5 >> 24) & 0x03);

            // eco0 workaround: early silicon has block version 1.1 but is actually v0.0
            int blkVersionMinor = (int)((block1Word3 >> 24) & 0x07);
            // EFUSE_BLOCK2_ADDR = EFUSE_BLOCK1_ADDR + 0x18 for S3
            uint block2Word4 = _client.ReadRegister(_config.EfuseMacWord0Addr + 0x18 + 16);
            int blkVersionMajor = (int)(block2Word4 & 0x03);

            bool isEco0 = (rawMinor & 0x7) == 0 && blkVersionMajor == 1 && blkVersionMinor == 1;

            int majorRev = isEco0 ? 0 : rawMajor;
            int minorRev = isEco0 ? 0 : rawMinor;

            string chipName = pkgVersion switch
            {
                0 => "ESP32-S3 (QFN56)",
                1 => "ESP32-S3-PICO-1 (LGA56)",
                _ => "Unknown ESP32-S3"
            };

            return $"{chipName} (revision v{majorRev}.{minorRev})";
        }

        /// <summary>
        /// ESP32-C3 chip description.
        /// Matches esptool.py ESP32C3ROM.get_chip_description().
        /// </summary>
        private string ReadEsp32C3ChipDescription()
        {
            int pkgVersion = ReadBlock1PkgVersion();
            var (majorRev, minorRev) = ReadRiscVChipRevision();

            string chipName = pkgVersion switch
            {
                0 => "ESP32-C3 (QFN32)",
                1 => "ESP8685 (QFN28)",
                2 => "ESP32-C3 AZ (QFN32)",
                3 => "ESP8686 (QFN24)",
                _ => "Unknown ESP32-C3"
            };

            return $"{chipName} (revision v{majorRev}.{minorRev})";
        }

        /// <summary>
        /// ESP32-C6 chip description.
        /// </summary>
        private string ReadEsp32C6ChipDescription()
        {
            int pkgVersion = ReadBlock1PkgVersion();
            var (majorRev, minorRev) = ReadRiscVChipRevision();

            string chipName = pkgVersion switch
            {
                0 => "ESP32-C6 (QFN40)",
                1 => "ESP32-C6FH4 (QFN32)",
                _ => "Unknown ESP32-C6"
            };

            return $"{chipName} (revision v{majorRev}.{minorRev})";
        }

        /// <summary>
        /// ESP32-H2 chip description.
        /// </summary>
        private string ReadEsp32H2ChipDescription()
        {
            int pkgVersion = ReadBlock1PkgVersion();
            var (majorRev, minorRev) = ReadRiscVChipRevision();

            string chipName = pkgVersion switch
            {
                0 => "ESP32-H2 (QFN32)",
                1 => "ESP32-H2AZ (QFN32)",
                _ => "Unknown ESP32-H2"
            };

            return $"{chipName} (revision v{majorRev}.{minorRev})";
        }

        /// <summary>
        /// ESP32-S3 features with embedded flash/PSRAM detection.
        /// Matches esptool.py ESP32S3ROM.get_chip_features().
        /// </summary>
        private string ReadEsp32S3Features()
        {
            try
            {
                var features = new System.Collections.Generic.List<string>
                    { "Wi-Fi", "BLE", "Dual Core", "240MHz" };

                uint block1Word3 = ReadBlock1Word(3);
                uint block1Word4 = ReadBlock1Word(4);
                uint block1Word5 = ReadBlock1Word(5);

                // Flash cap: block1_word3[29:27]
                int flashCap = (int)((block1Word3 >> 27) & 0x07);
                string flash = flashCap switch
                {
                    1 => "Embedded Flash 8MB",
                    2 => "Embedded Flash 4MB",
                    _ => null
                };

                if (flash != null)
                {
                    int flashVendorId = (int)(block1Word4 & 0x07);
                    string flashVendor = flashVendorId switch
                    {
                        1 => "XMC", 2 => "GD", 3 => "FM", 4 => "TT", 5 => "BY", _ => ""
                    };
                    features.Add(string.IsNullOrEmpty(flashVendor) ? flash : $"{flash} ({flashVendor})");
                }

                // PSRAM cap: low 2 bits from word4[4:3], hi bit from word5[19]
                int psramCapLow = (int)((block1Word4 >> 3) & 0x03);
                int psramCapHi = (int)((block1Word5 >> 19) & 0x01);
                int psramCap = (psramCapHi << 2) | psramCapLow;
                string psram = psramCap switch
                {
                    1 => "Embedded PSRAM 8MB",
                    2 => "Embedded PSRAM 2MB",
                    3 => "Embedded PSRAM 16MB",
                    4 => "Embedded PSRAM 4MB",
                    _ => null
                };

                if (psram != null)
                {
                    int psramVendorId = (int)((block1Word4 >> 7) & 0x03);
                    string psramVendor = psramVendorId switch
                    {
                        1 => "AP_3v3", 2 => "AP_1v8", _ => ""
                    };
                    features.Add(string.IsNullOrEmpty(psramVendor) ? psram : $"{psram} ({psramVendor})");
                }

                return string.Join(", ", features);
            }
            catch
            {
                return "Wi-Fi, BLE";
            }
        }

        /// <summary>
        /// ESP32-C3 features with embedded flash detection.
        /// Matches esptool.py ESP32C3ROM.get_chip_features().
        /// </summary>
        private string ReadEsp32C3Features()
        {
            try
            {
                var features = new System.Collections.Generic.List<string>
                    { "Wi-Fi", "BLE", "Single Core", "160MHz" };

                uint block1Word3 = ReadBlock1Word(3);
                uint block1Word4 = ReadBlock1Word(4);

                // Flash cap: block1_word3[29:27]
                int flashCap = (int)((block1Word3 >> 27) & 0x07);
                string flash = flashCap switch
                {
                    1 => "Embedded Flash 4MB",
                    2 => "Embedded Flash 2MB",
                    3 => "Embedded Flash 1MB",
                    4 => "Embedded Flash 8MB",
                    _ => null
                };

                if (flash != null)
                {
                    int flashVendorId = (int)(block1Word4 & 0x07);
                    string flashVendor = flashVendorId switch
                    {
                        1 => "XMC", 2 => "GD", 3 => "FM", 4 => "TT", 5 => "ZBIT", _ => ""
                    };
                    features.Add(string.IsNullOrEmpty(flashVendor) ? flash : $"{flash} ({flashVendor})");
                }

                return string.Join(", ", features);
            }
            catch
            {
                return "Wi-Fi, BLE";
            }
        }

        #endregion

        private void EnsureConfig()
        {
            if (_config == null)
            {
                throw new InvalidOperationException(
                    "Chip type has not been detected. Call DetectChipType() first.");
            }
        }
    }
}
