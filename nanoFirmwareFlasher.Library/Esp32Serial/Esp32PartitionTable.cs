// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    internal static class Esp32PartitionTable
    {
        internal const int Address = 0x8000;
        internal const int Size = 0xC00;
        private const int EntrySize = 32;

        internal static int GetDeploymentAddress(byte[] table, long applicationSize)
        {
            if (table == null || table.Length == 0 || table.Length > Size || table.Length % EntrySize != 0)
            {
                throw new EspToolExecutionException("Invalid ESP32 partition table length.");
            }

            uint address = 0;
            uint size = 0;

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

                // nanoFramework managed code uses a data partition with custom subtype 0x84.
                if (table[position + 2] == 0x01 && table[position + 3] == 0x84)
                {
                    if (size != 0)
                    {
                        throw new EspToolExecutionException("Multiple ESP32 deployment partitions found.");
                    }

                    address = Esp32CommandPacket.ReadUInt32LE(table, position + 4);
                    size = Esp32CommandPacket.ReadUInt32LE(table, position + 8);

                    if (address < 0x10000 || size == 0 || (long)address + size > int.MaxValue
                        || address % 0x1000 != 0 || size % 0x1000 != 0)
                    {
                        throw new EspToolExecutionException("Invalid ESP32 deployment partition range.");
                    }
                }
            }

            if (size == 0)
            {
                throw new EspToolExecutionException(
                    "No nanoFramework deployment partition found in the ESP32 partition table. Specify --address to override.");
            }

            if (applicationSize < 0 || applicationSize > size)
            {
                throw new EspToolExecutionException(
                    $"Application image ({applicationSize} bytes) exceeds the ESP32 deployment partition ({size} bytes).");
            }

            return (int)address;
        }
    }
}
