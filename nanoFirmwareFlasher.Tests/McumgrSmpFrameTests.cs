// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Unit tests for <see cref="McumgrSmpFrame"/> and <see cref="SmpHeader"/>:
    /// CRC16-CCITT (§12.1), frame encode/decode round-trip (§12.2),
    /// corrupt-frame rejection (§12.3), multi-fragment frames (§12.4),
    /// and header construction/wire format (§12.8).
    /// </summary>
    [TestClass]
    public class McumgrSmpFrameTests
    {
        [TestMethod]
        public void Crc16Ccitt_EmptyInput_ReturnsZero()
        {
            ushort crc = McumgrSmpFrame.Crc16Ccitt(Array.Empty<byte>());
            Assert.AreEqual((ushort)0x0000, crc);
        }

        [TestMethod]
        public void Crc16Ccitt_Digits123456789_ReturnsXmodemCheckValue()
        {
            // CRC-16/XMODEM (poly 0x1021, init 0, no reflection): check = 0x31C3
            byte[] input = Encoding.ASCII.GetBytes("123456789");
            ushort crc = McumgrSmpFrame.Crc16Ccitt(input);
            Assert.AreEqual((ushort)0x31C3, crc, "XMODEM check value for '123456789'");
        }

        [TestMethod]
        public void Crc16Ccitt_SingleZeroByte_ReturnsZero()
        {
            // Any single byte XOR'd against 0 (init=0) then shifted 8 times with polynomial
            // applied only on set MSB: 0x00 => crc stays 0
            ushort crc = McumgrSmpFrame.Crc16Ccitt(new byte[] { 0x00 });
            Assert.AreEqual((ushort)0x0000, crc);
        }

        [TestMethod]
        public void Crc16Ccitt_KnownSequence_MatchesPrecalculated()
        {
            // { 0x06, 0x09 } - the start-of-frame marker bytes
            // pre-calculated: process 0x06 with init=0 → 0x0600 XOR'd, then 0x09
            byte[] input = new byte[] { 0x06, 0x09 };
            ushort crc = McumgrSmpFrame.Crc16Ccitt(input);

            // Verify against reference: independently compute using the same algorithm
            uint expected = 0;
            foreach (byte b in input)
            {
                expected ^= (uint)b << 8;
                for (int i = 0; i < 8; i++)
                    expected = (expected & 0x8000) != 0 ? ((expected << 1) ^ 0x1021) & 0xFFFF : (expected << 1) & 0xFFFF;
            }
            Assert.AreEqual((ushort)expected, crc);
        }

        [TestMethod]
        public void Crc16Ccitt_SameDataTwice_ProducesSameResult()
        {
            byte[] data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            ushort crc1 = McumgrSmpFrame.Crc16Ccitt(data);
            ushort crc2 = McumgrSmpFrame.Crc16Ccitt(data);
            Assert.AreEqual(crc1, crc2, "CRC must be deterministic");
        }

        [TestMethod]
        public void Crc16Ccitt_DifferentData_ProducesDifferentResult()
        {
            byte[] a = new byte[] { 0x01, 0x02, 0x03 };
            byte[] b = new byte[] { 0x01, 0x02, 0x04 };
            Assert.AreNotEqual(McumgrSmpFrame.Crc16Ccitt(a), McumgrSmpFrame.Crc16Ccitt(b));
        }

        [TestMethod]
        public void Header_ToBytes_OpWriteInLowBits()
        {
            // Byte 0 = SmpV2Version (0x08) | op.
            // Write = 2 → byte 0 = 0x08 | 0x02 = 0x0A.
            byte[] hdr = new SmpHeader(
                op: SmpOpCode.Write,
                group: SmpGroup.Os,
                seq: 0,
                commandId: (byte)OsCommandId.Echo,
                payloadLength: 0).ToBytes();

            Assert.AreEqual(0x0A, hdr[0], "SmpV2(0x08)|Write(0x02)=0x0A");
        }

        [TestMethod]
        public void Header_ToBytes_OpRead_CorrectByte0()
        {
            // Read = 0 → byte 0 = 0x08 | 0x00 = 0x08.
            byte[] hdr = new SmpHeader(
                op: SmpOpCode.Read,
                group: 0, seq: 0, commandId: 0, payloadLength: 0).ToBytes();

            Assert.AreEqual(0x08, hdr[0], "SmpV2(0x08)|Read(0x00)=0x08");
        }

        [TestMethod]
        public void Header_ToBytes_OpWriteRsp_CorrectByte0()
        {
            // WriteResponse = 3 → byte 0 = 0x08 | 0x03 = 0x0B.
            byte[] hdr = new SmpHeader(
                op: SmpOpCode.WriteResponse,
                group: 0, seq: 0, commandId: 0, payloadLength: 0).ToBytes();

            Assert.AreEqual(0x0B, hdr[0], "SmpV2(0x08)|WriteResponse(0x03)=0x0B");
        }

        [TestMethod]
        public void Header_ToBytes_SmpV2VersionBits_PresentInByte0ForAllOpcodes()
        {
            // SMP v2 encodes the version number in bits 4:3 of byte 0 (value = 0x08).
            // All opcodes must carry these bits; FromBytes must strip them via OpMask (0x07).
            const byte SmpV2Bits = 0x08;

            foreach (SmpOpCode op in new[] { SmpOpCode.Read, SmpOpCode.ReadResponse,
                                              SmpOpCode.Write, SmpOpCode.WriteResponse })
            {
                byte[] bytes = new SmpHeader(op, 0, 0, 0, 0).ToBytes();
                Assert.AreEqual(SmpV2Bits, (byte)(bytes[0] & 0xF8), $"SMP v2 bits must be set in byte 0 for op={op}");
                Assert.AreEqual((byte)op, (byte)(bytes[0] & 0x07), $"op bits must be preserved in bits 2:0 for op={op}");
            }
        }

        [TestMethod]
        public void Header_FromBytes_SmpV2VersionBits_StrippedWhenDecodingOp()
        {
            // A response from a device includes SMP v2 version bits; FromBytes must
            // extract only bits 2:0 as the Op code (OpMask = 0x07).
            byte[] raw = new byte[8];
            raw[0] = 0x0B; // SmpV2 (0x08) | WriteResponse (0x03)

            SmpHeader h = SmpHeader.FromBytes(raw);
            Assert.AreEqual(SmpOpCode.WriteResponse, h.Op, "FromBytes must strip SMP v2 bits and return op=WriteResponse");
        }

        [TestMethod]
        public void Header_ToBytes_PayloadLen_StoredBigEndianInBytes2And3()
        {
            ushort payloadLen = 0x0102;
            byte[] hdr = new SmpHeader(
                op: 0, group: 0, seq: 0, commandId: 0, payloadLength: payloadLen).ToBytes();

            Assert.AreEqual(0x01, hdr[2], "high byte of payloadLen");
            Assert.AreEqual(0x02, hdr[3], "low byte of payloadLen");
        }

        [TestMethod]
        public void Header_ToBytes_Group_StoredBigEndianInBytes4And5()
        {
            ushort group = 0x0040; // group 64 = nanoFramework custom
            byte[] hdr = new SmpHeader(
                op: 0, group: (SmpGroup)group, seq: 7, commandId: 0, payloadLength: 0).ToBytes();

            Assert.AreEqual(0x00, hdr[4], "high byte of group");
            Assert.AreEqual(0x40, hdr[5], "low byte of group");
        }

        [TestMethod]
        public void Header_ToBytes_Seq_StoredInByte6()
        {
            byte seq = 42;
            byte[] hdr = new SmpHeader(op: 0, group: 0, seq: seq, commandId: 0, payloadLength: 0).ToBytes();
            Assert.AreEqual(seq, hdr[6]);
        }

        [TestMethod]
        public void Header_ToBytes_CommandId_StoredInByte7()
        {
            byte id = (byte)ImageCommandId.Upload;
            byte[] hdr = new SmpHeader(op: 0, group: 0, seq: 0, commandId: id, payloadLength: 0).ToBytes();
            Assert.AreEqual(id, hdr[7]);
        }

        [TestMethod]
        public void Header_ToBytes_FlagsAlwaysZero()
        {
            byte[] hdr = new SmpHeader((SmpOpCode)3, (SmpGroup)0xFFFF, 0xFF, 0xFF, 0xFFFF).ToBytes();
            Assert.AreEqual(0x00, hdr[1], "flags byte must always be 0");
        }

        [TestMethod]
        public void Header_FromBytes_Op_RoundTrips_AllOpcodes()
        {
            foreach (SmpOpCode op in new[] { SmpOpCode.Read, SmpOpCode.ReadResponse,
                                              SmpOpCode.Write, SmpOpCode.WriteResponse })
            {
                byte[] hdrBytes = new SmpHeader(op, 0, 0, 0, 0).ToBytes();
                SmpHeader decoded = SmpHeader.FromBytes(hdrBytes);
                Assert.AreEqual(op, decoded.Op, $"op={op} round-trip via ToBytes/FromBytes");
            }
        }

        [TestMethod]
        public void Header_FromBytes_PayloadLen_RoundTrips()
        {
            ushort len = 0xABCD;
            byte[] hdrBytes = new SmpHeader(0, 0, 0, 0, len).ToBytes();
            Assert.AreEqual(len, SmpHeader.FromBytes(hdrBytes).PayloadLength);
        }

        [TestMethod]
        public void Header_FromBytes_AllFields_RoundTrip()
        {
            var original = new SmpHeader(SmpOpCode.WriteResponse, SmpGroup.Image, seq: 99, commandId: (byte)ImageCommandId.Upload, payloadLength: 512);
            SmpHeader decoded = SmpHeader.FromBytes(original.ToBytes());

            Assert.AreEqual(original.Op, decoded.Op);
            Assert.AreEqual(original.Group, decoded.Group);
            Assert.AreEqual(original.Seq, decoded.Seq);
            Assert.AreEqual(original.CommandId, decoded.CommandId);
            Assert.AreEqual(original.PayloadLength, decoded.PayloadLength);
            Assert.AreEqual(original.Flags, decoded.Flags);
        }

        [TestMethod]
        public void EncodeDecodeFrame_SmallPayload_HeaderAndPayloadRecoveredIntact()
        {
            var header  = new SmpHeader(SmpOpCode.Write, SmpGroup.Os, 1, (byte)OsCommandId.Echo, 5);
            byte[] payload = new byte[] { 0xA1, 0x61, 0x64, 0x65, 0x68 }; // {"d":"h"} CBOR

            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();
            bool ok = McumgrSmpFrame.TryDecode(wireBytes, out McumgrSmpFrame decoded);

            Assert.IsTrue(ok, "TryDecode must succeed for a valid frame");
            CollectionAssert.AreEqual(header.ToBytes(), decoded.Header.ToBytes(), "recovered header must match");
            CollectionAssert.AreEqual(payload, decoded.Payload, "recovered payload must match");
        }

        [TestMethod]
        public void EncodeDecodeFrame_EmptyPayload_RoundTrip()
        {
            var header  = new SmpHeader(SmpOpCode.Write, SmpGroup.Os, 0, (byte)OsCommandId.Reset, 0);
            byte[] payload = Array.Empty<byte>();

            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();
            bool ok = McumgrSmpFrame.TryDecode(wireBytes, out McumgrSmpFrame decoded);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(header.ToBytes(), decoded.Header.ToBytes());
            Assert.AreEqual(0, decoded.Payload.Length);
        }

        [TestMethod]
        public void EncodeDecodeFrame_BinaryPayload_AllByteValues_RoundTrip()
        {
            var header  = new SmpHeader(SmpOpCode.ReadResponse, SmpGroup.Image, 5, (byte)ImageCommandId.State, 256);
            // payload with all 256 byte values
            byte[] payload = new byte[256];
            for (int i = 0; i < 256; i++) payload[i] = (byte)i;

            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();
            bool ok = McumgrSmpFrame.TryDecode(wireBytes, out McumgrSmpFrame decoded);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(header.ToBytes(), decoded.Header.ToBytes());
            CollectionAssert.AreEqual(payload, decoded.Payload);
        }

        [TestMethod]
        public void ConvenienceConstructor_ComputesPayloadLengthInHeader()
        {
            byte[] payload = new byte[] { 0x01, 0x02, 0x03 };
            var frame = new McumgrSmpFrame(SmpOpCode.Write, SmpGroup.Os, 7, (byte)OsCommandId.Echo, payload);

            Assert.AreEqual((ushort)payload.Length, frame.Header.PayloadLength);
            Assert.AreEqual(SmpOpCode.Write, frame.Header.Op);
            Assert.AreEqual(SmpGroup.Os, frame.Header.Group);
            Assert.AreEqual((byte)7, frame.Header.Seq);
            Assert.AreEqual((byte)OsCommandId.Echo, frame.Header.CommandId);
        }

        [TestMethod]
        public void TryDecode_CorruptedPayloadByte_ReturnsFalse()
        {
            var header  = new SmpHeader(SmpOpCode.Write, SmpGroup.Os, 0, 0, 3);
            byte[] payload = new byte[] { 0x01, 0x02, 0x03 };
            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();

            // corrupt one byte in the middle of the frame - keep framing markers intact
            // find the base64 content area and flip a character
            int corruptAt = wireBytes.Length / 2;
            wireBytes[corruptAt] ^= 0x04; // flip bits in a base64 char (stays ASCII-printable range)

            bool ok = McumgrSmpFrame.TryDecode(wireBytes, out _);
            Assert.IsFalse(ok, "Corrupted frame must be rejected by CRC16 validation");
        }

        [TestMethod]
        public void TryDecode_EmptyInput_ReturnsFalse()
        {
            bool ok = McumgrSmpFrame.TryDecode(Array.Empty<byte>(), out _);
            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void TryDecode_RandomBytes_ReturnsFalse()
        {
            byte[] junk = Encoding.ASCII.GetBytes("not a valid smp frame at all\n");
            bool ok = McumgrSmpFrame.TryDecode(junk, out _);
            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void TryDecode_TruncatedFrame_ReturnsFalse()
        {
            var header  = new SmpHeader(SmpOpCode.Write, SmpGroup.Os, 0, 0, 4);
            byte[] payload = new byte[] { 0xA0, 0xB0, 0xC0, 0xD0 };
            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();

            // drop last 6 bytes (removes \r\n + 4 b64 chars, causing length mismatch)
            byte[] truncated = wireBytes.Take(wireBytes.Length - 6).ToArray();
            bool ok = McumgrSmpFrame.TryDecode(truncated, out _);
            Assert.IsFalse(ok, "Truncated frame must be rejected");
        }

        [TestMethod]
        public void EncodeDecodeFrame_PayloadRequiringTwoFragments_SplitsAndReassemblesCorrectly()
        {
            // A single boot_serial line carries at most 508 base64 chars = 381 raw bytes.
            // raw = 2 (len) + 8 (header) + payload + 2 (CRC), so a payload <= 369 stays on one
            // line. payload.Length = 400 → raw = 412 bytes → base64 = 552 chars → 2 fragments.
            var header  = new SmpHeader(SmpOpCode.Write, SmpGroup.Image, 0, (byte)ImageCommandId.Upload, 400);
            byte[] payload = Enumerable.Range(0, 400).Select(i => (byte)(i & 0xFF)).ToArray();

            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();

            // verify two fragment lines are present: one start marker and one continuation
            int startMarkers = CountMarker(wireBytes, (byte)SmpFrameMarker.StartByte1, (byte)SmpFrameMarker.StartByte2);
            int contMarkers  = CountMarker(wireBytes, (byte)SmpFrameMarker.ContinuationByte1, (byte)SmpFrameMarker.ContinuationByte2);
            Assert.AreEqual(1, startMarkers, "exactly one start-of-frame marker (0x06 0x09)");
            Assert.IsTrue(contMarkers >= 1, "at least one continuation marker (0x04 0x14)");

            // and it round-trips correctly
            bool ok = McumgrSmpFrame.TryDecode(wireBytes, out McumgrSmpFrame decoded);
            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(header.ToBytes(), decoded.Header.ToBytes());
            CollectionAssert.AreEqual(payload, decoded.Payload);
        }

        [TestMethod]
        public void EncodeDecodeFrame_LargePayloadRequiringManyFragments_RoundTrips()
        {
            // 512 byte payload → raw = 524 bytes → base64 = 700 chars → 6 lines
            var header  = new SmpHeader(SmpOpCode.Write, SmpGroup.Image, 1, (byte)ImageCommandId.Upload, 512);
            byte[] payload = Enumerable.Range(0, 512).Select(i => (byte)(i & 0xFF)).ToArray();

            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();
            bool ok = McumgrSmpFrame.TryDecode(wireBytes, out McumgrSmpFrame decoded);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(header.ToBytes(), decoded.Header.ToBytes());
            CollectionAssert.AreEqual(payload, decoded.Payload);
        }

        [TestMethod]
        public void EncodeFrame_SingleFragment_HasNoContMarker()
        {
            // payload.Length = 10 → raw = 22 bytes → base64 = 32 chars → fits in one line
            var header  = new SmpHeader(0, 0, 0, 0, 10);
            byte[] payload = new byte[10];

            byte[] wireBytes = new McumgrSmpFrame(header, payload).ToWireBytes();

            int contMarkers = CountMarker(wireBytes, (byte)SmpFrameMarker.ContinuationByte1, (byte)SmpFrameMarker.ContinuationByte2);
            Assert.AreEqual(0, contMarkers, "single-fragment frame must not contain continuation markers");
        }

        private static int CountMarker(byte[] data, byte m1, byte m2)
        {
            int count = 0;
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == m1 && data[i + 1] == m2)
                    count++;
            }
            return count;
        }
    }
}
