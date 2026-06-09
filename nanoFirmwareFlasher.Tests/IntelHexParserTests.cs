// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class IntelHexParserTests
    {
        private string _testDir;

        [TestInitialize]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "nanoff_hex_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        #region Parse tests

        [TestMethod]
        public void Parse_SimpleDataRecords_ReturnsCorrectBlock()
        {
            // 16 bytes at address 0x0000
            byte[] data = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00 };
            string hexContent = MakeDataRecord(0x0000, data) + "\n" + MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(0u, blocks[0].Address);
            Assert.AreEqual(16, blocks[0].Data.Length);
            Assert.AreEqual(0x11, blocks[0].Data[0]);
            Assert.AreEqual(0x00, blocks[0].Data[15]);
        }

        [TestMethod]
        public void Parse_ExtendedLinearAddress_ComputesCorrectAbsoluteAddress()
        {
            // Extended Linear Address: upper 16 bits = 0x0800 → base 0x08000000
            byte[] data = { 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x11, 0x22, 0x33, 0x44 };
            string hexContent =
                MakeExtLinearAddress(0x0800) + "\n" +
                MakeDataRecord(0x0000, data) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(0x08000000u, blocks[0].Address);
            Assert.AreEqual(16, blocks[0].Data.Length);
            Assert.AreEqual(0xAA, blocks[0].Data[0]);
        }

        [TestMethod]
        public void Parse_ExtendedSegmentAddress_ComputesCorrectAddress()
        {
            // Extended Segment Address: 0x1000 → base = 0x1000 << 4 = 0x10000
            // Data at offset 0x0010 → absolute 0x10010
            byte[] data = { 0xDE, 0xAD, 0xBE, 0xEF };
            string hexContent =
                MakeExtSegmentAddress(0x1000) + "\n" +
                MakeDataRecord(0x0010, data) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(0x00010010u, blocks[0].Address);
            Assert.AreEqual(4, blocks[0].Data.Length);
            Assert.AreEqual(0xDE, blocks[0].Data[0]);
            Assert.AreEqual(0xAD, blocks[0].Data[1]);
            Assert.AreEqual(0xBE, blocks[0].Data[2]);
            Assert.AreEqual(0xEF, blocks[0].Data[3]);
        }

        [TestMethod]
        public void Parse_ContiguousRecords_CoalescedIntoSingleBlock()
        {
            // Two 16-byte data records at 0x0000 and 0x0010 (contiguous)
            byte[] data1 = new byte[16];
            byte[] data2 = new byte[16];
            for (int i = 0; i < 16; i++) { data1[i] = 0x11; data2[i] = 0x22; }

            string hexContent =
                MakeDataRecord(0x0000, data1) + "\n" +
                MakeDataRecord(0x0010, data2) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count, "Contiguous records should be coalesced into one block");
            Assert.AreEqual(0u, blocks[0].Address);
            Assert.AreEqual(32, blocks[0].Data.Length);
            Assert.AreEqual(0x11, blocks[0].Data[0]);
            Assert.AreEqual(0x22, blocks[0].Data[16]);
        }

        [TestMethod]
        public void Parse_NonContiguousRecords_ReturnsSeparateBlocks()
        {
            // 4 bytes at 0x0000 and 4 bytes at 0x0100 (gap)
            byte[] data1 = { 0xDE, 0xAD, 0xBE, 0xEF };
            byte[] data2 = { 0xCA, 0xFE, 0xBA, 0xBE };
            string hexContent =
                MakeDataRecord(0x0000, data1) + "\n" +
                MakeDataRecord(0x0100, data2) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(2, blocks.Count, "Non-contiguous records should produce separate blocks");
            Assert.AreEqual(0u, blocks[0].Address);
            Assert.AreEqual(0x100u, blocks[1].Address);
        }

        [TestMethod]
        public void Parse_EmptyFile_ReturnsEmptyList()
        {
            string hexContent = MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(0, blocks.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void Parse_NonExistentFile_ThrowsFileNotFound()
        {
            IntelHexParser.Parse(Path.Combine(_testDir, "nonexistent.hex"));
        }

        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void Parse_InvalidStartCode_ThrowsFormatException()
        {
            string hexContent = "INVALID_LINE\n";
            string hexFile = WriteHexFile(hexContent);

            IntelHexParser.Parse(hexFile);
        }

        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void Parse_BadChecksum_ThrowsFormatException()
        {
            // Valid format but manually corrupted checksum
            byte[] data = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00 };
            string goodRecord = MakeDataRecord(0x0000, data);
            // Corrupt the last two characters (checksum)
            string badRecord = goodRecord.Substring(0, goodRecord.Length - 2) + "FF";
            string hexContent = badRecord + "\n" + MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            IntelHexParser.Parse(hexFile);
        }

        #endregion

        #region ParseToFlatBinary tests

        [TestMethod]
        public void ParseToFlatBinary_SingleBlock_ReturnsCorrectData()
        {
            byte[] data = { 0xAA, 0xBB, 0xCC, 0xDD };
            string hexContent =
                MakeExtLinearAddress(0x0800) + "\n" +
                MakeDataRecord(0x0000, data) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            byte[] result = IntelHexParser.ParseToFlatBinary(hexFile, out uint startAddress);

            Assert.AreEqual(0x08000000u, startAddress);
            Assert.AreEqual(4, result.Length);
            Assert.AreEqual(0xAA, result[0]);
            Assert.AreEqual(0xDD, result[3]);
        }

        [TestMethod]
        public void ParseToFlatBinary_NonContiguousBlocks_GapsFilledWithFF()
        {
            // 4 bytes at 0x0000, 4 bytes at 0x0008 → gap at 0x0004-0x0007
            byte[] data1 = { 0xDE, 0xAD, 0xBE, 0xEF };
            byte[] data2 = { 0xCA, 0xFE, 0xBA, 0xBE };
            string hexContent =
                MakeDataRecord(0x0000, data1) + "\n" +
                MakeDataRecord(0x0008, data2) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            byte[] result = IntelHexParser.ParseToFlatBinary(hexFile, out uint startAddress);

            Assert.AreEqual(0u, startAddress);
            Assert.AreEqual(12, result.Length);

            // first block
            Assert.AreEqual(0xDE, result[0]);

            // gap filled with 0xFF
            Assert.AreEqual(0xFF, result[4]);
            Assert.AreEqual(0xFF, result[5]);
            Assert.AreEqual(0xFF, result[6]);
            Assert.AreEqual(0xFF, result[7]);

            // second block
            Assert.AreEqual(0xCA, result[8]);
        }

        [TestMethod]
        public void ParseToFlatBinary_EmptyFile_ReturnsEmpty()
        {
            string hexContent = MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            byte[] data = IntelHexParser.ParseToFlatBinary(hexFile, out uint startAddress);

            Assert.AreEqual(0u, startAddress);
            Assert.AreEqual(0, data.Length);
        }

        #endregion

        #region Checksum verification tests

        [TestMethod]
        public void Parse_ValidChecksums_Succeeds()
        {
            // Use the helper to generate a valid real-world-like record
            byte[] data = { 0x00, 0x20, 0x00, 0x20, 0xE1, 0x01, 0x00, 0x08, 0xE3, 0x01, 0x00, 0x08, 0xE5, 0x01, 0x00, 0x08 };
            string hexContent =
                MakeExtLinearAddress(0x0800) + "\n" +
                MakeDataRecord(0x0000, data) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            // Should not throw
            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.IsTrue(blocks.Count > 0);
        }

        #endregion

        #region Multiple Extended Address changes

        [TestMethod]
        public void Parse_MultipleExtendedAddressChanges_CorrectAddresses()
        {
            byte[] data1 = { 0xAA, 0xAA, 0x11, 0x11 };
            byte[] data2 = { 0xBB, 0xBB, 0x22, 0x22 };
            string hexContent =
                MakeExtLinearAddress(0x0800) + "\n" +
                MakeDataRecord(0x0000, data1) + "\n" +
                MakeExtLinearAddress(0x0801) + "\n" +
                MakeDataRecord(0x0000, data2) + "\n" +
                MakeEofRecord() + "\n";

            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(2, blocks.Count);
            Assert.AreEqual(0x08000000u, blocks[0].Address);
            Assert.AreEqual(0x08010000u, blocks[1].Address);
        }

        #endregion

        #region Record type 03/05 and edge case tests

        [TestMethod]
        public void Parse_StartSegmentAddress_Type03_IsIgnored()
        {
            // Type 03 record: CS:IP = 0x0000:0x1234 — should be silently ignored
            byte[] data = { 0xAA, 0xBB, 0xCC, 0xDD };
            string type03Record = MakeHexRecord(0x03, 0x0000, new byte[] { 0x00, 0x00, 0x12, 0x34 });
            string hexContent =
                MakeDataRecord(0x0000, data) + "\n" +
                type03Record + "\n" +
                MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(0u, blocks[0].Address);
            CollectionAssert.AreEqual(data, blocks[0].Data);
        }

        [TestMethod]
        public void Parse_StartLinearAddress_Type05_IsIgnored()
        {
            // Type 05 record: execution start address 0x08000000 — should be silently ignored
            byte[] data = { 0x11, 0x22, 0x33, 0x44 };
            string type05Record = MakeHexRecord(0x05, 0x0000, new byte[] { 0x08, 0x00, 0x00, 0x00 });
            string hexContent =
                MakeExtLinearAddress(0x0800) + "\n" +
                MakeDataRecord(0x0000, data) + "\n" +
                type05Record + "\n" +
                MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(0x08000000u, blocks[0].Address);
            CollectionAssert.AreEqual(data, blocks[0].Data);
        }

        [TestMethod]
        public void Parse_CrLfLineEndings_WorksCorrectly()
        {
            byte[] data = { 0xAA, 0xBB };
            string hexContent =
                MakeDataRecord(0x0000, data) + "\r\n" +
                MakeEofRecord() + "\r\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(2, blocks[0].Data.Length);
            Assert.AreEqual(0xAA, blocks[0].Data[0]);
        }

        [TestMethod]
        public void Parse_MixedLineEndings_WorksCorrectly()
        {
            byte[] data1 = { 0x11, 0x22 };
            byte[] data2 = { 0x33, 0x44 };
            string hexContent =
                MakeDataRecord(0x0000, data1) + "\r\n" +
                MakeDataRecord(0x0002, data2) + "\n" +
                MakeEofRecord() + "\r\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(4, blocks[0].Data.Length);
        }

        [TestMethod]
        public void Parse_UnknownRecordType_ThrowsFormatException()
        {
            // Type 0x06 is not defined in Intel HEX format
            string unknownRecord = MakeHexRecord(0x06, 0x0000, new byte[] { 0x00 });
            string hexContent =
                MakeDataRecord(0x0000, new byte[] { 0xAA }) + "\n" +
                unknownRecord + "\n" +
                MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            Assert.ThrowsException<FormatException>(() => IntelHexParser.Parse(hexFile));
        }

        [TestMethod]
        public void Parse_EmptyDataRecord_ProducesNoData()
        {
            // A valid HEX file with only an EOF record (no data)
            string hexContent = MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(0, blocks.Count);
        }

        [TestMethod]
        public void Parse_BothType03And05_BothIgnored()
        {
            // Both start address record types in one file — both should be silently ignored
            byte[] data = { 0xDE, 0xAD };
            string hexContent =
                MakeHexRecord(0x03, 0x0000, new byte[] { 0x00, 0x00, 0x00, 0x00 }) + "\n" +
                MakeDataRecord(0x0000, data) + "\n" +
                MakeHexRecord(0x05, 0x0000, new byte[] { 0x08, 0x00, 0x00, 0x00 }) + "\n" +
                MakeEofRecord() + "\n";
            string hexFile = WriteHexFile(hexContent);

            List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);

            Assert.AreEqual(1, blocks.Count);
            CollectionAssert.AreEqual(data, blocks[0].Data);
        }

        [TestMethod]
        public void Parse_TrailingWhitespace_IsHandled()
        {
            byte[] data = { 0x42 };
            // Add trailing spaces after the record
            string hexContent =
                MakeDataRecord(0x0000, data) + "  \n" +
                MakeEofRecord() + " \n";
            string hexFile = WriteHexFile(hexContent);

            // Should either parse successfully or throw a clear error — not crash
            try
            {
                List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFile);
                // If it parses, data should be correct
                Assert.AreEqual(1, blocks.Count);
            }
            catch (FormatException)
            {
                // Acceptable — parser is strict about format
            }
        }

        #endregion

        #region Helper methods

        private string WriteHexFile(string content)
        {
            string path = Path.Combine(_testDir, Guid.NewGuid().ToString("N") + ".hex");
            File.WriteAllText(path, content);
            return path;
        }

        /// <summary>
        /// Creates a valid Intel HEX record line with correct checksum.
        /// </summary>
        private static string MakeHexRecord(byte type, ushort address, byte[] data)
        {
            byte byteCount = (byte)data.Length;
            int sum = byteCount + (address >> 8) + (address & 0xFF) + type;

            string hex = $":{byteCount:X2}{address:X4}{type:X2}";

            foreach (byte b in data)
            {
                hex += $"{b:X2}";
                sum += b;
            }

            byte checksum = (byte)((-sum) & 0xFF);
            hex += $"{checksum:X2}";

            return hex;
        }

        private static string MakeDataRecord(ushort address, byte[] data)
        {
            return MakeHexRecord(0x00, address, data);
        }

        private static string MakeExtLinearAddress(ushort upperAddress)
        {
            return MakeHexRecord(0x04, 0x0000, new byte[] { (byte)(upperAddress >> 8), (byte)(upperAddress & 0xFF) });
        }

        private static string MakeExtSegmentAddress(ushort segAddress)
        {
            return MakeHexRecord(0x02, 0x0000, new byte[] { (byte)(segAddress >> 8), (byte)(segAddress & 0xFF) });
        }

        private static string MakeEofRecord()
        {
            return ":00000001FF";
        }

        #endregion
    }
}
