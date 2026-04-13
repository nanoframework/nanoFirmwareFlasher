// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Builds ESP32 bootloader command request packets with proper header, checksum, and SLIP framing.
    /// 
    /// Packet structure (Host → Device):
    ///   Byte 0:      Direction (0x00 = request)
    ///   Byte 1:      Command opcode
    ///   Bytes 2-3:   Data length (little-endian uint16)
    ///   Bytes 4-7:   Checksum (little-endian uint32, for data commands)
    ///   Bytes 8+:    Data payload
    /// </summary>
    internal static class Esp32CommandPacket
    {
        /// <summary>XOR seed value used for checksum calculation.</summary>
        internal const byte ChecksumSeed = 0xEF;

        /// <summary>Header size in bytes (direction + command + length + checksum).</summary>
        internal const int HeaderSize = 8;

        /// <summary>
        /// Build a raw (un-framed) command request packet.
        /// </summary>
        /// <param name="command">Command opcode.</param>
        /// <param name="data">Optional payload data.</param>
        /// <param name="checksum">Checksum value (0 for non-data commands).</param>
        /// <returns>The raw packet bytes (without SLIP framing).</returns>
        internal static byte[] BuildRaw(Esp32Command command, byte[] data = null, uint checksum = 0)
        {
            data ??= Array.Empty<byte>();

            byte[] packet = new byte[HeaderSize + data.Length];

            // Direction: 0x00 = request
            packet[0] = 0x00;

            // Command opcode
            packet[1] = (byte)command;

            // Data length (little-endian uint16)
            ushort dataLength = (ushort)data.Length;
            packet[2] = (byte)(dataLength & 0xFF);
            packet[3] = (byte)((dataLength >> 8) & 0xFF);

            // Checksum (little-endian uint32)
            packet[4] = (byte)(checksum & 0xFF);
            packet[5] = (byte)((checksum >> 8) & 0xFF);
            packet[6] = (byte)((checksum >> 16) & 0xFF);
            packet[7] = (byte)((checksum >> 24) & 0xFF);

            // Payload data
            if (data.Length > 0)
            {
                Buffer.BlockCopy(data, 0, packet, HeaderSize, data.Length);
            }

            return packet;
        }

        /// <summary>
        /// Build a SLIP-framed command request packet ready to send over serial.
        /// </summary>
        /// <param name="command">Command opcode.</param>
        /// <param name="data">Optional payload data.</param>
        /// <param name="checksum">Checksum value (0 for non-data commands).</param>
        /// <returns>SLIP-framed packet ready to send.</returns>
        internal static byte[] Build(Esp32Command command, byte[] data = null, uint checksum = 0)
        {
            byte[] raw = BuildRaw(command, data, checksum);
            return SlipFraming.Encode(raw);
        }

        /// <summary>
        /// Calculate the ESP32 bootloader checksum for a data payload.
        /// The checksum is the XOR of all data bytes, seeded with 0xEF.
        /// </summary>
        /// <param name="data">Data bytes to checksum.</param>
        /// <returns>The checksum value.</returns>
        internal static uint CalculateChecksum(byte[] data)
        {
            uint checksum = ChecksumSeed;

            for (int i = 0; i < data.Length; i++)
            {
                checksum ^= data[i];
            }

            return checksum;
        }

        /// <summary>
        /// Build a SYNC command packet.
        /// The sync payload is: 0x07, 0x07, 0x12, 0x20 followed by 32 bytes of 0x55.
        /// </summary>
        /// <returns>SLIP-framed SYNC packet ready to send.</returns>
        internal static byte[] BuildSync()
        {
            byte[] syncData = new byte[36];
            syncData[0] = 0x07;
            syncData[1] = 0x07;
            syncData[2] = 0x12;
            syncData[3] = 0x20;

            for (int i = 4; i < 36; i++)
            {
                syncData[i] = 0x55;
            }

            return Build(Esp32Command.Sync, syncData);
        }

        /// <summary>
        /// Build a READ_REG command packet.
        /// </summary>
        /// <param name="address">32-bit register address to read.</param>
        /// <returns>SLIP-framed READ_REG packet.</returns>
        internal static byte[] BuildReadReg(uint address)
        {
            byte[] data = new byte[4];
            data[0] = (byte)(address & 0xFF);
            data[1] = (byte)((address >> 8) & 0xFF);
            data[2] = (byte)((address >> 16) & 0xFF);
            data[3] = (byte)((address >> 24) & 0xFF);

            return Build(Esp32Command.ReadReg, data);
        }

        /// <summary>
        /// Build a WRITE_REG command packet.
        /// </summary>
        /// <param name="address">32-bit register address to write.</param>
        /// <param name="value">Value to write.</param>
        /// <param name="mask">Bit mask (default: 0xFFFFFFFF for all bits).</param>
        /// <param name="delayUs">Delay in microseconds after write (default: 0).</param>
        /// <returns>SLIP-framed WRITE_REG packet.</returns>
        internal static byte[] BuildWriteReg(uint address, uint value, uint mask = 0xFFFFFFFF, uint delayUs = 0)
        {
            byte[] data = new byte[16];

            WriteUInt32LE(data, 0, address);
            WriteUInt32LE(data, 4, value);
            WriteUInt32LE(data, 8, mask);
            WriteUInt32LE(data, 12, delayUs);

            return Build(Esp32Command.WriteReg, data);
        }

        /// <summary>
        /// Build a CHANGE_BAUDRATE command packet.
        /// </summary>
        /// <param name="newBaudRate">New baud rate to switch to.</param>
        /// <param name="oldBaudRate">Current baud rate (0 for ROM bootloader which ignores this).</param>
        /// <returns>SLIP-framed CHANGE_BAUDRATE packet.</returns>
        internal static byte[] BuildChangeBaudrate(int newBaudRate, int oldBaudRate = 0)
        {
            byte[] data = new byte[8];

            WriteUInt32LE(data, 0, (uint)newBaudRate);
            WriteUInt32LE(data, 4, (uint)oldBaudRate);

            return Build(Esp32Command.ChangeBaudrate, data);
        }

        /// <summary>
        /// Write a 32-bit unsigned integer in little-endian format to a byte array.
        /// </summary>
        internal static void WriteUInt32LE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>
        /// Read a 32-bit unsigned integer in little-endian format from a byte array.
        /// </summary>
        internal static uint ReadUInt32LE(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }
    }
}
