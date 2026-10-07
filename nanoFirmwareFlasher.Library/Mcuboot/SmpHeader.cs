// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Decoded 8-byte nmgr SMP header.
    /// </summary>
    internal readonly struct SmpHeader
    {
        /// <summary>Wire size of a header in bytes.</summary>
        internal const int WireLength = 8;

        /// <summary>Byte offset of byte 0: op (bits 2–0) | version (bits 4–3) | reserved (bits 7–5).</summary>
        private const int OpOffset = 0;

        /// <summary>Byte offset of byte 1: protocol flags.</summary>
        private const int FlagsOffset = 1;

        /// <summary>Byte offset of byte 2: payload length, high byte (BE).</summary>
        private const int PayloadLenHiOff = 2;

        /// <summary>Byte offset of byte 3: payload length, low byte (BE).</summary>
        private const int PayloadLenLoOff = 3;

        /// <summary>Byte offset of byte 4: management group, high byte (BE).</summary>
        private const int GroupHiOff = 4;

        /// <summary>Byte offset of byte 5: management group, low byte (BE).</summary>
        private const int GroupLoOff = 5;

        /// <summary>Byte offset of byte 6: sequence number.</summary>
        private const int SeqOffset = 6;

        /// <summary>Byte offset of byte 7: command identifier.</summary>
        private const int CommandIdOffset = 7;

        /// <summary>3-bit mask for the op field in bits 2–0 of byte 0 (nmgr_hdr bitfield layout).</summary>
        private const int OpMask = 0x07;

        /// <summary>SMP version 2 encoded in bits 4:3 of byte 0 (0b01 shifted left 3 = 0x08).</summary>
        private const byte SmpV2Version = 0x08;

        /// <summary>Shift to place/extract the high byte of a 16-bit big-endian field.</summary>
        private const int HighByteShift = 8;

        /// <summary>Mask to isolate the low byte of a 16-bit field.</summary>
        private const int LowByteMask = 0xFF;

        /// <summary>Operation code (bits 2–0 of byte 0).</summary>
        public SmpOpCode Op { get; }

        /// <summary>Protocol flags (byte 1; always 0 in current SMP version).</summary>
        public byte Flags { get; }

        /// <summary>Length of the CBOR payload that follows the header, in bytes.</summary>
        public ushort PayloadLength { get; }

        /// <summary>Management group (bytes 4–5, big-endian).</summary>
        public SmpGroup Group { get; }

        /// <summary>Sequence number used to match requests to responses (byte 6).</summary>
        public byte Seq { get; }

        /// <summary>Command identifier within the group (byte 7).</summary>
        public byte CommandId { get; }

        /// <param name="op">SMP operation code.</param>
        /// <param name="group">Management group.</param>
        /// <param name="seq">Sequence number.</param>
        /// <param name="commandId">Command identifier.</param>
        /// <param name="payloadLength">CBOR payload length in bytes.</param>
        /// <param name="flags">Protocol flags byte (defaults to 0).</param>
        public SmpHeader(
            SmpOpCode op,
            SmpGroup group,
            byte seq,
            byte commandId,
            ushort payloadLength,
            byte flags = 0)
        {
            Op = op;
            Flags = flags;
            Group = group;
            Seq = seq;
            CommandId = commandId;
            PayloadLength = payloadLength;
        }

        /// <summary>
        /// Serialises this header to the 8-byte wire format.
        /// </summary>
        internal byte[] ToBytes()
        {
            byte[] bytes =
            [
                (byte)(SmpV2Version | (byte)Op),
                Flags,
                (byte)(PayloadLength >> HighByteShift),
                (byte)(PayloadLength & LowByteMask),
                (byte)((ushort)Group >> HighByteShift),
                (byte)((ushort)Group & LowByteMask),
                Seq,
                CommandId,
            ];

            return bytes;
        }

        /// <summary>
        /// Parses a header from 8 raw wire bytes.
        /// </summary>
        internal static SmpHeader FromBytes(byte[] bytes) => new(
            op: (SmpOpCode)(bytes[OpOffset] & OpMask),
            group: (SmpGroup)(bytes[GroupHiOff] << HighByteShift | bytes[GroupLoOff]),
            seq: bytes[SeqOffset],
            commandId: bytes[CommandIdOffset],
            payloadLength: (ushort)(bytes[PayloadLenHiOff] << HighByteShift | bytes[PayloadLenLoOff]),
            flags: bytes[FlagsOffset]);
    }
}
