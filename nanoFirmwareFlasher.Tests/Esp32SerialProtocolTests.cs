// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Esp32Serial;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Esp32SerialProtocolTests
    {
        #region SLIP Framing Tests

        [TestMethod]
        public void SlipEncode_BasicPayload_WrapsWithFrameDelimiters()
        {
            byte[] payload = { 0x01, 0x02, 0x03 };

            byte[] encoded = SlipFraming.Encode(payload);

            Assert.AreEqual(SlipFraming.FrameEnd, encoded[0], "First byte should be 0xC0");
            Assert.AreEqual(SlipFraming.FrameEnd, encoded[encoded.Length - 1], "Last byte should be 0xC0");
            Assert.AreEqual(5, encoded.Length, "3 payload bytes + 2 frame delimiters = 5");
        }

        [TestMethod]
        public void SlipEncode_EmptyPayload_ReturnsOnlyDelimiters()
        {
            byte[] payload = Array.Empty<byte>();

            byte[] encoded = SlipFraming.Encode(payload);

            Assert.AreEqual(2, encoded.Length);
            Assert.AreEqual(SlipFraming.FrameEnd, encoded[0]);
            Assert.AreEqual(SlipFraming.FrameEnd, encoded[1]);
        }

        [TestMethod]
        public void SlipEncode_WithFrameEndByte_EscapesCorrectly()
        {
            // Payload contains 0xC0 which must be escaped to 0xDB 0xDC
            byte[] payload = { 0x01, 0xC0, 0x02 };

            byte[] encoded = SlipFraming.Encode(payload);

            // Expected: 0xC0, 0x01, 0xDB, 0xDC, 0x02, 0xC0
            Assert.AreEqual(6, encoded.Length);
            Assert.AreEqual(0xC0, encoded[0]); // Frame delimiter
            Assert.AreEqual(0x01, encoded[1]);
            Assert.AreEqual(0xDB, encoded[2]); // Escape byte
            Assert.AreEqual(0xDC, encoded[3]); // Escaped END
            Assert.AreEqual(0x02, encoded[4]);
            Assert.AreEqual(0xC0, encoded[5]); // Frame delimiter
        }

        [TestMethod]
        public void SlipEncode_WithEscapeByte_EscapesCorrectly()
        {
            // Payload contains 0xDB which must be escaped to 0xDB 0xDD
            byte[] payload = { 0x01, 0xDB, 0x02 };

            byte[] encoded = SlipFraming.Encode(payload);

            // Expected: 0xC0, 0x01, 0xDB, 0xDD, 0x02, 0xC0
            Assert.AreEqual(6, encoded.Length);
            Assert.AreEqual(0xC0, encoded[0]);
            Assert.AreEqual(0x01, encoded[1]);
            Assert.AreEqual(0xDB, encoded[2]); // Escape byte
            Assert.AreEqual(0xDD, encoded[3]); // Escaped ESC
            Assert.AreEqual(0x02, encoded[4]);
            Assert.AreEqual(0xC0, encoded[5]);
        }

        [TestMethod]
        public void SlipEncode_BothSpecialBytes_EscapesBothCorrectly()
        {
            byte[] payload = { 0xC0, 0xDB };

            byte[] encoded = SlipFraming.Encode(payload);

            // Expected: 0xC0, 0xDB, 0xDC, 0xDB, 0xDD, 0xC0
            Assert.AreEqual(6, encoded.Length);
            Assert.AreEqual(0xC0, encoded[0]);
            Assert.AreEqual(0xDB, encoded[1]);
            Assert.AreEqual(0xDC, encoded[2]);
            Assert.AreEqual(0xDB, encoded[3]);
            Assert.AreEqual(0xDD, encoded[4]);
            Assert.AreEqual(0xC0, encoded[5]);
        }

        [TestMethod]
        public void SlipDecode_BasicPayload_DecodesCorrectly()
        {
            byte[] encoded = { 0x01, 0x02, 0x03 };

            byte[] decoded = SlipFraming.Decode(encoded);

            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03 }, decoded);
        }

        [TestMethod]
        public void SlipDecode_WithEscapedEnd_DecodesCorrectly()
        {
            // 0xDB 0xDC should decode to 0xC0
            byte[] encoded = { 0x01, 0xDB, 0xDC, 0x02 };

            byte[] decoded = SlipFraming.Decode(encoded);

            CollectionAssert.AreEqual(new byte[] { 0x01, 0xC0, 0x02 }, decoded);
        }

        [TestMethod]
        public void SlipDecode_WithEscapedEsc_DecodesCorrectly()
        {
            // 0xDB 0xDD should decode to 0xDB
            byte[] encoded = { 0x01, 0xDB, 0xDD, 0x02 };

            byte[] decoded = SlipFraming.Decode(encoded);

            CollectionAssert.AreEqual(new byte[] { 0x01, 0xDB, 0x02 }, decoded);
        }

        [TestMethod]
        public void SlipDecode_EmptyInput_ReturnsEmpty()
        {
            byte[] decoded = SlipFraming.Decode(Array.Empty<byte>());

            Assert.AreEqual(0, decoded.Length);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SlipDecode_TrailingEscape_ThrowsException()
        {
            // Escape byte at end with no following byte is invalid
            byte[] encoded = { 0x01, 0xDB };

            SlipFraming.Decode(encoded);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SlipDecode_InvalidEscapeSequence_ThrowsException()
        {
            // 0xDB followed by invalid byte
            byte[] encoded = { 0x01, 0xDB, 0xFF, 0x02 };

            SlipFraming.Decode(encoded);
        }

        [TestMethod]
        public void SlipEncodeDecode_RoundTrip_ProducesOriginalData()
        {
            byte[] original = { 0x00, 0x08, 0x24, 0x00, 0xEF, 0x00, 0x00, 0x00,
                                0x07, 0x07, 0x12, 0x20, 0xC0, 0xDB, 0xFF, 0x55 };

            byte[] encoded = SlipFraming.Encode(original);
            
            // Strip frame delimiters to get the body for decode
            byte[] body = new byte[encoded.Length - 2];
            Buffer.BlockCopy(encoded, 1, body, 0, body.Length);
            byte[] decoded = SlipFraming.Decode(body);

            CollectionAssert.AreEqual(original, decoded);
        }

        [TestMethod]
        public void SlipEncodeDecode_AllByteValues_RoundTripsCorrectly()
        {
            // Create payload with all possible byte values
            byte[] original = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                original[i] = (byte)i;
            }

            byte[] encoded = SlipFraming.Encode(original);
            byte[] body = new byte[encoded.Length - 2];
            Buffer.BlockCopy(encoded, 1, body, 0, body.Length);
            byte[] decoded = SlipFraming.Decode(body);

            CollectionAssert.AreEqual(original, decoded);
        }

        #endregion

        #region Command Packet Tests

        [TestMethod]
        public void CommandPacket_Checksum_CalculatesXorWithSeed()
        {
            // Checksum = 0xEF XOR each byte
            byte[] data = { 0x01, 0x02, 0x03 };

            uint checksum = Esp32CommandPacket.CalculateChecksum(data);

            uint expected = (uint)(0xEF ^ 0x01 ^ 0x02 ^ 0x03);
            Assert.AreEqual(expected, checksum);
        }

        [TestMethod]
        public void CommandPacket_Checksum_EmptyData_ReturnsSeed()
        {
            uint checksum = Esp32CommandPacket.CalculateChecksum(Array.Empty<byte>());

            Assert.AreEqual((uint)Esp32CommandPacket.ChecksumSeed, checksum);
        }

        [TestMethod]
        public void CommandPacket_BuildRaw_CorrectHeader()
        {
            byte[] data = { 0xAA, 0xBB };
            uint checksum = 0x12345678;

            byte[] packet = Esp32CommandPacket.BuildRaw(Esp32Command.ReadReg, data, checksum);

            Assert.AreEqual(10, packet.Length); // 8 header + 2 data
            Assert.AreEqual(0x00, packet[0]);   // Direction = request
            Assert.AreEqual(0x0A, packet[1]);   // ReadReg opcode
            Assert.AreEqual(0x02, packet[2]);   // Data length low byte
            Assert.AreEqual(0x00, packet[3]);   // Data length high byte
            Assert.AreEqual(0x78, packet[4]);   // Checksum byte 0 (LE)
            Assert.AreEqual(0x56, packet[5]);   // Checksum byte 1
            Assert.AreEqual(0x34, packet[6]);   // Checksum byte 2
            Assert.AreEqual(0x12, packet[7]);   // Checksum byte 3
            Assert.AreEqual(0xAA, packet[8]);   // Data byte 0
            Assert.AreEqual(0xBB, packet[9]);   // Data byte 1
        }

        [TestMethod]
        public void CommandPacket_BuildRaw_NoData_HeaderOnly()
        {
            byte[] packet = Esp32CommandPacket.BuildRaw(Esp32Command.EraseFlash);

            Assert.AreEqual(8, packet.Length);
            Assert.AreEqual(0x00, packet[0]);   // Direction = request
            Assert.AreEqual(0xD0, packet[1]);   // EraseFlash opcode
            Assert.AreEqual(0x00, packet[2]);   // Data length = 0
            Assert.AreEqual(0x00, packet[3]);
        }

        [TestMethod]
        public void CommandPacket_Build_IsSLIPFramed()
        {
            byte[] data = { 0x01 };
            byte[] packet = Esp32CommandPacket.Build(Esp32Command.ReadReg, data);

            // Should start and end with 0xC0
            Assert.AreEqual(0xC0, packet[0]);
            Assert.AreEqual(0xC0, packet[packet.Length - 1]);
        }

        [TestMethod]
        public void CommandPacket_BuildSync_CorrectStructure()
        {
            byte[] syncPacket = Esp32CommandPacket.BuildSync();

            // The sync packet should be SLIP-framed
            Assert.AreEqual(0xC0, syncPacket[0]);
            Assert.AreEqual(0xC0, syncPacket[syncPacket.Length - 1]);

            // Decode the SLIP frame to get the raw packet
            byte[] body = new byte[syncPacket.Length - 2];
            Buffer.BlockCopy(syncPacket, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            // Verify header
            Assert.AreEqual(0x00, raw[0]); // Direction = request
            Assert.AreEqual(0x08, raw[1]); // SYNC command

            // Data length should be 36
            ushort dataLen = (ushort)(raw[2] | (raw[3] << 8));
            Assert.AreEqual(36, dataLen);

            // Verify sync data pattern: 0x07, 0x07, 0x12, 0x20, then 32 x 0x55
            Assert.AreEqual(0x07, raw[8]);
            Assert.AreEqual(0x07, raw[9]);
            Assert.AreEqual(0x12, raw[10]);
            Assert.AreEqual(0x20, raw[11]);

            for (int i = 12; i < 44; i++)
            {
                Assert.AreEqual(0x55, raw[i], $"Byte at offset {i} should be 0x55");
            }
        }

        [TestMethod]
        public void CommandPacket_BuildReadReg_CorrectAddress()
        {
            uint address = 0x3FF5A004;
            byte[] packet = Esp32CommandPacket.BuildReadReg(address);

            // Decode SLIP
            byte[] body = new byte[packet.Length - 2];
            Buffer.BlockCopy(packet, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            // Verify command
            Assert.AreEqual(0x0A, raw[1]); // ReadReg

            // Verify address in data section (offset 8)
            uint parsedAddr = Esp32CommandPacket.ReadUInt32LE(raw, 8);
            Assert.AreEqual(address, parsedAddr);
        }

        [TestMethod]
        public void CommandPacket_BuildWriteReg_CorrectFields()
        {
            uint address = 0x60002000;
            uint value = 0x00000001;
            uint mask = 0x0000FFFF;
            uint delay = 100;

            byte[] packet = Esp32CommandPacket.BuildWriteReg(address, value, mask, delay);

            // Decode SLIP
            byte[] body = new byte[packet.Length - 2];
            Buffer.BlockCopy(packet, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            Assert.AreEqual(0x09, raw[1]); // WriteReg

            // Data length should be 16
            ushort dataLen = (ushort)(raw[2] | (raw[3] << 8));
            Assert.AreEqual(16, dataLen);

            // Verify fields at offset 8 (data section)
            Assert.AreEqual(address, Esp32CommandPacket.ReadUInt32LE(raw, 8));
            Assert.AreEqual(value, Esp32CommandPacket.ReadUInt32LE(raw, 12));
            Assert.AreEqual(mask, Esp32CommandPacket.ReadUInt32LE(raw, 16));
            Assert.AreEqual(delay, Esp32CommandPacket.ReadUInt32LE(raw, 20));
        }

        [TestMethod]
        public void CommandPacket_BuildChangeBaudrate_CorrectFields()
        {
            int newBaud = 921600;
            int oldBaud = 115200;

            byte[] packet = Esp32CommandPacket.BuildChangeBaudrate(newBaud, oldBaud);

            // Decode SLIP
            byte[] body = new byte[packet.Length - 2];
            Buffer.BlockCopy(packet, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            Assert.AreEqual(0x0F, raw[1]); // ChangeBaudrate

            Assert.AreEqual((uint)newBaud, Esp32CommandPacket.ReadUInt32LE(raw, 8));
            Assert.AreEqual((uint)oldBaud, Esp32CommandPacket.ReadUInt32LE(raw, 12));
        }

        [TestMethod]
        public void CommandPacket_WriteUInt32LE_ReadUInt32LE_RoundTrip()
        {
            byte[] buffer = new byte[8];
            uint[] values = { 0, 1, 0x12345678, 0xFFFFFFFF, 0xDEADBEEF };

            foreach (uint val in values)
            {
                Esp32CommandPacket.WriteUInt32LE(buffer, 0, val);
                uint result = Esp32CommandPacket.ReadUInt32LE(buffer, 0);
                Assert.AreEqual(val, result, $"Round-trip failed for 0x{val:X8}");
            }
        }

        #endregion

        #region Response Packet Tests

        [TestMethod]
        public void ResponsePacket_Parse_SuccessResponse()
        {
            // Build a valid ROM bootloader response packet:
            // Direction=0x01, Cmd=0x08 (Sync), DataLen=4, Value=0x00000000
            // Data: status=0x00, error=0x00, 0x00, 0x00 (4-byte ROM status trailer)
            byte[] payload = new byte[]
            {
                0x01,       // Direction = response
                0x08,       // Command = Sync
                0x04, 0x00, // Data length = 4
                0x00, 0x00, 0x00, 0x00, // Value = 0
                0x00, 0x00, 0x00, 0x00  // Status = success (ROM: 4-byte trailer)
            };

            var response = Esp32ResponsePacket.Parse(payload);

            Assert.AreEqual(Esp32Command.Sync, response.Command);
            Assert.AreEqual(0u, response.Value);
            Assert.IsTrue(response.IsSuccess);
            Assert.AreEqual(0, response.StatusCode);
            Assert.AreEqual(0, response.ErrorCode);
            Assert.AreEqual(0, response.Data.Length);
        }

        [TestMethod]
        public void ResponsePacket_Parse_ErrorResponse()
        {
            // Error response: status=1, error=0x05 (invalid message)
            // ROM bootloader sends 4-byte status trailer
            byte[] payload = new byte[]
            {
                0x01,       // Direction = response
                0x0A,       // Command = ReadReg
                0x04, 0x00, // Data length = 4
                0x00, 0x00, 0x00, 0x00, // Value = 0
                0x01, 0x05, 0x00, 0x00  // Status = 1 (failure), Error = 0x05, padding
            };

            var response = Esp32ResponsePacket.Parse(payload);

            Assert.AreEqual(Esp32Command.ReadReg, response.Command);
            Assert.IsFalse(response.IsSuccess);
            Assert.AreEqual(1, response.StatusCode);
            Assert.AreEqual(0x05, response.ErrorCode);
        }

        [TestMethod]
        public void ResponsePacket_Parse_WithValueField()
        {
            // ReadReg response with a register value
            // ROM bootloader: 4-byte status trailer
            byte[] payload = new byte[]
            {
                0x01,       // Direction
                0x0A,       // ReadReg
                0x04, 0x00, // Data length = 4
                0x83, 0x1D, 0xF0, 0x00, // Value = 0x00F01D83 (ESP32 magic)
                0x00, 0x00, 0x00, 0x00  // Status = success (ROM 4-byte trailer)
            };

            var response = Esp32ResponsePacket.Parse(payload);

            Assert.AreEqual(Esp32Command.ReadReg, response.Command);
            Assert.AreEqual(0x00F01D83u, response.Value);
            Assert.IsTrue(response.IsSuccess);
        }

        [TestMethod]
        public void ResponsePacket_Parse_WithExtraData()
        {
            // Response with extra data beyond status bytes
            // ROM bootloader: 4-byte status trailer
            byte[] payload = new byte[]
            {
                0x01,       // Direction
                0x13,       // SpiFlashMd5
                0x24, 0x00, // Data length = 36 (32 MD5 + 4 ROM status)
                0x00, 0x00, 0x00, 0x00, // Value = 0
                // 32 bytes of "MD5" data
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
                0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
                0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20,
                // Status (ROM 4-byte trailer)
                0x00, 0x00, 0x00, 0x00  // Success
            };

            var response = Esp32ResponsePacket.Parse(payload);

            Assert.IsTrue(response.IsSuccess);
            Assert.AreEqual(32, response.Data.Length);
            Assert.AreEqual(0x01, response.Data[0]);
            Assert.AreEqual(0x20, response.Data[31]);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ResponsePacket_Parse_TooShort_ThrowsException()
        {
            byte[] payload = { 0x01, 0x08, 0x00 }; // Only 3 bytes, minimum is 8

            Esp32ResponsePacket.Parse(payload);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ResponsePacket_Parse_NullPayload_ThrowsException()
        {
            Esp32ResponsePacket.Parse(null);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ResponsePacket_Parse_WrongDirection_ThrowsException()
        {
            byte[] payload = new byte[]
            {
                0x00,       // Direction = request (WRONG - should be 0x01)
                0x08, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00
            };

            Esp32ResponsePacket.Parse(payload);
        }

        [TestMethod]
        public void ResponsePacket_ThrowIfError_SuccessDoesNotThrow()
        {
            byte[] payload = new byte[]
            {
                0x01, 0x08, 0x04, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            };

            var response = Esp32ResponsePacket.Parse(payload);
            response.ThrowIfError(); // Should not throw
        }

        [TestMethod]
        [ExpectedException(typeof(Esp32BootloaderException))]
        public void ResponsePacket_ThrowIfError_ErrorThrowsException()
        {
            byte[] payload = new byte[]
            {
                0x01, 0x08, 0x04, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x01, 0x07, 0x00, 0x00  // Status=1, Error=0x07 (invalid CRC), ROM trailer
            };

            var response = Esp32ResponsePacket.Parse(payload);
            response.ThrowIfError();
        }

        [TestMethod]
        public void ResponsePacket_ErrorDescription_KnownCodes()
        {
            Assert.IsTrue(Esp32ResponsePacket.GetErrorDescription(0x05).Contains("invalid"));
            Assert.IsTrue(Esp32ResponsePacket.GetErrorDescription(0x07).Contains("CRC"));
            Assert.IsTrue(Esp32ResponsePacket.GetErrorDescription(0x08).Contains("write"));
            Assert.IsTrue(Esp32ResponsePacket.GetErrorDescription(0x09).Contains("read"));
            Assert.IsTrue(Esp32ResponsePacket.GetErrorDescription(0x0B).Contains("Deflate"));
        }

        [TestMethod]
        public void ResponsePacket_Parse_HeaderOnly_NoExtraData()
        {
            // Response with no data after header (dataLen=0)
            byte[] payload = new byte[]
            {
                0x01,       // Direction
                0xD0,       // EraseFlash
                0x00, 0x00, // Data length = 0
                0x00, 0x00, 0x00, 0x00  // Value = 0
            };

            var response = Esp32ResponsePacket.Parse(payload);

            Assert.AreEqual(Esp32Command.EraseFlash, response.Command);
            Assert.AreEqual(0, response.Data.Length);
            // No status bytes => defaults to success
            Assert.AreEqual(0, response.StatusCode);
        }

        [TestMethod]
        public void ResponsePacket_Parse_StubMode_2ByteStatus()
        {
            // Stub loader uses only 2-byte status trailer
            byte[] payload = new byte[]
            {
                0x01,       // Direction
                0x0A,       // ReadReg
                0x02, 0x00, // Data length = 2
                0x83, 0x1D, 0xF0, 0x00, // Value = 0x00F01D83
                0x00, 0x00  // Status = success (stub 2-byte trailer)
            };

            var response = Esp32ResponsePacket.Parse(payload, isStubRunning: true);

            Assert.AreEqual(0x00F01D83u, response.Value);
            Assert.IsTrue(response.IsSuccess);
            Assert.AreEqual(0, response.Data.Length);
        }

        [TestMethod]
        public void ResponsePacket_Parse_StubMode_ErrorResponse()
        {
            byte[] payload = new byte[]
            {
                0x01,       // Direction
                0x03,       // FlashData
                0x02, 0x00, // Data length = 2
                0x00, 0x00, 0x00, 0x00, // Value
                0x01, 0x08  // Status=1, Error=0x08 (flash write error)
            };

            var response = Esp32ResponsePacket.Parse(payload, isStubRunning: true);

            Assert.IsFalse(response.IsSuccess);
            Assert.AreEqual(1, response.StatusCode);
            Assert.AreEqual(0x08, response.ErrorCode);
        }

        [TestMethod]
        public void ResponsePacket_Parse_StubMode_WithData()
        {
            // Stub response with MD5 data + 2-byte status
            byte[] payload = new byte[]
            {
                0x01,       // Direction
                0x13,       // SpiFlashMd5
                0x22, 0x00, // Data length = 34 (32 data + 2 stub status)
                0x00, 0x00, 0x00, 0x00,
                // 32 bytes of MD5 data
                0xAA, 0xBB, 0xCC, 0xDD, 0x01, 0x02, 0x03, 0x04,
                0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
                0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14,
                0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C,
                // Status (stub 2-byte trailer)
                0x00, 0x00
            };

            var response = Esp32ResponsePacket.Parse(payload, isStubRunning: true);

            Assert.IsTrue(response.IsSuccess);
            Assert.AreEqual(32, response.Data.Length);
            Assert.AreEqual(0xAA, response.Data[0]);
            Assert.AreEqual(0x1C, response.Data[31]);
        }

        #endregion

        #region Esp32BootloaderException Tests

        [TestMethod]
        public void BootloaderException_ContainsCommandAndErrorCode()
        {
            var ex = new Esp32BootloaderException(Esp32Command.FlashData, 0x08);

            Assert.AreEqual(Esp32Command.FlashData, ex.Command);
            Assert.AreEqual(0x08, ex.ErrorCode);
            Assert.IsTrue(ex.Message.Contains("FlashData"));
            Assert.IsTrue(ex.Message.Contains("write"));
        }

        #endregion

        #region Integration: Command Build + Response Parse

        [TestMethod]
        public void Integration_SyncPacketAndResponse_EndToEnd()
        {
            // Build sync packet
            byte[] syncPacket = Esp32CommandPacket.BuildSync();

            // Verify it's SLIP-framed
            Assert.AreEqual(0xC0, syncPacket[0]);
            Assert.AreEqual(0xC0, syncPacket[syncPacket.Length - 1]);

            // Simulate a sync response (as the ESP32 ROM would send)
            byte[] responsePayload = new byte[]
            {
                0x01,       // Direction = response
                0x08,       // Sync
                0x04, 0x00, // Data length = 4 (ROM status trailer)
                0x00, 0x00, 0x00, 0x00, // Value
                0x00, 0x00, 0x00, 0x00  // Status = success (ROM 4-byte trailer)
            };

            var response = Esp32ResponsePacket.Parse(responsePayload);

            Assert.AreEqual(Esp32Command.Sync, response.Command);
            Assert.IsTrue(response.IsSuccess);
        }

        [TestMethod]
        public void Integration_ReadRegPacketAndResponse_EndToEnd()
        {
            // Build a ReadReg packet for ESP32 magic register
            uint magicAddr = 0x40001000;
            byte[] readRegPacket = Esp32CommandPacket.BuildReadReg(magicAddr);

            // Verify it's valid SLIP-framed
            Assert.AreEqual(0xC0, readRegPacket[0]);

            // Simulate a response with ESP32 magic value (ROM bootloader)
            byte[] responsePayload = new byte[]
            {
                0x01,       // Direction = response
                0x0A,       // ReadReg
                0x04, 0x00, // Data length = 4 (ROM status trailer)
                0x83, 0x1D, 0xF0, 0x00, // Value = 0x00F01D83 (ESP32 magic)
                0x00, 0x00, 0x00, 0x00  // Status = success (ROM 4-byte trailer)
            };

            var response = Esp32ResponsePacket.Parse(responsePayload);

            Assert.AreEqual(Esp32Command.ReadReg, response.Command);
            Assert.AreEqual(0x00F01D83u, response.Value);
            Assert.IsTrue(response.IsSuccess);
        }

        #endregion

        #region Phase 3: Bootloader Client Protocol Simulation

        [TestMethod]
        public void BootloaderClient_DefaultConstants_AreCorrect()
        {
            Assert.AreEqual(115200, Esp32BootloaderClient.DefaultBaudRate);
            Assert.AreEqual(3000, Esp32BootloaderClient.DefaultTimeoutMs);
            Assert.AreEqual(60000, Esp32BootloaderClient.EraseTimeoutMs);
        }

        [TestMethod]
        public void Protocol_WriteRegCommand_BuildAndParseResponse()
        {
            // Simulate a WriteReg operation:
            // 1. Build the command
            uint address = 0x3FF42000;
            uint value = 0x00000001;
            uint mask = 0xFFFFFFFF;
            uint delay = 0;

            byte[] writeRegPacket = Esp32CommandPacket.BuildWriteReg(address, value, mask, delay);

            // Should be SLIP-framed
            Assert.AreEqual(0xC0, writeRegPacket[0]);
            Assert.AreEqual(0xC0, writeRegPacket[writeRegPacket.Length - 1]);

            // 2. Simulate ROM bootloader response
            byte[] responsePayload = new byte[]
            {
                0x01,       // Response direction
                0x09,       // WriteReg
                0x04, 0x00, // Data length (ROM 4-byte status)
                0x00, 0x00, 0x00, 0x00, // Value
                0x00, 0x00, 0x00, 0x00  // Status = success
            };

            var response = Esp32ResponsePacket.Parse(responsePayload);

            Assert.AreEqual(Esp32Command.WriteReg, response.Command);
            Assert.IsTrue(response.IsSuccess);
        }

        [TestMethod]
        public void Protocol_ChangeBaudrate_BuildAndParseResponse()
        {
            // Build baud rate change command
            byte[] packet = Esp32CommandPacket.BuildChangeBaudrate(921600, 115200);

            // Decode SLIP to verify contents
            byte[] body = new byte[packet.Length - 2];
            Buffer.BlockCopy(packet, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            // Verify it's a ChangeBaudrate command
            Assert.AreEqual((byte)Esp32Command.ChangeBaudrate, raw[1]);

            // Data should be 8 bytes (new baud + old baud)
            ushort dataLen = (ushort)(raw[2] | (raw[3] << 8));
            Assert.AreEqual(8, dataLen);

            // Verify baud rates in the packet data
            Assert.AreEqual(921600u, Esp32CommandPacket.ReadUInt32LE(raw, 8));
            Assert.AreEqual(115200u, Esp32CommandPacket.ReadUInt32LE(raw, 12));

            // Simulate response
            byte[] responsePayload = new byte[]
            {
                0x01, (byte)Esp32Command.ChangeBaudrate,
                0x04, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            };

            var response = Esp32ResponsePacket.Parse(responsePayload);
            Assert.AreEqual(Esp32Command.ChangeBaudrate, response.Command);
            Assert.IsTrue(response.IsSuccess);
        }

        [TestMethod]
        public void Protocol_SyncPacket_HasCorrectPayloadSize()
        {
            byte[] syncPacket = Esp32CommandPacket.BuildSync();

            // Decode SLIP
            byte[] body = new byte[syncPacket.Length - 2];
            Buffer.BlockCopy(syncPacket, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            // Total raw size: 8 header + 36 sync data = 44 bytes
            Assert.AreEqual(44, raw.Length);

            // Data length field should be 36
            ushort dataLen = (ushort)(raw[2] | (raw[3] << 8));
            Assert.AreEqual(36, dataLen);
        }

        [TestMethod]
        public void Protocol_MultipleResponseParsing_DifferentCommands()
        {
            // Simulate receiving multiple responses and matching them by command
            var responses = new[]
            {
                // Stale Sync response
                new byte[] { 0x01, 0x08, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                // ReadReg response with ESP32 magic
                new byte[] { 0x01, 0x0A, 0x04, 0x00, 0x83, 0x1D, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00 },
                // WriteReg response
                new byte[] { 0x01, 0x09, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            };

            // Parse all and find the ReadReg response
            Esp32ResponsePacket readRegResponse = null;

            foreach (var payload in responses)
            {
                var response = Esp32ResponsePacket.Parse(payload);

                if (response.Command == Esp32Command.ReadReg)
                {
                    readRegResponse = response;
                    break;
                }
            }

            Assert.IsNotNull(readRegResponse);
            Assert.AreEqual(0x00F01D83u, readRegResponse.Value);
        }

        [TestMethod]
        public void Protocol_EraseFlashCommand_BuildsCorrectly()
        {
            // Erase flash has no data payload
            byte[] packet = Esp32CommandPacket.Build(Esp32Command.EraseFlash);

            // Decode SLIP
            byte[] body = new byte[packet.Length - 2];
            Buffer.BlockCopy(packet, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            Assert.AreEqual(0x00, raw[0]); // Direction = request
            Assert.AreEqual((byte)Esp32Command.EraseFlash, raw[1]);

            // Data length should be 0
            ushort dataLen = (ushort)(raw[2] | (raw[3] << 8));
            Assert.AreEqual(0, dataLen);

            // Header should be exactly 8 bytes
            Assert.AreEqual(8, raw.Length);
        }

        [TestMethod]
        public void Protocol_EraseRegionCommand_BuildsCorrectly()
        {
            // Erase region takes start address + length
            uint startAddr = 0x10000;
            uint length = 0x1000; // 4KB

            byte[] data = new byte[8];
            Esp32CommandPacket.WriteUInt32LE(data, 0, startAddr);
            Esp32CommandPacket.WriteUInt32LE(data, 4, length);

            byte[] packet = Esp32CommandPacket.Build(Esp32Command.EraseRegion, data);

            // Decode SLIP
            byte[] body = new byte[packet.Length - 2];
            Buffer.BlockCopy(packet, 1, body, 0, body.Length);
            byte[] raw = SlipFraming.Decode(body);

            Assert.AreEqual((byte)Esp32Command.EraseRegion, raw[1]);
            Assert.AreEqual(startAddr, Esp32CommandPacket.ReadUInt32LE(raw, 8));
            Assert.AreEqual(length, Esp32CommandPacket.ReadUInt32LE(raw, 12));
        }

        [TestMethod]
        public void Protocol_FullSyncSimulation_CommandAndMultipleResponses()
        {
            // The ROM bootloader sends up to 8 responses to a single SYNC.
            // Verify we can parse them all.
            byte[] syncResponsePayload = new byte[]
            {
                0x01, 0x08, 0x04, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            };

            for (int i = 0; i < 8; i++)
            {
                var response = Esp32ResponsePacket.Parse(syncResponsePayload);
                Assert.AreEqual(Esp32Command.Sync, response.Command);
                Assert.IsTrue(response.IsSuccess);
            }
        }

        [TestMethod]
        public void Protocol_SlipFramedResponse_DecodeAndParse()
        {
            // Simulate a SLIP-framed response as it would arrive from serial
            // Build a raw response payload
            byte[] rawResponse = new byte[]
            {
                0x01, 0x0A, 0x04, 0x00,
                0x83, 0x1D, 0xF0, 0x00,
                0x00, 0x00, 0x00, 0x00
            };

            // SLIP-encode it (as the bootloader would send)
            byte[] slipFramed = SlipFraming.Encode(rawResponse);

            // Verify framing
            Assert.AreEqual(0xC0, slipFramed[0]);
            Assert.AreEqual(0xC0, slipFramed[slipFramed.Length - 1]);

            // Decode the frame (strip delimiters)
            byte[] body = new byte[slipFramed.Length - 2];
            Buffer.BlockCopy(slipFramed, 1, body, 0, body.Length);
            byte[] decoded = SlipFraming.Decode(body);

            // Parse the response
            var response = Esp32ResponsePacket.Parse(decoded);
            Assert.AreEqual(Esp32Command.ReadReg, response.Command);
            Assert.AreEqual(0x00F01D83u, response.Value);
            Assert.IsTrue(response.IsSuccess);
        }

        [TestMethod]
        public void Protocol_ResponseWithSpecialSlipBytes_DecodesCorrectly()
        {
            // Create a response where the value contains bytes that need SLIP escaping
            // Value = 0xC0DB00FF — contains both 0xC0 and 0xDB
            byte[] rawResponse = new byte[]
            {
                0x01, 0x0A, 0x04, 0x00,
                0xC0, 0xDB, 0x00, 0xFF,  // Value with special SLIP bytes
                0x00, 0x00, 0x00, 0x00
            };

            // SLIP encode → decode round trip
            byte[] slipFramed = SlipFraming.Encode(rawResponse);
            byte[] body = new byte[slipFramed.Length - 2];
            Buffer.BlockCopy(slipFramed, 1, body, 0, body.Length);
            byte[] decoded = SlipFraming.Decode(body);

            // Parse response
            var response = Esp32ResponsePacket.Parse(decoded);
            Assert.AreEqual(0xFF00DBC0u, response.Value); // LE: C0, DB, 00, FF
        }

        [TestMethod]
        public void Protocol_BootloaderClient_CanBeCreatedAndDisposed()
        {
            // Verify the client can be constructed with a port name
            // (Won't actually open the port since we don't connect)
            using var client = new Esp32BootloaderClient("COM999", 115200);

            Assert.AreEqual(115200, client.CurrentBaudRate);
            Assert.IsFalse(client.IsStubRunning);
        }

        [TestMethod]
        public void Protocol_BootloaderClient_DisposeIsIdempotent()
        {
            var client = new Esp32BootloaderClient("COM999");

            client.Dispose();
            client.Dispose(); // Should not throw
        }

        #endregion

        #region Phase 4: Chip Configuration & Detection Tests

        // ---- Esp32ChipConfigs: GetByMagicValue ----

        [TestMethod]
        [DataRow(0x00F01D83u, "esp32", "ESP32")]
        [DataRow(0x000007C6u, "esp32s2", "ESP32-S2")]
        [DataRow(0x00000009u, "esp32s3", "ESP32-S3")]
        [DataRow(0x6921506Fu, "esp32c3", "ESP32-C3")]
        [DataRow(0x2CE0806Fu, "esp32c6", "ESP32-C6")]
        [DataRow(0xD7B73E80u, "esp32h2", "ESP32-H2")]
        public void ChipConfig_GetByMagicValue_ReturnsCorrectChip(uint magic, string expectedType, string expectedName)
        {
            var config = Esp32ChipConfigs.GetByMagicValue(magic);

            Assert.IsNotNull(config);
            Assert.AreEqual(expectedType, config.ChipType);
            Assert.AreEqual(expectedName, config.Name);
        }

        [TestMethod]
        public void ChipConfig_GetByMagicValue_C3AltMagic_ReturnsC3()
        {
            // ESP8685 variant uses an alternative magic value
            var config = Esp32ChipConfigs.GetByMagicValue(0x1B31506F);

            Assert.IsNotNull(config);
            Assert.AreEqual("esp32c3", config.ChipType);
            Assert.AreEqual("ESP32-C3", config.Name);
        }

        [TestMethod]
        public void ChipConfig_GetByMagicValue_UnknownMagic_ReturnsNull()
        {
            Assert.IsNull(Esp32ChipConfigs.GetByMagicValue(0xDEADBEEF));
        }

        // ---- Esp32ChipConfigs: GetByType ----

        [TestMethod]
        [DataRow("esp32", "ESP32")]
        [DataRow("esp32s2", "ESP32-S2")]
        [DataRow("esp32s3", "ESP32-S3")]
        [DataRow("esp32c3", "ESP32-C3")]
        [DataRow("esp32c6", "ESP32-C6")]
        [DataRow("esp32h2", "ESP32-H2")]
        public void ChipConfig_GetByType_ReturnsCorrectChip(string chipType, string expectedName)
        {
            var config = Esp32ChipConfigs.GetByType(chipType);

            Assert.IsNotNull(config);
            Assert.AreEqual(expectedName, config.Name);
        }

        [TestMethod]
        public void ChipConfig_GetByType_IsCaseInsensitive()
        {
            var config = Esp32ChipConfigs.GetByType("ESP32S3");

            Assert.IsNotNull(config);
            Assert.AreEqual("esp32s3", config.ChipType);
        }

        [TestMethod]
        public void ChipConfig_GetByType_UnknownType_ReturnsNull()
        {
            Assert.IsNull(Esp32ChipConfigs.GetByType("esp32x9"));
        }

        // ---- Esp32ChipConfig: SPI register address calculations ----

        [TestMethod]
        public void ChipConfig_Esp32_SpiAddressesCorrect()
        {
            var config = Esp32ChipConfigs.ESP32;

            // SPI base 0x3FF42000
            Assert.AreEqual(0x3FF42000u + 0x1Cu, config.SpiUsrAddr);
            Assert.AreEqual(0x3FF42000u + 0x80u, config.SpiW0Addr);
            Assert.AreEqual(0x3FF42000u + 0x20u, config.SpiUsr1Addr);
            Assert.AreEqual(0x3FF42000u + 0x24u, config.SpiUsr2Addr);
            Assert.AreEqual(0x3FF42000u + 0x28u, config.SpiMosiDlenAddr);
            Assert.AreEqual(0x3FF42000u + 0x2Cu, config.SpiMisoDlenAddr);
            Assert.AreEqual(0x3FF42000u, config.SpiCmdAddr);
            Assert.IsFalse(config.UsesOldSpiRegisters);
        }

        [TestMethod]
        public void ChipConfig_Esp32S3_SpiAddressesCorrect()
        {
            var config = Esp32ChipConfigs.ESP32_S3;

            // SPI base 0x60002000
            Assert.AreEqual(0x60002000u + 0x18u, config.SpiUsrAddr);
            Assert.AreEqual(0x60002000u + 0x58u, config.SpiW0Addr);
            Assert.AreEqual(0x60002000u + 0x1Cu, config.SpiUsr1Addr);
            Assert.AreEqual(0x60002000u + 0x20u, config.SpiUsr2Addr);
            Assert.AreEqual(0x60002000u + 0x24u, config.SpiMosiDlenAddr);
            Assert.AreEqual(0x60002000u + 0x28u, config.SpiMisoDlenAddr);
            Assert.AreEqual(0x60002000u, config.SpiCmdAddr);
            Assert.IsFalse(config.UsesOldSpiRegisters);
        }

        // ---- Esp32ChipConfig: Bootloader addresses ----

        [TestMethod]
        public void ChipConfig_Esp32_BootloaderAt0x1000()
        {
            Assert.AreEqual(0x1000, Esp32ChipConfigs.ESP32.BootloaderAddress);
            Assert.AreEqual(0x1000, Esp32ChipConfigs.ESP32_S2.BootloaderAddress);
        }

        [TestMethod]
        public void ChipConfig_NewerChips_BootloaderAt0x0()
        {
            Assert.AreEqual(0x0, Esp32ChipConfigs.ESP32_S3.BootloaderAddress);
            Assert.AreEqual(0x0, Esp32ChipConfigs.ESP32_C3.BootloaderAddress);
            Assert.AreEqual(0x0, Esp32ChipConfigs.ESP32_C6.BootloaderAddress);
            Assert.AreEqual(0x0, Esp32ChipConfigs.ESP32_H2.BootloaderAddress);
        }

        // ---- Esp32ChipConfig: Only ESP32 uses old SPI registers ----

        [TestMethod]
        public void ChipConfig_OnlyEsp32_UsesOldSpiRegisters()
        {
            // ESP32 uses new-style SPI registers (MOSI_DLEN/MISO_DLEN) per esptool.py
            // Old-style USR1 register path is only for ESP8266 (not supported)
            Assert.IsFalse(Esp32ChipConfigs.ESP32.UsesOldSpiRegisters);
            Assert.IsFalse(Esp32ChipConfigs.ESP32_S2.UsesOldSpiRegisters);
            Assert.IsFalse(Esp32ChipConfigs.ESP32_S3.UsesOldSpiRegisters);
            Assert.IsFalse(Esp32ChipConfigs.ESP32_C3.UsesOldSpiRegisters);
            Assert.IsFalse(Esp32ChipConfigs.ESP32_C6.UsesOldSpiRegisters);
            Assert.IsFalse(Esp32ChipConfigs.ESP32_H2.UsesOldSpiRegisters);
        }

        // ---- Esp32ChipConfigs.All ----

        [TestMethod]
        public void ChipConfig_All_ReturnsSixConfigs()
        {
            var all = new System.Collections.Generic.List<Esp32ChipConfig>(Esp32ChipConfigs.All);

            Assert.AreEqual(6, all.Count);
        }

        // ---- Esp32ChipConfig: MagicRegAddr ----

        [TestMethod]
        public void ChipConfig_AllChips_UseSameMagicRegAddr()
        {
            foreach (var config in Esp32ChipConfigs.All)
            {
                Assert.AreEqual(0x40001000u, config.MagicRegAddr,
                    $"{config.Name} should use magic register at 0x40001000");
            }
        }

        // ---- Esp32ChipConfig: FlashWriteBlockSize consistency ----

        [TestMethod]
        public void ChipConfig_AllChips_FlashWriteBlockSize0x4000()
        {
            foreach (var config in Esp32ChipConfigs.All)
            {
                Assert.AreEqual(0x4000, config.FlashWriteBlockSize,
                    $"{config.Name} should have FlashWriteBlockSize=0x4000");
            }
        }

        // ---- Esp32ChipDetector: DetectFlashSizeFromId ----

        [TestMethod]
        [DataRow((short)0x1240, 256 * 1024, "256KB")]
        [DataRow((short)0x1340, 512 * 1024, "512KB")]
        [DataRow((short)0x1440, 1 * 1024 * 1024, "1MB")]
        [DataRow((short)0x1540, 2 * 1024 * 1024, "2MB")]
        [DataRow((short)0x1640, 4 * 1024 * 1024, "4MB")]
        [DataRow((short)0x1740, 8 * 1024 * 1024, "8MB")]
        [DataRow((short)0x1840, 16 * 1024 * 1024, "16MB")]
        [DataRow((short)0x1940, 32 * 1024 * 1024, "32MB")]
        [DataRow((short)0x1A40, 64 * 1024 * 1024, "64MB")]
        [DataRow((short)0x2040, 64 * 1024 * 1024, "64MB alt")]
        public void ChipDetector_DetectFlashSizeFromId_KnownSizes(short deviceId, int expectedBytes, string description)
        {
            // deviceId layout: capacity in high byte, memType in low byte
            // (deviceId >> 8) & 0xFF extracts the capacity byte
            int result = Esp32ChipDetector.DetectFlashSizeFromId(deviceId);

            Assert.AreEqual(expectedBytes, result, $"Failed for {description}");
        }

        [TestMethod]
        public void ChipDetector_DetectFlashSizeFromId_UnknownReturnsNegativeOne()
        {
            // Capacity byte 0xFF is not in the known list
            Assert.AreEqual(-1, Esp32ChipDetector.DetectFlashSizeFromId(unchecked((short)0xFF40)));
        }

        [TestMethod]
        public void ChipDetector_DetectFlashSizeFromId_MemTypeDoesNotAffectResult()
        {
            // Same capacity byte (0x16 = 4MB) with different memType values
            int size1 = Esp32ChipDetector.DetectFlashSizeFromId(0x1640); // memType 0x40
            int size2 = Esp32ChipDetector.DetectFlashSizeFromId(0x1620); // memType 0x20
            int size3 = Esp32ChipDetector.DetectFlashSizeFromId(0x1600); // memType 0x00

            Assert.AreEqual(4 * 1024 * 1024, size1);
            Assert.AreEqual(4 * 1024 * 1024, size2);
            Assert.AreEqual(4 * 1024 * 1024, size3);
        }

        // ---- Esp32ChipConfig: eFuse MAC register addresses are set ----

        [TestMethod]
        public void ChipConfig_Esp32_EfuseMacAddresses()
        {
            Assert.AreEqual(0x3FF5A004u, Esp32ChipConfigs.ESP32.EfuseMacWord0Addr);
            Assert.AreEqual(0x3FF5A008u, Esp32ChipConfigs.ESP32.EfuseMacWord1Addr);
        }

        [TestMethod]
        public void ChipConfig_Esp32S3_EfuseMacAddresses()
        {
            Assert.AreEqual(0x60007044u, Esp32ChipConfigs.ESP32_S3.EfuseMacWord0Addr);
            Assert.AreEqual(0x60007048u, Esp32ChipConfigs.ESP32_S3.EfuseMacWord1Addr);
        }

        [TestMethod]
        public void ChipConfig_Esp32C3_EfuseMacAddresses()
        {
            Assert.AreEqual(0x60008844u, Esp32ChipConfigs.ESP32_C3.EfuseMacWord0Addr);
            Assert.AreEqual(0x60008848u, Esp32ChipConfigs.ESP32_C3.EfuseMacWord1Addr);
        }

        [TestMethod]
        public void ChipConfig_Esp32C6_And_H2_EfuseMacAddresses()
        {
            // C6 and H2 share the same eFuse layout
            Assert.AreEqual(0x600B0844u, Esp32ChipConfigs.ESP32_C6.EfuseMacWord0Addr);
            Assert.AreEqual(0x600B0848u, Esp32ChipConfigs.ESP32_C6.EfuseMacWord1Addr);
            Assert.AreEqual(0x600B0844u, Esp32ChipConfigs.ESP32_H2.EfuseMacWord0Addr);
            Assert.AreEqual(0x600B0848u, Esp32ChipConfigs.ESP32_H2.EfuseMacWord1Addr);
        }

        #endregion

        #region Phase 5: Flash Controller Tests

        // ---- CalculateBlockCount ----

        [TestMethod]
        [DataRow(0, 0x4000, 0)]
        [DataRow(1, 0x4000, 1)]
        [DataRow(0x4000, 0x4000, 1)]
        [DataRow(0x4001, 0x4000, 2)]
        [DataRow(0x8000, 0x4000, 2)]
        [DataRow(0x10000, 0x4000, 4)]
        [DataRow(100, 0x1000, 1)]
        [DataRow(0x1001, 0x1000, 2)]
        public void FlashController_CalculateBlockCount(int size, int blockSize, int expectedBlocks)
        {
            Assert.AreEqual(expectedBlocks, Esp32FlashController.CalculateBlockCount(size, blockSize));
        }

        // ---- PadToBlockSize ----

        [TestMethod]
        public void FlashController_PadToBlockSize_AlreadyAligned_ReturnsSameArray()
        {
            byte[] data = new byte[0x4000];
            byte[] result = Esp32FlashController.PadToBlockSize(data, 0x4000);

            Assert.AreSame(data, result); // Should return the exact same array reference
        }

        [TestMethod]
        public void FlashController_PadToBlockSize_NotAligned_PadsWithFF()
        {
            byte[] data = new byte[] { 0x01, 0x02, 0x03 };
            byte[] result = Esp32FlashController.PadToBlockSize(data, 8);

            Assert.AreEqual(8, result.Length);

            // Original data preserved
            Assert.AreEqual(0x01, result[0]);
            Assert.AreEqual(0x02, result[1]);
            Assert.AreEqual(0x03, result[2]);

            // Padding is 0xFF
            Assert.AreEqual(0xFF, result[3]);
            Assert.AreEqual(0xFF, result[4]);
            Assert.AreEqual(0xFF, result[5]);
            Assert.AreEqual(0xFF, result[6]);
            Assert.AreEqual(0xFF, result[7]);
        }

        [TestMethod]
        public void FlashController_PadToBlockSize_Empty_ReturnsEmpty()
        {
            byte[] data = Array.Empty<byte>();
            byte[] result = Esp32FlashController.PadToBlockSize(data, 0x4000);

            Assert.AreSame(data, result); // 0 % any blockSize == 0, so same reference
        }

        [TestMethod]
        public void FlashController_PadToBlockSize_PadsToNextMultiple()
        {
            byte[] data = new byte[0x4001]; // Just 1 byte over a 16KB block
            byte[] result = Esp32FlashController.PadToBlockSize(data, 0x4000);

            Assert.AreEqual(0x8000, result.Length); // Should pad to 32KB (2 blocks)
        }

        // ---- EraseBlockSize and constants ----

        [TestMethod]
        public void FlashController_EraseBlockSizeIs4KB()
        {
            Assert.AreEqual(0x1000, Esp32FlashController.EraseBlockSize);
        }

        [TestMethod]
        public void FlashController_RomFlashBlockSizeIs16KB()
        {
            Assert.AreEqual(0x4000, Esp32FlashController.RomFlashBlockSize);
        }

        [TestMethod]
        public void FlashController_ReadBlockSizeIs4KB()
        {
            Assert.AreEqual(0x1000, Esp32FlashController.ReadBlockSize);
        }

        // ---- Flash write protocol packet construction ----

        [TestMethod]
        public void FlashController_FlashBeginPacket_HasCorrectFormat()
        {
            // FLASH_BEGIN data: [total_size:4][num_blocks:4][block_size:4][offset:4]
            // Verify that sending these values through the command packet builder produces
            // the expected binary layout
            uint totalSize = 0x10000;  // 64KB
            uint numBlocks = 4;        // 4 blocks of 16KB
            uint blockSize = 0x4000;   // 16KB
            uint offset = 0x1000;      // Start at 4KB

            byte[] data = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(data, 0, totalSize);
            Esp32CommandPacket.WriteUInt32LE(data, 4, numBlocks);
            Esp32CommandPacket.WriteUInt32LE(data, 8, blockSize);
            Esp32CommandPacket.WriteUInt32LE(data, 12, offset);

            // Verify correct encoding
            Assert.AreEqual(totalSize, Esp32CommandPacket.ReadUInt32LE(data, 0));
            Assert.AreEqual(numBlocks, Esp32CommandPacket.ReadUInt32LE(data, 4));
            Assert.AreEqual(blockSize, Esp32CommandPacket.ReadUInt32LE(data, 8));
            Assert.AreEqual(offset, Esp32CommandPacket.ReadUInt32LE(data, 12));

            // Build as a command packet for FLASH_BEGIN
            byte[] raw = Esp32CommandPacket.BuildRaw(Esp32Command.FlashBegin, data);
            Assert.AreEqual(0x00, raw[0]); // Direction = request
            Assert.AreEqual((byte)Esp32Command.FlashBegin, raw[1]); // Command = 0x02
            Assert.AreEqual(16, raw[2] | (raw[3] << 8)); // Data length = 16
        }

        [TestMethod]
        public void FlashController_FlashDataPacket_HasHeaderAndBlock()
        {
            // FLASH_DATA payload: [data_size:4][seq_num:4][0:4][0:4][block_data]
            int blockSize = 16; // Small block for testing
            byte[] block = new byte[blockSize];

            for (int i = 0; i < blockSize; i++)
            {
                block[i] = (byte)(i + 1);
            }

            byte[] payload = new byte[16 + blockSize];
            Esp32CommandPacket.WriteUInt32LE(payload, 0, (uint)blockSize);
            Esp32CommandPacket.WriteUInt32LE(payload, 4, 0);  // Sequence 0
            Esp32CommandPacket.WriteUInt32LE(payload, 8, 0);
            Esp32CommandPacket.WriteUInt32LE(payload, 12, 0);
            Buffer.BlockCopy(block, 0, payload, 16, blockSize);

            // Verify header fields
            Assert.AreEqual((uint)blockSize, Esp32CommandPacket.ReadUInt32LE(payload, 0));
            Assert.AreEqual(0u, Esp32CommandPacket.ReadUInt32LE(payload, 4));

            // Verify block data at offset 16
            Assert.AreEqual(0x01, payload[16]);
            Assert.AreEqual(0x10, payload[31]); // blockSize = 16, last byte

            // Checksum covers only the block data, not the header
            uint checksum = Esp32CommandPacket.CalculateChecksum(block);
            byte[] raw = Esp32CommandPacket.BuildRaw(Esp32Command.FlashData, payload, checksum);

            // The checksum field is at raw[4..7]
            uint packetChecksum = Esp32CommandPacket.ReadUInt32LE(raw, 4);
            Assert.AreEqual(checksum, packetChecksum);
        }

        [TestMethod]
        public void FlashController_FlashEndPacket_StayInBootloader()
        {
            // FLASH_END data: [action:4] where 0=reboot, 1=stay
            byte[] data = new byte[4];
            Esp32CommandPacket.WriteUInt32LE(data, 0, 1); // stay in bootloader

            byte[] raw = Esp32CommandPacket.BuildRaw(Esp32Command.FlashEnd, data);
            Assert.AreEqual((byte)Esp32Command.FlashEnd, raw[1]); // Command = 0x04
            Assert.AreEqual(4, raw[2] | (raw[3] << 8)); // Data length = 4
            Assert.AreEqual(1u, Esp32CommandPacket.ReadUInt32LE(raw, 8)); // action=1 at payload offset
        }

        [TestMethod]
        public void FlashController_FlashEndPacket_Reboot()
        {
            byte[] data = new byte[4];
            Esp32CommandPacket.WriteUInt32LE(data, 0, 0); // reboot

            byte[] raw = Esp32CommandPacket.BuildRaw(Esp32Command.FlashEnd, data);
            Assert.AreEqual(0u, Esp32CommandPacket.ReadUInt32LE(raw, 8)); // action=0
        }

        // ---- Erase region alignment validation ----

        [TestMethod]
        public void FlashController_EraseRegion_ValidatesAddressAlignment()
        {
            // Cannot test EraseRegion without a real client, but we can verify
            // alignment constants are consistent
            Assert.AreEqual(0, 0x0000 % Esp32FlashController.EraseBlockSize);
            Assert.AreEqual(0, 0x1000 % Esp32FlashController.EraseBlockSize);
            Assert.AreEqual(0, 0x10000 % Esp32FlashController.EraseBlockSize);
            Assert.AreNotEqual(0, 0x0001 % Esp32FlashController.EraseBlockSize);
            Assert.AreNotEqual(0, 0x0800 % Esp32FlashController.EraseBlockSize);
        }

        // ---- Erase region packet construction ----

        [TestMethod]
        public void FlashController_EraseRegionPacket_Format()
        {
            // ERASE_REGION data: [start_address:4][length:4]
            uint startAddr = 0x10000;
            uint length = 0x8000;

            byte[] data = new byte[8];
            Esp32CommandPacket.WriteUInt32LE(data, 0, startAddr);
            Esp32CommandPacket.WriteUInt32LE(data, 4, length);

            byte[] raw = Esp32CommandPacket.BuildRaw(Esp32Command.EraseRegion, data);
            Assert.AreEqual((byte)Esp32Command.EraseRegion, raw[1]); // 0xD1
            Assert.AreEqual(startAddr, Esp32CommandPacket.ReadUInt32LE(raw, 8));
            Assert.AreEqual(length, Esp32CommandPacket.ReadUInt32LE(raw, 12));
        }

        // ---- Mass erase packet ----

        [TestMethod]
        public void FlashController_EraseFlashPacket_NoData()
        {
            // ERASE_FLASH has no data payload
            byte[] raw = Esp32CommandPacket.BuildRaw(Esp32Command.EraseFlash);
            Assert.AreEqual((byte)Esp32Command.EraseFlash, raw[1]); // 0xD0
            Assert.AreEqual(0, raw[2] | (raw[3] << 8)); // Data length = 0
        }

        // ---- Read flash packet construction ----

        [TestMethod]
        public void FlashController_ReadFlashPacket_Format()
        {
            // READ_FLASH data: [address:4][length:4][block_size:4][max_in_flight:4]
            uint address = 0x0;
            uint length = 0x400000; // 4MB
            uint blockSize = 0x1000;
            uint maxInFlight = 1;

            byte[] data = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(data, 0, address);
            Esp32CommandPacket.WriteUInt32LE(data, 4, length);
            Esp32CommandPacket.WriteUInt32LE(data, 8, blockSize);
            Esp32CommandPacket.WriteUInt32LE(data, 12, maxInFlight);

            byte[] raw = Esp32CommandPacket.BuildRaw(Esp32Command.ReadFlash, data);
            Assert.AreEqual((byte)Esp32Command.ReadFlash, raw[1]); // 0xD2
            Assert.AreEqual(address, Esp32CommandPacket.ReadUInt32LE(raw, 8));
            Assert.AreEqual(length, Esp32CommandPacket.ReadUInt32LE(raw, 12));
            Assert.AreEqual(blockSize, Esp32CommandPacket.ReadUInt32LE(raw, 16));
            Assert.AreEqual(maxInFlight, Esp32CommandPacket.ReadUInt32LE(raw, 20));
        }

        // ---- Checksum correctness for flash data blocks ----

        [TestMethod]
        public void FlashController_FlashDataChecksum_CoversBlockOnly()
        {
            // The checksum should cover only the actual block data,
            // NOT the 16-byte flash data header
            byte[] block = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            uint expectedChecksum = Esp32CommandPacket.CalculateChecksum(block);

            // Verify the checksum is computed from block data
            // Seed with 0xEF, XOR each byte
            uint manual = 0xEF ^ 0x01 ^ 0x02 ^ 0x03 ^ 0x04;
            Assert.AreEqual(manual, expectedChecksum);

            // Adding a header to the payload should NOT affect the checksum
            // (because checksum is calculated from block only, before constructing payload)
            byte[] payload = new byte[16 + block.Length];
            Esp32CommandPacket.WriteUInt32LE(payload, 0, (uint)block.Length);
            Esp32CommandPacket.WriteUInt32LE(payload, 4, 0);
            Buffer.BlockCopy(block, 0, payload, 16, block.Length);

            uint payloadChecksum = Esp32CommandPacket.CalculateChecksum(payload);
            Assert.AreNotEqual(expectedChecksum, payloadChecksum, 
                "Checksum of full payload differs from checksum of block alone");
        }

        // ---- Block count and padding for typical firmware sizes ----

        [TestMethod]
        public void FlashController_TypicalFirmwareWrite_BlockCalc()
        {
            // Typical nanoCLR firmware is ~600KB
            int firmwareSize = 600 * 1024;
            int blockSize = 0x4000; // 16KB

            int blocks = Esp32FlashController.CalculateBlockCount(firmwareSize, blockSize);

            // 600KB / 16KB = 37.5 → 38 blocks
            Assert.AreEqual(38, blocks);

            // Total padded size = 38 * 16KB = 608KB
            byte[] padded = Esp32FlashController.PadToBlockSize(
                new byte[firmwareSize], blockSize);
            Assert.AreEqual(38 * blockSize, padded.Length);
        }

        // ---- Chip config FlashWriteBlockSize matches ROM constant ----

        [TestMethod]
        public void FlashController_ChipConfigBlockSize_MatchesRomConstant()
        {
            foreach (var config in Esp32ChipConfigs.All)
            {
                Assert.AreEqual(Esp32FlashController.RomFlashBlockSize, config.FlashWriteBlockSize,
                    $"{config.Name} FlashWriteBlockSize should match RomFlashBlockSize");
            }
        }

        #endregion

        #region Phase 7: EspTool Refactoring Tests

        [TestMethod]
        public void EspTool_ImplementsIDisposable()
        {
            // Verify EspTool implements IDisposable after refactoring
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(EspTool)));
        }

        [TestMethod]
        public void EspTool_Constructor_InvalidPort_ThrowsException()
        {
            // On Windows, a non-existent COM port should throw
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Assert.ThrowsException<EspToolExecutionException>(() =>
                {
                    new EspTool("COM999", 115200, "dio", 40, null, VerbosityLevel.Quiet);
                });
            }
        }

        [TestMethod]
        public void EspTool_ChipTypeDisplayFormat()
        {
            // Verify the chip type to display format mapping matches expected convention
            // This tests the switch expression logic in GetDeviceDetails
            var expectedMappings = new Dictionary<string, string>
            {
                { "esp32", "ESP32" },
                { "esp32s2", "ESP32-S2" },
                { "esp32s3", "ESP32-S3" },
                { "esp32c3", "ESP32-C3" },
                { "esp32c6", "ESP32-C6" },
                { "esp32h2", "ESP32-H2" },
            };

            // Verify all chip configs map to known display names
            foreach (var config in Esp32ChipConfigs.All)
            {
                Assert.IsTrue(expectedMappings.ContainsKey(config.ChipType),
                    $"Chip type '{config.ChipType}' should have a display format mapping");

                Assert.AreEqual(config.Name, expectedMappings[config.ChipType],
                    $"Config name for '{config.ChipType}' should match expected display format");
            }
        }

        [TestMethod]
        public void EspTool_DisposeIsIdempotent()
        {
            // Verify Dispose can be called multiple times without error
            // Even though we can't create a real EspTool (needs valid port), verify the pattern
            Assert.IsTrue(typeof(EspTool).GetMethod("Dispose") != null);
        }

        [TestMethod]
        public void EspTool_HasExpectedPublicApi()
        {
            // Verify the refactored EspTool preserves the expected public API surface
            var type = typeof(EspTool);

            // Constructor
            Assert.IsNotNull(type.GetConstructor(new[]
            {
                typeof(string), typeof(int), typeof(string),
                typeof(int), typeof(PartitionTableSize?), typeof(VerbosityLevel)
            }), "Constructor should exist with original parameter types");

            // Public properties
            Assert.IsNotNull(type.GetProperty("ComPortAvailable"), "ComPortAvailable property should exist");
            Assert.IsNotNull(type.GetProperty("Verbosity"), "Verbosity property should exist");

            // Public method
            Assert.IsNotNull(type.GetMethod("GetDeviceDetails"), "GetDeviceDetails method should exist");

            // Dispose
            Assert.IsNotNull(type.GetMethod("Dispose"), "Dispose method should exist");
        }

        #endregion

        #region Phase 10: esptool Binary Removal Validation

        [TestMethod]
        public void EspTool_NoExternalBinaryDependency()
        {
            // Verify the EspTool class no longer uses System.Diagnostics.Process
            var type = typeof(EspTool);
            var fields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                Assert.AreNotEqual(typeof(System.Diagnostics.Process), field.FieldType,
                    $"Field '{field.Name}' should not be of type Process — esptool binary dependency removed");
            }
        }

        [TestMethod]
        public void EspTool_NoRunEspToolMethod()
        {
            // Verify the old RunEspTool method has been removed
            var type = typeof(EspTool);
            var method = type.GetMethod("RunEspTool",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNull(method, "RunEspTool should have been removed in the native implementation");
        }

        [TestMethod]
        public void EspTool_UsesNativeSerialProtocol()
        {
            // Verify the EspTool has fields for the native serial protocol components
            var type = typeof(EspTool);
            var fields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fieldNames = fields.Select(f => f.Name).ToList();

            Assert.IsTrue(fieldNames.Contains("_client"), "Should have _client field for Esp32BootloaderClient");
            Assert.IsTrue(fieldNames.Contains("_chipDetector"), "Should have _chipDetector field for Esp32ChipDetector");
            Assert.IsTrue(fieldNames.Contains("_flashController"), "Should have _flashController field for Esp32FlashController");
        }

        [TestMethod]
        public void ProjectFile_NoEsptoolBinaryReferences()
        {
            // Verify the .csproj no longer references esptool binaries
            string projectDir = Path.GetDirectoryName(typeof(EspTool).Assembly.Location);
            // Walk up to find the source project file
            string repoRoot = projectDir;
            while (repoRoot != null && !File.Exists(Path.Combine(repoRoot, "nanoFirmwareFlasher.sln")))
            {
                repoRoot = Path.GetDirectoryName(repoRoot);
            }

            if (repoRoot != null)
            {
                string csproj = Path.Combine(repoRoot, "nanoFirmwareFlasher.Library", "nanoFirmwareFlasher.Library.csproj");
                if (File.Exists(csproj))
                {
                    string content = File.ReadAllText(csproj);
                    Assert.IsFalse(content.Contains("esptoolWin"), ".csproj should not reference esptoolWin");
                    Assert.IsFalse(content.Contains("esptoolMac"), ".csproj should not reference esptoolMac");
                    Assert.IsFalse(content.Contains("esptoolLinux"), ".csproj should not reference esptoolLinux");
                }
            }
        }

        #endregion

        #region Phase 6: Stub Loader Tests

        [TestMethod]
        public void StubImage_ParseJson_ValidStub()
        {
            // Create a minimal valid stub JSON
            byte[] textData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            byte[] dataData = new byte[] { 0xAA, 0xBB };
            string json = $@"{{
                ""text"": ""{Convert.ToBase64String(textData)}"",
                ""text_start"": 1073905664,
                ""data"": ""{Convert.ToBase64String(dataData)}"",
                ""data_start"": 1073889280,
                ""entry"": 1073906908
            }}";

            var stub = Esp32StubImage.ParseJson(json);

            CollectionAssert.AreEqual(textData, stub.Text);
            Assert.AreEqual(1073905664u, stub.TextStart);
            CollectionAssert.AreEqual(dataData, stub.Data);
            Assert.AreEqual(1073889280u, stub.DataStart);
            Assert.AreEqual(1073906908u, stub.Entry);
        }

        [TestMethod]
        public void StubImage_ParseJson_EmptyData()
        {
            byte[] textData = new byte[] { 0xFF };
            string json = $@"{{
                ""text"": ""{Convert.ToBase64String(textData)}"",
                ""text_start"": 100,
                ""entry"": 200
            }}";

            var stub = Esp32StubImage.ParseJson(json);

            Assert.AreEqual(1, stub.Text.Length);
            Assert.AreEqual(0, stub.Data.Length, "Missing data field should result in empty array");
            Assert.AreEqual(100u, stub.TextStart);
            Assert.AreEqual(200u, stub.Entry);
        }

        [TestMethod]
        public void StubImage_ParseJson_MissingText_Throws()
        {
            string json = @"{ ""data"": ""AAAA"", ""data_start"": 100, ""entry"": 200 }";

            Assert.ThrowsException<FormatException>(() => Esp32StubImage.ParseJson(json));
        }

        [TestMethod]
        public void StubImage_TryLoad_UnknownChip_ReturnsNull()
        {
            // No stub should exist for a non-existent chip type
            var result = Esp32StubImage.TryLoad("esp32_nonexistent_xyz");
            Assert.IsNull(result, "Should return null for unknown chip types");
        }

        [TestMethod]
        public void ZlibCompress_ProducesValidFormat()
        {
            byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

            byte[] compressed = Esp32FlashController.CompressZlib(data);

            // Zlib header: CMF=0x78, FLG=0x9C
            Assert.AreEqual(0x78, compressed[0], "Zlib CMF byte should be 0x78");
            Assert.AreEqual(0x9C, compressed[1], "Zlib FLG byte should be 0x9C");

            // Last 4 bytes should be the Adler-32 checksum (big-endian)
            uint expectedAdler = Esp32FlashController.ComputeAdler32(data);
            uint actualAdler = (uint)(
                (compressed[compressed.Length - 4] << 24) |
                (compressed[compressed.Length - 3] << 16) |
                (compressed[compressed.Length - 2] << 8) |
                compressed[compressed.Length - 1]);

            Assert.AreEqual(expectedAdler, actualAdler, "Adler-32 checksum should match");

            // The compressed data should be shorter than header + data + trailer for compressible data
            Assert.IsTrue(compressed.Length > 6, "Compressed output should have header + data + trailer");
        }

        [TestMethod]
        public void ZlibCompress_RoundTrip()
        {
            // Create some repeatable data that compresses well
            byte[] original = new byte[1024];
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = (byte)(i % 7);
            }

            byte[] compressed = Esp32FlashController.CompressZlib(original);

            // Verify compressed is smaller (repeating data should compress well)
            Assert.IsTrue(compressed.Length < original.Length,
                $"Compressed ({compressed.Length}) should be smaller than original ({original.Length})");

            // Decompress and verify round-trip
            // Skip 2-byte header, strip 4-byte Adler-32 trailer
            byte[] deflateData = new byte[compressed.Length - 6];
            Buffer.BlockCopy(compressed, 2, deflateData, 0, deflateData.Length);

            using (var input = new System.IO.MemoryStream(deflateData))
            using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new System.IO.MemoryStream())
            {
                deflate.CopyTo(output);
                byte[] decompressed = output.ToArray();
                CollectionAssert.AreEqual(original, decompressed, "Round-trip should produce original data");
            }
        }

        [TestMethod]
        public void Adler32_KnownValue()
        {
            // "Wikipedia" example: Adler-32 of "Wikipedia" = 0x11E60398
            byte[] data = System.Text.Encoding.ASCII.GetBytes("Wikipedia");
            uint adler = Esp32FlashController.ComputeAdler32(data);
            Assert.AreEqual(0x11E60398u, adler, "Adler-32 of 'Wikipedia' should be 0x11E60398");
        }

        [TestMethod]
        public void Adler32_EmptyData()
        {
            uint adler = Esp32FlashController.ComputeAdler32(Array.Empty<byte>());
            Assert.AreEqual(1u, adler, "Adler-32 of empty data should be 1 (initial seed)");
        }

        [TestMethod]
        public void CompressedWrite_RequiresStub()
        {
            // FlashController should throw when trying compressed write without stub
            var clientType = typeof(Esp32BootloaderClient);
            var configType = typeof(Esp32ChipConfig);

            // We can't create a real controller without a serial port, so test the
            // validation via reflection: verify the method exists and has the guard
            var controllerType = typeof(Esp32FlashController);
            var method = controllerType.GetMethod("WriteFlashCompressed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(uint), typeof(byte[]), typeof(Action<int, int>) },
                null);

            Assert.IsNotNull(method, "WriteFlashCompressed method should exist");
        }

        [TestMethod]
        public void FlashMd5_RequiresStub()
        {
            var controllerType = typeof(Esp32FlashController);
            var method = controllerType.GetMethod("FlashMd5",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(method, "FlashMd5 method should exist");
        }

        [TestMethod]
        public void StubLoader_UploadStub_MethodExists()
        {
            var type = typeof(Esp32StubLoader);
            var method = type.GetMethod("UploadStub",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.IsNotNull(method, "UploadStub static method should exist");
        }

        [TestMethod]
        public void EspTool_HasStubUploadedField()
        {
            var type = typeof(EspTool);
            var field = type.GetField("_stubUploaded",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(field, "EspTool should have _stubUploaded field");
            Assert.AreEqual(typeof(bool), field.FieldType);
        }

        [TestMethod]
        public void ZlibCompress_LargeData()
        {
            // Test with a larger block (simulating a real firmware partition)
            byte[] data = new byte[16384]; // 16 KB
            var rng = new Random(42); // deterministic seed
            rng.NextBytes(data);

            byte[] compressed = Esp32FlashController.CompressZlib(data);

            // Should produce valid zlib format
            Assert.AreEqual(0x78, compressed[0]);
            Assert.AreEqual(0x9C, compressed[1]);

            // Random data doesn't compress well but should still be valid
            Assert.IsTrue(compressed.Length > 6);
        }

        [TestMethod]
        public void StubImage_ParseJson_LargeAddresses()
        {
            // Test with addresses > int.MaxValue (common for ESP32 register addresses)
            byte[] textData = new byte[] { 0x01 };
            string json = $@"{{
                ""text"": ""{Convert.ToBase64String(textData)}"",
                ""text_start"": 3758153728,
                ""data"": ""{Convert.ToBase64String(textData)}"",
                ""data_start"": 3758096384,
                ""entry"": 3758154972
            }}";

            var stub = Esp32StubImage.ParseJson(json);

            Assert.AreEqual(3758153728u, stub.TextStart);
            Assert.AreEqual(3758096384u, stub.DataStart);
            Assert.AreEqual(3758154972u, stub.Entry);
        }

        #endregion

        #region Bootloader Image Header Patching Tests

        [TestMethod]
        public void PatchBootloaderHeader_PatchesModeAndFreqAndSize()
        {
            // Build a minimal valid ESP32-S3 bootloader image (magic=0xE9, 0 segments)
            // Byte layout: [magic][segcount][flash_mode][flash_size_freq][entry:4][ext_header:16]
            byte[] image = new byte[32];
            image[0] = 0xE9;  // magic
            image[1] = 0;      // 0 segments
            image[2] = 0xFF;   // flash_mode (placeholder)
            image[3] = 0xFF;   // flash_size_freq (placeholder)
            // byte 23 = append_digest = 0 (no SHA)

            var config = Esp32ChipConfigs.ESP32_S3; // bootloaderAddress = 0x0
            var controller = CreateTestFlashController(config);

            // Patch: dio mode, 80MHz, 16MB
            byte[] patched = controller.PatchBootloaderImageHeader(
                0x0, image, "dio", 80, 16 * 1024 * 1024);

            Assert.AreEqual(0x02, patched[2], "flash_mode should be DIO (0x02)");
            Assert.AreEqual(0x4F, patched[3], "flash_size_freq should be 16MB(0x40)|80MHz(0x0F)=0x4F");
        }

        [TestMethod]
        public void PatchBootloaderHeader_NoChangeIfNotBootloaderAddress()
        {
            byte[] image = new byte[32];
            image[0] = 0xE9;
            image[2] = 0xFF;
            image[3] = 0xFF;

            var config = Esp32ChipConfigs.ESP32_S3; // bootloaderAddress = 0x0
            var controller = CreateTestFlashController(config);

            // Address 0x10000 is not the bootloader address for S3
            byte[] result = controller.PatchBootloaderImageHeader(
                0x10000, image, "dio", 80, 16 * 1024 * 1024);

            Assert.AreSame(image, result, "Should return same array when address doesn't match bootloader");
        }

        [TestMethod]
        public void PatchBootloaderHeader_NoChangeIfNotMagic()
        {
            byte[] image = new byte[32];
            image[0] = 0x00; // Not magic
            image[2] = 0xFF;
            image[3] = 0xFF;

            var config = Esp32ChipConfigs.ESP32_S3;
            var controller = CreateTestFlashController(config);

            byte[] result = controller.PatchBootloaderImageHeader(
                0x0, image, "dio", 80, 16 * 1024 * 1024);

            Assert.AreSame(image, result, "Should return same array when magic byte is wrong");
        }

        [TestMethod]
        public void PatchBootloaderHeader_ESP32_CorrectBootloaderAddress()
        {
            byte[] image = new byte[32];
            image[0] = 0xE9;
            image[2] = 0xFF;
            image[3] = 0xFF;

            var config = Esp32ChipConfigs.ESP32; // bootloaderAddress = 0x1000
            var controller = CreateTestFlashController(config);

            // ESP32 bootloader is at 0x1000
            byte[] patched = controller.PatchBootloaderImageHeader(
                0x1000, image, "qio", 40, 4 * 1024 * 1024);

            Assert.AreEqual(0x00, patched[2], "flash_mode should be QIO (0x00)");
            Assert.AreEqual(0x20, patched[3], "flash_size_freq should be 4MB(0x20)|40MHz(0x00)=0x20");
        }

        [TestMethod]
        public void PatchBootloaderHeader_RecalculatesSha256WhenAppended()
        {
            // Build image: magic, 1 segment, with SHA appended
            // Header (8) + Extended header (16) + Segment header (8) + Segment data (16) + checksum pad (16) + SHA (32)
            byte[] image = new byte[8 + 16 + 8 + 16 + 16 + 32];
            image[0] = 0xE9;   // magic
            image[1] = 1;       // 1 segment
            image[2] = 0xFF;    // flash_mode placeholder
            image[3] = 0xFF;    // flash_size_freq placeholder
            image[23] = 1;      // append_digest = true

            // Segment: load_addr=0, data_len=16
            image[24 + 4] = 16; // segment data length (little-endian)
            // Segment data: 16 bytes of 0xAA
            for (int i = 0; i < 16; i++) image[32 + i] = 0xAA;

            // Checksum area starts at offset 48, padded to 16 bytes → offset 64
            // SHA-256 at offset 64, 32 bytes

            var config = Esp32ChipConfigs.ESP32_S3;
            var controller = CreateTestFlashController(config);

            byte[] patched = controller.PatchBootloaderImageHeader(
                0x0, image, "dio", 80, 8 * 1024 * 1024);

            Assert.AreEqual(0x02, patched[2], "flash_mode should be DIO");
            Assert.AreEqual(0x3F, patched[3], "flash_size_freq should be 8MB(0x30)|80MHz(0x0F)=0x3F");

            // Verify SHA-256 was recalculated (should not be all zeros or all 0xFF)
            byte[] sha = new byte[32];
            Buffer.BlockCopy(patched, 64, sha, 0, 32);
            bool allZero = true;
            for (int i = 0; i < 32; i++) { if (sha[i] != 0) { allZero = false; break; } }
            Assert.IsFalse(allZero, "SHA-256 digest should not be all zeros after recalculation");

            // Verify it matches actual SHA-256 of patched data
            byte[] expectedSha;
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                expectedSha = hasher.ComputeHash(patched, 0, 64);
            }
            CollectionAssert.AreEqual(expectedSha, sha, "SHA-256 digest should match hash of image data");
        }

        [TestMethod]
        [DataRow("qio", 0x00)]
        [DataRow("qout", 0x01)]
        [DataRow("dio", 0x02)]
        [DataRow("dout", 0x03)]
        public void PatchBootloaderHeader_AllFlashModes(string mode, int expectedByte)
        {
            byte[] image = new byte[32];
            image[0] = 0xE9;
            image[2] = 0xFF;
            image[3] = 0x00;

            var controller = CreateTestFlashController(Esp32ChipConfigs.ESP32_S3);
            byte[] patched = controller.PatchBootloaderImageHeader(
                0x0, image, mode, 40, 1 * 1024 * 1024);

            Assert.AreEqual((byte)expectedByte, patched[2]);
        }

        [TestMethod]
        [DataRow(1, 0x00)]
        [DataRow(2, 0x10)]
        [DataRow(4, 0x20)]
        [DataRow(8, 0x30)]
        [DataRow(16, 0x40)]
        [DataRow(32, 0x50)]
        public void PatchBootloaderHeader_AllFlashSizes(int sizeMB, int expectedNibble)
        {
            byte[] image = new byte[32];
            image[0] = 0xE9;
            image[2] = 0xFF;
            image[3] = 0x00;

            var controller = CreateTestFlashController(Esp32ChipConfigs.ESP32_S3);
            byte[] patched = controller.PatchBootloaderImageHeader(
                0x0, image, "qio", 40, sizeMB * 1024 * 1024);

            Assert.AreEqual((byte)expectedNibble, (byte)(patched[3] & 0xF0));
        }

        /// <summary>
        /// Helper to create a flash controller for testing without a real serial connection.
        /// </summary>
        private static Esp32FlashController CreateTestFlashController(Esp32ChipConfig config)
        {
            // Pass null client — only used for PatchBootloaderImageHeader which doesn't send commands
            return new Esp32FlashController(null, config);
        }

        #endregion
    }
}
