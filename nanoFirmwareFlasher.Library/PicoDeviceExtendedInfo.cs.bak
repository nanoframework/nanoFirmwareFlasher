// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Extended device information for RP2350 devices obtained via the
    /// PICOBOOT GET_INFO command and OTP memory reads.
    /// </summary>
    public class PicoDeviceExtendedInfo
    {
        #region GET_INFO SYS response tag constants

        /// <summary>Chip info tag (chip family, revision).</summary>
        internal const byte TagChipInfo = 0x01;

        /// <summary>Critical boot flags tag.</summary>
        internal const byte TagCritical = 0x02;

        /// <summary>Flash device info tag (JEDEC manufacturer/device ID).</summary>
        internal const byte TagFlashDevInfo = 0x03;

        /// <summary>Boot diagnostics tag.</summary>
        internal const byte TagBootDiagnostic = 0x04;

        /// <summary>Boot random nonce tag.</summary>
        internal const byte TagBootRandom = 0x05;

        #endregion

        #region OTP critical row definitions

        /// <summary>OTP row index for CRIT0 flags.</summary>
        internal const ushort OtpRowCrit0 = 0x0000;

        /// <summary>Number of critical config rows to read (CRIT0-CRIT1).</summary>
        internal const ushort OtpCritRowCount = 2;

        // CRIT0 bit masks (RP2350 datasheet Table 172)
        internal const ushort Crit0SecureBootEnable = 0x0001;
        internal const ushort Crit0SecureDebugDisable = 0x0002;
        internal const ushort Crit0DebugDisable = 0x0004;
        internal const ushort Crit0GlitchDetectorEnable = 0x0010;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the chip type ("RP2040" or "RP2350").
        /// </summary>
        public string ChipType { get; }

        /// <summary>
        /// Gets the raw SYS info response from GET_INFO, or <c>null</c> if not available.
        /// </summary>
        public byte[] RawSysInfo { get; }

        /// <summary>
        /// Gets the parsed info items from the GET_INFO SYS response.
        /// Each entry maps a tag byte to the list of uint32 values for that tag.
        /// </summary>
        public IReadOnlyDictionary<byte, List<uint>> SysInfoItems { get; }

        /// <summary>
        /// Gets the raw partition table info from GET_INFO, or <c>null</c> if not available.
        /// </summary>
        public byte[] RawPartitionInfo { get; }

        /// <summary>
        /// Gets the raw OTP critical rows, or <c>null</c> if not read.
        /// </summary>
        public byte[] RawOtpCritical { get; }

        /// <summary>
        /// Gets the CRIT0 flags value from OTP, or <c>null</c> if not read.
        /// </summary>
        public ushort? Crit0Flags { get; }

        /// <summary>
        /// Gets whether secure boot is enabled (from OTP CRIT0), or <c>null</c> if unknown.
        /// </summary>
        public bool? SecureBootEnabled { get; }

        /// <summary>
        /// Gets whether debug is disabled (from OTP CRIT0), or <c>null</c> if unknown.
        /// </summary>
        public bool? DebugDisabled { get; }

        /// <summary>
        /// Gets the flash JEDEC info as a uint32 value, or <c>null</c> if not available.
        /// Format: bits [7:0] = manufacturer ID, bits [15:8] = memory type, bits [23:16] = capacity.
        /// </summary>
        public uint? FlashJedecId { get; }

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="PicoDeviceExtendedInfo"/> class. 
        /// </summary>
        internal PicoDeviceExtendedInfo(
            string chipType,
            byte[] rawSysInfo,
            byte[] rawPartitionInfo,
            byte[] rawOtpCritical)
        {
            ChipType = chipType;
            RawSysInfo = rawSysInfo;
            RawPartitionInfo = rawPartitionInfo;
            RawOtpCritical = rawOtpCritical;

            // parse SYS info TLV items
            SysInfoItems = ParseTlvResponse(rawSysInfo);

            // extract flash JEDEC ID if available
            if (SysInfoItems.TryGetValue(TagFlashDevInfo, out var flashItems) && flashItems.Count > 0)
            {
                FlashJedecId = flashItems[0];
            }

            // parse OTP critical flags
            if (rawOtpCritical != null && rawOtpCritical.Length >= 2)
            {
                Crit0Flags = (ushort)(rawOtpCritical[0] | (rawOtpCritical[1] << 8));
                SecureBootEnabled = (Crit0Flags.Value & Crit0SecureBootEnable) != 0;
                DebugDisabled = (Crit0Flags.Value & Crit0DebugDisable) != 0;
            }
        }

        /// <summary>
        /// Parses a GET_INFO response in TLV (tag-length-value) format.
        /// Each item: 1-byte tag, 1-byte count (number of uint32 values), then count*4 bytes of data.
        /// </summary>
        internal static Dictionary<byte, List<uint>> ParseTlvResponse(byte[] data)
        {
            var items = new Dictionary<byte, List<uint>>();

            if (data == null || data.Length < 2)
            {
                return items;
            }

            int offset = 0;

            while (offset + 2 <= data.Length)
            {
                byte tag = data[offset++];
                byte count = data[offset++];

                // sanity check: count * 4 bytes must fit in remaining data
                if (count == 0 || offset + (count * 4) > data.Length)
                {
                    break;
                }

                var values = new List<uint>(count);

                for (int i = 0; i < count; i++)
                {
                    uint val = (uint)(data[offset]
                        | (data[offset + 1] << 8)
                        | (data[offset + 2] << 16)
                        | (data[offset + 3] << 24));
                    values.Add(val);
                    offset += 4;
                }

                items[tag] = values;
            }

            return items;
        }

        /// <summary>
        /// Formats flash JEDEC ID into a human-readable string.
        /// </summary>
        public static string FormatFlashJedecId(uint jedecId)
        {
            byte manufacturerId = (byte)(jedecId & 0xFF);
            byte memoryType = (byte)((jedecId >> 8) & 0xFF);
            byte capacity = (byte)((jedecId >> 16) & 0xFF);

            string manufacturer = manufacturerId switch
            {
                0xEF => "Winbond",
                0xC8 => "GigaDevice",
                0x1F => "Adesto/Atmel",
                0x20 => "Micron/Numonyx",
                0xC2 => "Macronix",
                0x01 => "Spansion/Cypress",
                0x9D => "ISSI",
                0x68 => "BoyaMicro",
                _ => $"Unknown(0x{manufacturerId:X2})"
            };

            // capacity byte typically encodes size as 2^capacity bytes
            string sizeStr = capacity >= 16 && capacity <= 26
                ? $"{1 << (capacity - 20)}MB"
                : $"2^{capacity}B";

            return $"{manufacturer} 0x{memoryType:X2}:{capacity:X2} ({sizeStr})";
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder sb = new();

            sb.AppendLine($"Chip: {ChipType}");
            sb.AppendLine("Interface: PICOBOOT USB");

            if (FlashJedecId.HasValue)
            {
                sb.AppendLine($"Flash: {FormatFlashJedecId(FlashJedecId.Value)}");
            }

            if (Crit0Flags.HasValue)
            {
                sb.AppendLine($"Secure boot: {(SecureBootEnabled == true ? "Enabled" : "Disabled")}");
                sb.AppendLine($"Debug: {(DebugDisabled == true ? "Disabled" : "Enabled")}");
            }

            // display any other SYS info items as hex
            if (SysInfoItems.Count > 0)
            {
                foreach (var kvp in SysInfoItems)
                {
                    // skip already-displayed items
                    if (kvp.Key == TagFlashDevInfo)
                    {
                        continue;
                    }

                    string tagName = kvp.Key switch
                    {
                        TagChipInfo => "Chip info",
                        TagCritical => "Critical flags",
                        TagBootDiagnostic => "Boot diagnostic",
                        TagBootRandom => "Boot random",
                        _ => $"Tag 0x{kvp.Key:X2}"
                    };

                    sb.Append($"{tagName}:");

                    foreach (uint val in kvp.Value)
                    {
                        sb.Append($" 0x{val:X8}");
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
