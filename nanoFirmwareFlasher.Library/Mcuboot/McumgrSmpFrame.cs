// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;

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
        private const int OpOffset        = 0;
        /// <summary>Byte offset of byte 1: protocol flags.</summary>
        private const int FlagsOffset     = 1;
        /// <summary>Byte offset of byte 2: payload length, high byte (BE).</summary>
        private const int PayloadLenHiOff = 2;
        /// <summary>Byte offset of byte 3: payload length, low byte (BE).</summary>
        private const int PayloadLenLoOff = 3;
        /// <summary>Byte offset of byte 4: management group, high byte (BE).</summary>
        private const int GroupHiOff      = 4;
        /// <summary>Byte offset of byte 5: management group, low byte (BE).</summary>
        private const int GroupLoOff      = 5;
        /// <summary>Byte offset of byte 6: sequence number.</summary>
        private const int SeqOffset       = 6;
        /// <summary>Byte offset of byte 7: command identifier.</summary>
        private const int CommandIdOffset = 7;

        /// <summary>3-bit mask for the op field in bits 2–0 of byte 0 (nmgr_hdr bitfield layout).</summary>
        private const int OpMask      = 0x07;
        /// <summary>SMP version 2 encoded in bits 4:3 of byte 0 (0b01 shifted left 3 = 0x08).</summary>
        private const byte SmpV2Version = 0x08;
        /// <summary>Shift to place/extract the high byte of a 16-bit big-endian field.</summary>
        private const int HighByteShift = 8;
        /// <summary>Mask to isolate the low byte of a 16-bit field.</summary>
        private const int LowByteMask  = 0xFF;

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
            op:            (SmpOpCode)(bytes[OpOffset] & OpMask),
            group:         (SmpGroup)(bytes[GroupHiOff] << HighByteShift | bytes[GroupLoOff]),
            seq:           bytes[SeqOffset],
            commandId:     bytes[CommandIdOffset],
            payloadLength: (ushort)(bytes[PayloadLenHiOff] << HighByteShift | bytes[PayloadLenLoOff]),
            flags:         bytes[FlagsOffset]);
    }

    /// <summary>
    /// An SMP (Simple Management Protocol) frame - a decoded header plus a raw CBOR payload -
    /// with methods for serial boot_serial framing.
    /// </summary>
    /// <remarks>
    /// Wire format per line:
    ///   First line:         0x06 0x09 + base64_chunk + \r\n
    ///   Continuation lines: 0x04 0x14 + base64_chunk + \r\n
    ///
    /// Lines are terminated with CR+LF: the MCUboot boot_serial decoder
    /// reads each line and base64-decodes (inlen - 2) bytes, i.e. it assumes a
    /// two-byte line terminator and strips it.
    ///
    /// The base64-encoded content is:
    ///   [2-byte BE length of (header + CBOR + CRC)] + [8-byte nmgr header] + [CBOR payload] + [2-byte CRC16-CCITT]
    ///
    /// CRC16-CCITT (poly 0x1021, init 0) covers the header + CBOR payload (NOT the 2-byte length prefix).
    /// </remarks>
    internal sealed class McumgrSmpFrame
    {
        /// <summary>
        /// Maximum base64 characters per boot_serial line.
        /// MCUBOOT_SERIAL_MAX_RECEIVE_SIZE=512 minus 2 marker bytes minus 2 CR+LF bytes = 508,
        /// which is exactly divisible by 4 (the base64 block size).
        /// </summary>
        private const int MaxLineChars      = 508;

        /// <summary>Bytes for the per-line framing marker (0x06 0x09 or 0x04 0x14).</summary>
        private const int FramingMarkerSize = 2;

        /// <summary>Bytes for the big-endian 16-bit total-length prefix.</summary>
        private const int LengthPrefixSize = 2;

        /// <summary>Bytes for the CRC16 trailer.</summary>
        private const int CrcSize          = 2;

        /// <summary>Minimum decodable raw frame size in bytes.</summary>
        private const int MinRawFrameSize  = LengthPrefixSize + CrcSize;

        /// <summary>Base64 encodes in 4-character blocks.</summary>
        private const int Base64PadBlock = 4;

        /// <summary>Shift to place/extract the high byte of a 16-bit big-endian field.</summary>
        private const int HighByteShift = 8;

        /// <summary>Mask to isolate the low byte of a 16-bit field.</summary>
        private const int LowByteMask   = 0xFF;

        /// <summary>Generator polynomial for CRC16-CCITT.</summary>
        private const uint CrcPolynomial = 0x1021;

        /// <summary>MSB of the 16-bit shift register.</summary>
        private const uint CrcMsbMask    = 0x8000;

        /// <summary>Keeps CRC arithmetic within 16 bits.</summary>
        private const uint Crc16Mask     = 0xFFFF;

        /// <summary>Decoded SMP header.</summary>
        public SmpHeader Header { get; }

        /// <summary>Raw CBOR payload bytes.</summary>
        public byte[] Payload { get; }

        /// <summary>
        /// Constructs a frame from a pre-built header and payload.
        /// The header's <see cref="SmpHeader.PayloadLength"/> must match <paramref name="payload"/> length.
        /// </summary>
        public McumgrSmpFrame(SmpHeader header, byte[] payload)
        {
            Header = header;
            Payload = payload ?? Array.Empty<byte>();
        }

        /// <summary>
        /// Constructs a frame, computing the <see cref="SmpHeader.PayloadLength"/> field automatically.
        /// </summary>
        public McumgrSmpFrame(
            SmpOpCode op,
            SmpGroup group,
            byte seq,
            byte commandId,
            byte[] payload)
        {
            Payload = payload ?? Array.Empty<byte>();
            Header = new SmpHeader(op, group, seq, commandId, (ushort)Payload.Length);
        }

        /// <summary>
        /// Encodes this frame into boot_serial line bytes ready to write to the serial port.
        /// </summary>
        public byte[] ToWireBytes()
        {
            byte[] headerBytes = Header.ToBytes();
            int innerLen = headerBytes.Length + Payload.Length;
            byte[] inner = new byte[innerLen];
            Buffer.BlockCopy(headerBytes, 0, inner, 0, headerBytes.Length);
            Buffer.BlockCopy(Payload, 0, inner, headerBytes.Length, Payload.Length);

            ushort crc = Crc16Ccitt(inner);

            // raw = [LengthPrefixSize-byte BE total] + inner + [CrcSize-byte CRC]
            int total = innerLen + CrcSize;
            byte[] raw = new byte[LengthPrefixSize + total];
            raw[0] = (byte)(total >> HighByteShift);
            raw[1] = (byte)(total & LowByteMask);
            Buffer.BlockCopy(inner, 0, raw, LengthPrefixSize, innerLen);
            raw[LengthPrefixSize + innerLen]     = (byte)(crc >> HighByteShift);
            raw[LengthPrefixSize + innerLen + 1] = (byte)(crc & LowByteMask);

            string b64 = Convert.ToBase64String(raw);
            var ms = new MemoryStream();

            for (int i = 0; i < b64.Length; i += MaxLineChars)
            {
                int chunkLen = Math.Min(MaxLineChars, b64.Length - i);

                if (i == 0)
                {
                    ms.WriteByte((byte)SmpFrameMarker.StartByte1);
                    ms.WriteByte((byte)SmpFrameMarker.StartByte2);
                }
                else
                {
                    ms.WriteByte((byte)SmpFrameMarker.ContinuationByte1);
                    ms.WriteByte((byte)SmpFrameMarker.ContinuationByte2);
                }

                byte[] chunk = Encoding.ASCII.GetBytes(b64.Substring(i, chunkLen));
                ms.Write(chunk, 0, chunk.Length);

                // CR+LF terminator: the boot_serial decoder strips a two-byte
                // line terminator (base64_decode of inlen - 2).
                ms.WriteByte((byte)'\r');
                ms.WriteByte((byte)'\n');
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Attempts to decode a received byte sequence into a frame.
        /// Returns <see langword="false"/> if the data is incomplete or corrupt.
        /// </summary>
        public static bool TryDecode(
            byte[] data,
            out McumgrSmpFrame frame)
        {
            frame = null;

            var b64 = new StringBuilder();
            int pos = 0;

            while (pos < data.Length)
            {
                bool isStart = pos + 1 < data.Length
                    && data[pos] == (byte)SmpFrameMarker.StartByte1
                    && data[pos + 1] == (byte)SmpFrameMarker.StartByte2;

                bool isCont = pos + 1 < data.Length
                    && data[pos] == (byte)SmpFrameMarker.ContinuationByte1
                    && data[pos + 1] == (byte)SmpFrameMarker.ContinuationByte2;

                if (isStart || isCont)
                {
                    pos += FramingMarkerSize;

                    while (pos < data.Length && data[pos] != (byte)'\n')
                    {
                        if (data[pos] != (byte)'\r')
                        {
                            b64.Append((char)data[pos]);
                        }

                        pos++;
                    }

                    if (pos < data.Length)
                    {
                        pos++;  // skip '\n'
                    }
                }
                else
                {
                    pos++;
                }
            }

            if (b64.Length == 0)
            {
                return false;
            }

            string b64str = b64.ToString();
            int pad = (Base64PadBlock - b64str.Length % Base64PadBlock) % Base64PadBlock;
            b64str += new string('=', pad);

            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(b64str);
            }
            catch
            {
                return false;
            }

            if (raw.Length < MinRawFrameSize)
            {
                return false;
            }

            int totalLen = raw[0] << HighByteShift | raw[1];

            if (raw.Length < LengthPrefixSize + totalLen)
            {
                return false;  // incomplete
            }

            int innerLen = totalLen - CrcSize;

            if (innerLen < SmpHeader.WireLength)
            {
                return false;
            }

            byte[] inner = new byte[innerLen];
            Buffer.BlockCopy(raw, LengthPrefixSize, inner, 0, innerLen);

            ushort expectedCrc = (ushort)(raw[LengthPrefixSize + innerLen] << HighByteShift | raw[LengthPrefixSize + innerLen + 1]);

            if (Crc16Ccitt(inner) != expectedCrc)
            {
                return false;
            }

            byte[] headerBytes = new byte[SmpHeader.WireLength];
            Buffer.BlockCopy(inner, 0, headerBytes, 0, SmpHeader.WireLength);

            byte[] payload = new byte[innerLen - SmpHeader.WireLength];
            Buffer.BlockCopy(inner, SmpHeader.WireLength, payload, 0, payload.Length);

            frame = new McumgrSmpFrame(SmpHeader.FromBytes(headerBytes), payload);
            return true;
        }

        /// <summary>
        /// Computes CRC16-CCITT (polynomial 0x1021, initial value 0) over <paramref name="data"/>.
        /// </summary>
        internal static ushort Crc16Ccitt(byte[] data)
        {
            uint crc = 0;

            foreach (byte b in data)
            {
                crc ^= (uint)b << HighByteShift;

                for (int i = 0; i < 8; i++)
                {
                    crc = (crc & CrcMsbMask) != 0
                        ? (crc << 1 ^ CrcPolynomial) & Crc16Mask
                        : crc << 1 & Crc16Mask;
                }
            }

            return (ushort)crc;
        }
    }
}
