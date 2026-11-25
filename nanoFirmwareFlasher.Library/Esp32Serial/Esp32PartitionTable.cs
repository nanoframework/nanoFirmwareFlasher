// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    internal static class Esp32PartitionTable
    {
        internal const int Address = 0x8000;
        internal const int Size = 0xC00;
        private const int EntrySize = 32;

        internal static int GetDeploymentAddress(byte[] table, long applicationSize)
            => GetPartitionAddress(table, applicationSize, "deployment", int.MaxValue);

        internal static int GetClrAddress(byte[] table, long imageSize, int flashSize)
            => GetPartitionAddress(table, imageSize, "clr", flashSize);

        internal static IReadOnlyList<Esp32Partition> Parse(byte[] table, int flashSize)
        {
            if (table == null || table.Length == 0 || table.Length > Size || table.Length % EntrySize != 0)
            {
                throw new EspToolExecutionException("Invalid ESP32 partition table length.");
            }

            List<Esp32Partition> partitions = new List<Esp32Partition>();

            for (int position = 0; position < table.Length; position += EntrySize)
            {
                if (table[position] == 0xFF && table[position + 1] == 0xFF)
                {
                    break;
                }

                if (table[position] == 0xEB && table[position + 1] == 0xEB)
                {
                    using (MD5 md5 = MD5.Create())
                    {
                        byte[] checksum = md5.ComputeHash(table, 0, position);
                        for (int i = 0; i < checksum.Length; i++)
                        {
                            if (checksum[i] != table[position + 16 + i])
                            {
                                throw new EspToolExecutionException("ESP32 partition table checksum mismatch.");
                            }
                        }
                    }

                    break;
                }

                if (table[position] != 0xAA || table[position + 1] != 0x50)
                {
                    throw new EspToolExecutionException("Invalid ESP32 partition table entry.");
                }

                uint address = Esp32CommandPacket.ReadUInt32LE(table, position + 4);
                uint size = Esp32CommandPacket.ReadUInt32LE(table, position + 8);
                byte type = table[position + 2];
                byte subtype = table[position + 3];
                string label = Encoding.ASCII.GetString(table, position + 12, 16).TrimEnd('\0');
                uint flags = Esp32CommandPacket.ReadUInt32LE(table, position + 28);

                if (address < Address + 0x1000 || size == 0 || (long)address + size > flashSize
                    || address % 0x1000 != 0 || size % 0x1000 != 0 || string.IsNullOrWhiteSpace(label))
                {
                    throw new EspToolExecutionException($"Invalid ESP32 partition '{label}' range or label.");
                }

                if (type == 0x00 && address % 0x10000 != 0)
                {
                    throw new EspToolExecutionException($"The ESP32 application partition '{label}' must be aligned to 64 KB.");
                }

                if (partitions.Any(partition => string.Equals(partition.Label, label, StringComparison.OrdinalIgnoreCase)
                    || (address < (long)partition.Address + partition.Size && partition.Address < (long)address + size)))
                {
                    throw new EspToolExecutionException($"Duplicate or overlapping ESP32 partition '{label}'.");
                }

                partitions.Add(new Esp32Partition(label, type, subtype, (int)address, (int)size, flags));
            }

            return partitions;
        }

        internal static bool Matches(Esp32Partition partition, string selector)
        {
            switch (selector.ToLowerInvariant())
            {
                case "clr":
                    return partition.Type == 0 && partition.Subtype == 0;
                case "deploy":
                case "deployment":
                    return partition.Type == 1 && partition.Subtype == 0x84;
                default:
                    return string.Equals(partition.Label, selector, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static Esp32Partition Find(IReadOnlyList<Esp32Partition> partitions, string selector, bool required = true)
        {
            List<Esp32Partition> matches = partitions.Where(partition => Matches(partition, selector)).ToList();
            if (matches.Count > 1)
            {
                throw new EspToolExecutionException($"Multiple ESP32 '{selector}' partitions found.");
            }

            if (matches.Count == 0 && required)
            {
                throw new EspToolExecutionException($"No ESP32 '{selector}' partition found. Application deployment supports --address; otherwise flash a complete firmware package first.");
            }

            return matches.FirstOrDefault();
        }

        internal static (Esp32Partition Source, Esp32Partition Destination)? GetConfigRestorePlan(
            byte[] installedTable,
            byte[] incomingTable,
            int flashSize,
            bool requireBackup)
        {
            Esp32Partition source = Find(Parse(installedTable, flashSize), "config", requireBackup);
            Esp32Partition destination = Find(Parse(incomingTable, flashSize), "config", source != null);
            if (source == null)
            {
                return null;
            }

            if (source.Type != destination.Type || source.Subtype != destination.Subtype
                || source.Size != destination.Size || source.Flags != destination.Flags || (source.Flags & 1) != 0)
            {
                throw new EspToolExecutionException(
                    "The installed and incoming ESP32 configuration partitions are incompatible. Back up the device and explicitly use masserase to replace the layout without preserving configuration.");
            }

            return (source, destination);
        }

        private static int GetPartitionAddress(byte[] table, long imageSize, string selector, int flashSize)
        {
            Esp32Partition partition = Find(Parse(table, flashSize), selector);
            if (selector == "clr" && (partition.Flags & 1) != 0)
            {
                throw new EspToolExecutionException("CLR-only flashing of an encrypted ESP32 partition is not supported.");
            }

            if (imageSize < 0 || imageSize > partition.Size)
            {
                throw new EspToolExecutionException($"Image ({imageSize} bytes) exceeds the ESP32 {selector} partition ({partition.Size} bytes).");
            }

            return partition.Address;
        }
    }
}
