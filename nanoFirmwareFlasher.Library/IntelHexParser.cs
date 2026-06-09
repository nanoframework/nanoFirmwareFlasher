//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Parser for Intel HEX format files (.hex).
    /// Produces a list of memory segments with absolute addresses and data.
    /// </summary>
    public static class IntelHexParser
    {
        /// <summary>
        /// Represents a contiguous block of memory data parsed from a HEX file.
        /// </summary>
        public class MemoryBlock
        {
            /// <summary>
            /// The absolute start address in flash memory.
            /// </summary>
            public uint Address { get; set; }

            /// <summary>
            /// The data bytes for this block.
            /// </summary>
            public byte[] Data { get; set; }
        }

        /// <summary>
        /// Parses an Intel HEX file and returns a list of contiguous memory blocks.
        /// Adjacent records are coalesced into single blocks for efficient flash writes.
        /// </summary>
        /// <param name="hexFilePath">Path to the .hex file.</param>
        /// <returns>A list of <see cref="MemoryBlock"/> with absolute addresses and data.</returns>
        /// <exception cref="FileNotFoundException">The specified HEX file does not exist.</exception>
        /// <exception cref="FormatException">The HEX file contains invalid data.</exception>
        public static List<MemoryBlock> Parse(string hexFilePath)
        {
            if (!File.Exists(hexFilePath))
            {
                throw new FileNotFoundException("HEX file not found.", hexFilePath);
            }

            string[] lines = File.ReadAllLines(hexFilePath);

            // sorted dictionary: absolute address -> data bytes
            var segments = new SortedDictionary<uint, List<byte>>();

            uint baseAddress = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (line[0] != ':')
                {
                    throw new FormatException($"Invalid HEX record (missing start code): {line}");
                }

                if (line.Length < 11)
                {
                    throw new FormatException($"HEX record too short: {line}");
                }

                // parse fields
                byte byteCount = ParseHexByte(line, 1);
                ushort recordAddress = ParseHexUInt16(line, 3);
                byte recordType = ParseHexByte(line, 7);

                // verify length
                int expectedLength = 1 + 2 + (byteCount * 2) + 2 + 2 + 2 + 1; // ':' + byte_count(2) + address(4) + type(2) + data + checksum(2)
                // simpler: 11 + byteCount * 2
                if (line.Length < 11 + (byteCount * 2))
                {
                    throw new FormatException($"HEX record length mismatch: {line}");
                }

                // verify checksum
                if (!VerifyChecksum(line))
                {
                    throw new FormatException($"HEX record checksum error: {line}");
                }

                switch (recordType)
                {
                    case 0x00: // Data Record
                        byte[] data = ParseDataBytes(line, 9, byteCount);
                        uint absoluteAddress = baseAddress + recordAddress;
                        AddData(segments, absoluteAddress, data);
                        break;

                    case 0x01: // End of File
                        break;

                    case 0x02: // Extended Segment Address
                        if (byteCount != 2)
                        {
                            throw new FormatException($"Invalid Extended Segment Address record: {line}");
                        }

                        baseAddress = (uint)ParseHexUInt16(line, 9) << 4;
                        break;

                    case 0x03: // Start Segment Address (ignored — CS:IP for x86)
                        break;

                    case 0x04: // Extended Linear Address
                        if (byteCount != 2)
                        {
                            throw new FormatException($"Invalid Extended Linear Address record: {line}");
                        }

                        baseAddress = (uint)ParseHexUInt16(line, 9) << 16;
                        break;

                    case 0x05: // Start Linear Address (ignored — execution start address)
                        break;

                    default:
                        throw new FormatException($"Unknown HEX record type 0x{recordType:X2}: {line}");
                }
            }

            // coalesce adjacent segments into contiguous memory blocks
            return CoalesceSegments(segments);
        }

        /// <summary>
        /// Parses a HEX file and returns the raw data as a single byte array with the start address.
        /// Gaps between non-contiguous blocks are filled with 0xFF (erased flash value).
        /// </summary>
        /// <param name="hexFilePath">Path to the .hex file.</param>
        /// <param name="startAddress">The start address of the first block.</param>
        /// <returns>A byte array containing all data from the HEX file.</returns>
        public static byte[] ParseToFlatBinary(string hexFilePath, out uint startAddress)
        {
            List<MemoryBlock> blocks = Parse(hexFilePath);

            if (blocks.Count == 0)
            {
                startAddress = 0;
                return [];
            }

            startAddress = blocks[0].Address;
            uint endAddress = blocks[blocks.Count - 1].Address + (uint)blocks[blocks.Count - 1].Data.Length;
            uint totalSize = endAddress - startAddress;

            byte[] result = new byte[totalSize];
            // fill with 0xFF (erased flash)
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = 0xFF;
            }

            foreach (MemoryBlock block in blocks)
            {
                uint offset = block.Address - startAddress;
                Buffer.BlockCopy(block.Data, 0, result, (int)offset, block.Data.Length);
            }

            return result;
        }

        private static void AddData(SortedDictionary<uint, List<byte>> segments, uint address, byte[] data)
        {
            if (!segments.TryGetValue(address, out List<byte> existing))
            {
                segments[address] = [.. data];
            }
            else
            {
                existing.AddRange(data);
            }
        }

        private static List<MemoryBlock> CoalesceSegments(SortedDictionary<uint, List<byte>> segments)
        {
            var result = new List<MemoryBlock>();

            MemoryBlock current = null;

            foreach (KeyValuePair<uint, List<byte>> kvp in segments)
            {
                uint addr = kvp.Key;
                byte[] data = kvp.Value.ToArray();

                if (current == null)
                {
                    current = new MemoryBlock { Address = addr, Data = data };
                }
                else if (addr == current.Address + (uint)current.Data.Length)
                {
                    // contiguous — append
                    byte[] merged = new byte[current.Data.Length + data.Length];
                    Buffer.BlockCopy(current.Data, 0, merged, 0, current.Data.Length);
                    Buffer.BlockCopy(data, 0, merged, current.Data.Length, data.Length);
                    current.Data = merged;
                }
                else
                {
                    // gap — emit current and start new
                    result.Add(current);
                    current = new MemoryBlock { Address = addr, Data = data };
                }
            }

            if (current != null)
            {
                result.Add(current);
            }

            return result;
        }

        private static bool VerifyChecksum(string line)
        {
            int sum = 0;

            // sum all bytes from byte_count to checksum (inclusive)
            for (int i = 1; i < line.Length; i += 2)
            {
                sum += ParseHexByte(line, i);
            }

            return (sum & 0xFF) == 0;
        }

        private static byte ParseHexByte(string line, int offset)
        {
            return byte.Parse(line.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static ushort ParseHexUInt16(string line, int offset)
        {
            return ushort.Parse(line.Substring(offset, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static byte[] ParseDataBytes(string line, int offset, int count)
        {
            byte[] data = new byte[count];

            for (int i = 0; i < count; i++)
            {
                data[i] = ParseHexByte(line, offset + (i * 2));
            }

            return data;
        }
    }
}
