// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    internal static class Stm32WifiImage
    {
        internal const uint Address = 0x90000000;
        internal const int HeaderSize = 0x100;
        internal const int AreaSize = 0x40000;
        internal const string TargetName = "MXCHIP_AZ3166";

        internal static uint ReadUInt32(byte[] data, int offset)
            => (uint)(data[offset] | data[offset + 1] << 8 |
                data[offset + 2] << 16 | data[offset + 3] << 24);

        internal static uint ComputeCrc(byte[] data, int offset, int count)
        {
            uint crc = 0;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= (uint)data[i] << 24;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
                }
            }

            return crc;
        }

        internal static bool HasValidHeader(byte[] data)
        {
            if (data == null || data.Length < HeaderSize ||
                ReadUInt32(data, 0) != 0x4657464E || ReadUInt32(data, 4) != 1)
            {
                return false;
            }

            uint size = ReadUInt32(data, 8);
            if (size == 0 || size > AreaSize - HeaderSize)
            {
                return false;
            }

            for (int i = 16; i < HeaderSize; i++)
            {
                if (data[i] != 0xFF)
                {
                    return false;
                }
            }

            return true;
        }

        internal static void Validate(uint address, byte[] data)
        {
            if (address != Address || !HasValidHeader(data) ||
                data.Length != HeaderSize + ReadUInt32(data, 8) ||
                ComputeCrc(data, HeaderSize, data.Length - HeaderSize) != ReadUInt32(data, 12))
            {
                throw new SwdProtocolException(
                    "Invalid MXCHIP Wi-Fi image: expected a complete NFWF v1 image with valid size, padding and CRC at 0x90000000.");
            }
        }
    }
}
