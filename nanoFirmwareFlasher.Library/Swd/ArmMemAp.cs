//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// ARM MEM-AP (Memory Access Port) layer.
    /// Provides 32-bit, 16-bit, and block memory access through SWD.
    /// </summary>
    internal class ArmMemAp
    {
        private readonly SwdProtocol _swd;

        internal ArmMemAp(SwdProtocol swd)
        {
            _swd = swd ?? throw new ArgumentNullException(nameof(swd));
        }

        /// <summary>
        /// Reads a 32-bit value from a memory-mapped address.
        /// </summary>
        internal uint ReadWord(uint address)
        {
            _swd.WriteAp(SwdProtocol.ApCsw,
                SwdProtocol.CswSize32 | SwdProtocol.CswAddrinc_Off | SwdProtocol.CswDbgSwEnable);
            _swd.WriteAp(SwdProtocol.ApTar, address);

            return _swd.ReadAp(SwdProtocol.ApDrw);
        }

        /// <summary>
        /// Writes a 32-bit value to a memory-mapped address.
        /// </summary>
        internal void WriteWord(uint address, uint value)
        {
            _swd.WriteAp(SwdProtocol.ApCsw,
                SwdProtocol.CswSize32 | SwdProtocol.CswAddrinc_Off | SwdProtocol.CswDbgSwEnable);
            _swd.WriteAp(SwdProtocol.ApTar, address);
            _swd.WriteAp(SwdProtocol.ApDrw, value);
        }

        /// <summary>
        /// Reads a block of 32-bit words starting at the given address using auto-increment.
        /// </summary>
        /// <param name="address">Start address (must be 4-byte aligned).</param>
        /// <param name="wordCount">Number of 32-bit words to read.</param>
        /// <returns>Array of read values.</returns>
        internal uint[] ReadBlock(uint address, int wordCount)
        {
            if (wordCount <= 0)
            {
                return Array.Empty<uint>();
            }

            // Configure CSW for 32-bit, single auto-increment
            _swd.WriteAp(SwdProtocol.ApCsw,
                SwdProtocol.CswSize32 | SwdProtocol.CswAddrinc_Single | SwdProtocol.CswDbgSwEnable);

            uint[] result = new uint[wordCount];
            uint currentAddress = address;

            // TAR auto-increment wraps at 1KB boundary (ARM ADIv5 spec).
            // We need to re-set TAR every 1024 bytes (256 words).
            const int wordsPerWrap = 256;

            int remaining = wordCount;
            int offset = 0;

            while (remaining > 0)
            {
                // How many words until next 1KB wrap?
                int wordsUntilWrap = wordsPerWrap - (int)((currentAddress & 0x3FF) >> 2);
                int chunkSize = Math.Min(remaining, wordsUntilWrap);

                _swd.WriteAp(SwdProtocol.ApTar, currentAddress);

                // Read chunk word-by-word (CMSIS-DAP handles pipeline)
                for (int i = 0; i < chunkSize; i++)
                {
                    result[offset + i] = _swd.ReadAp(SwdProtocol.ApDrw);
                }

                offset += chunkSize;
                remaining -= chunkSize;
                currentAddress += (uint)(chunkSize * 4);
            }

            return result;
        }

        /// <summary>
        /// Writes a block of 32-bit words starting at the given address using auto-increment.
        /// </summary>
        /// <param name="address">Start address (must be 4-byte aligned).</param>
        /// <param name="data">Data words to write.</param>
        internal void WriteBlock(uint address, uint[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            // Configure CSW for 32-bit, single auto-increment
            _swd.WriteAp(SwdProtocol.ApCsw,
                SwdProtocol.CswSize32 | SwdProtocol.CswAddrinc_Single | SwdProtocol.CswDbgSwEnable);

            uint currentAddress = address;
            const int wordsPerWrap = 256;

            int remaining = data.Length;
            int offset = 0;

            while (remaining > 0)
            {
                int wordsUntilWrap = wordsPerWrap - (int)((currentAddress & 0x3FF) >> 2);
                int chunkSize = Math.Min(remaining, wordsUntilWrap);

                _swd.WriteAp(SwdProtocol.ApTar, currentAddress);

                for (int i = 0; i < chunkSize; i++)
                {
                    _swd.WriteAp(SwdProtocol.ApDrw, data[offset + i]);
                }

                offset += chunkSize;
                remaining -= chunkSize;
                currentAddress += (uint)(chunkSize * 4);
            }
        }

        /// <summary>
        /// Writes raw bytes to memory. Pads to 32-bit alignment if needed.
        /// </summary>
        /// <param name="address">Start address (should be 4-byte aligned).</param>
        /// <param name="data">Byte data to write.</param>
        /// <param name="dataOffset">Offset into data array.</param>
        /// <param name="length">Number of bytes to write.</param>
        internal void WriteBytes(uint address, byte[] data, int dataOffset, int length)
        {
            // Convert bytes to 32-bit words (little-endian, pad with 0xFF)
            int wordCount = (length + 3) / 4;
            uint[] words = new uint[wordCount];

            for (int i = 0; i < wordCount; i++)
            {
                uint word = 0xFFFFFFFF; // pad with 0xFF (erased flash value)
                int byteIndex = dataOffset + i * 4;

                for (int b = 0; b < 4 && (i * 4 + b) < length; b++)
                {
                    // Clear the byte position and set the data byte
                    word &= ~(0xFFU << (b * 8));
                    word |= (uint)data[byteIndex + b] << (b * 8);
                }

                words[i] = word;
            }

            WriteBlock(address, words);
        }

        /// <summary>
        /// Reads memory as raw bytes.
        /// </summary>
        /// <param name="address">Start address (should be 4-byte aligned).</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>Byte array of read data.</returns>
        internal byte[] ReadBytes(uint address, int length)
        {
            int wordCount = (length + 3) / 4;
            uint[] words = ReadBlock(address, wordCount);

            byte[] result = new byte[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = (byte)(words[i / 4] >> ((i % 4) * 8));
            }

            return result;
        }

        /// <summary>
        /// Polls a register until a condition is met or timeout expires.
        /// </summary>
        /// <param name="address">Register address.</param>
        /// <param name="mask">Bit mask to check.</param>
        /// <param name="expectedValue">Expected value after masking.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>True if condition was met before timeout.</returns>
        internal bool PollRegister(uint address, uint mask, uint expectedValue, int timeoutMs)
        {
            int elapsed = 0;

            while (elapsed < timeoutMs)
            {
                uint val = ReadWord(address);

                if ((val & mask) == expectedValue)
                {
                    return true;
                }

                System.Threading.Thread.Sleep(1);
                elapsed++;
            }

            return false;
        }
    }
}
