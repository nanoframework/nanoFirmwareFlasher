// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Esp32Serial;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Esp32ClrUpdateTests
    {
        private string _directory = null!;
        private string _imagePath = null!;
        private byte[] _table = null!;
        private int _reads;
        private int _writes;
        private Dictionary<int, string>? _writtenPartitions;
        private ExitCodes _writeResult;

        [TestInitialize]
        public void Initialize()
        {
            _directory = Path.Combine(Path.GetTempPath(), "nanoff-clr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _imagePath = Path.Combine(_directory, "nanoCLR.bin");
            File.WriteAllBytes(_imagePath, CreateImage());
            _table = CreateTable();
            _reads = 0;
            _writes = 0;
            _writtenPartitions = null;
            _writeResult = ExitCodes.OK;
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.Delete(_directory, true);
        }

        [TestMethod]
        [DataRow(0x10000)]
        [DataRow(0x20000)]
        [DataRow(0x30000)]
        public void UpdateClr_WritesOnlyInstalledClrPartition(int address)
        {
            Esp32CommandPacket.WriteUInt32LE(_table, 4, (uint)address);

            Assert.AreEqual(ExitCodes.OK, RunUpdate());
            Assert.AreEqual(1, _reads);
            Assert.AreEqual(1, _writes);
            Assert.IsNotNull(_writtenPartitions);
            Assert.AreEqual(1, _writtenPartitions.Count, "Bootloader, partition table, config and deployment must not be written.");
            Assert.AreEqual(Path.GetFullPath(_imagePath), _writtenPartitions[address]);
        }

        [TestMethod]
        public void UpdateClr_MissingFile_FailsBeforeReadingFlash()
        {
            File.Delete(_imagePath);

            Assert.AreEqual(ExitCodes.E9011, RunUpdate());
            Assert.AreEqual(0, _reads);
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_WrongExtension_FailsBeforeReadingFlash()
        {
            string path = Path.ChangeExtension(_imagePath, ".elf");
            File.Move(_imagePath, path);
            _imagePath = path;

            Assert.AreEqual(ExitCodes.E9012, RunUpdate());
            Assert.AreEqual(0, _reads);
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_ImageExactlyFits_IsAccepted()
        {
            byte[] image = CreateImage();
            Array.Resize(ref image, 0x1000);
            File.WriteAllBytes(_imagePath, image);
            Esp32CommandPacket.WriteUInt32LE(_table, 8, 0x1000);

            Assert.AreEqual(ExitCodes.OK, RunUpdate());
            Assert.AreEqual(1, _writes);
        }

        [TestMethod]
        public void UpdateClr_ImageOneByteTooLarge_DoesNotWrite()
        {
            File.WriteAllBytes(_imagePath, new byte[0x1001]);
            Esp32CommandPacket.WriteUInt32LE(_table, 8, 0x1000);

            Assert.ThrowsExactly<EspToolExecutionException>(() => RunUpdate());
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        [DataRow(0x21000, 0x10000)]
        [DataRow(0x400000, 0x10000)]
        [DataRow(0x20000, 0)]
        public void UpdateClr_InvalidPartitionRange_DoesNotWrite(int address, int size)
        {
            Esp32CommandPacket.WriteUInt32LE(_table, 4, (uint)address);
            Esp32CommandPacket.WriteUInt32LE(_table, 8, (uint)size);

            Assert.ThrowsExactly<EspToolExecutionException>(() => RunUpdate());
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_MissingFactoryPartition_DoesNotWrite()
        {
            _table[3] = 0x10;

            Assert.ThrowsExactly<EspToolExecutionException>(() => RunUpdate());
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_DuplicateFactoryPartition_DoesNotWrite()
        {
            Buffer.BlockCopy(_table, 0, _table, 96, 32);

            Assert.ThrowsExactly<EspToolExecutionException>(() => RunUpdate());
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_EncryptedPartition_DoesNotWrite()
        {
            Esp32CommandPacket.WriteUInt32LE(_table, 28, 1);

            Assert.ThrowsExactly<EspToolExecutionException>(() => RunUpdate());
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_OverlappingDeploymentPartition_DoesNotWrite()
        {
            Esp32CommandPacket.WriteUInt32LE(_table, 8, 0x30000);

            Assert.ThrowsExactly<EspToolExecutionException>(() => RunUpdate());
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_InvalidImage_DoesNotWrite()
        {
            File.WriteAllBytes(_imagePath, new byte[48]);

            Assert.ThrowsExactly<EspToolExecutionException>(() => RunUpdate());
            Assert.AreEqual(0, _writes);
        }

        [TestMethod]
        public void UpdateClr_WriteFailure_IsPropagated()
        {
            _writeResult = ExitCodes.E4003;

            Assert.AreEqual(ExitCodes.E4003, RunUpdate());
            Assert.AreEqual(1, _writes);
        }

        [TestMethod]
        public void UpdateClr_TableReadFailure_DoesNotWriteOrFallback()
        {
            Assert.ThrowsExactly<ReadEsp32FlashException>(() => Esp32Operations.UpdateClr(
                CreateDevice(),
                _imagePath,
                VerbosityLevel.Quiet,
                () => throw new ReadEsp32FlashException("Read failed."),
                _ => throw new AssertFailedException("No writes are allowed after a failed partition read.")));
        }

        [TestMethod]
        [DataRow("ESP32", 0)]
        [DataRow("ESP32-S2", 2)]
        [DataRow("ESP32-S3", 9)]
        public void ValidateApplicationImage_ValidImageForChip_IsAccepted(string chipType, int chipId)
        {
            Esp32FlashController.ValidateApplicationImage(CreateImage(chipId), chipType);
        }

        [TestMethod]
        public void ValidateApplicationImage_WrongChip_Throws()
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32FlashController.ValidateApplicationImage(CreateImage(2), "ESP32"));
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 0)]
        [DataRow(1, 17)]
        [DataRow(23, 2)]
        [DataRow(32, 0)]
        [DataRow(47, 0)]
        public void ValidateApplicationImage_CorruptedHeaderOrChecksum_Throws(int offset, int value)
        {
            byte[] image = CreateImage();
            image[offset] = (byte)value;

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32FlashController.ValidateApplicationImage(image, "ESP32"));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(23)]
        [DataRow(24)]
        [DataRow(31)]
        [DataRow(35)]
        [DataRow(47)]
        public void ValidateApplicationImage_TruncatedImage_Throws(int length)
        {
            byte[] image = CreateImage();
            Array.Resize(ref image, length);

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32FlashController.ValidateApplicationImage(image, "ESP32"));
        }

        [TestMethod]
        public void ValidateApplicationImage_SegmentLengthOverflow_Throws()
        {
            byte[] image = CreateImage();
            Esp32CommandPacket.WriteUInt32LE(image, 28, uint.MaxValue);

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32FlashController.ValidateApplicationImage(image, "ESP32"));
        }

        [TestMethod]
        public void ValidateApplicationImage_ValidSha256_IsAccepted()
        {
            Esp32FlashController.ValidateApplicationImage(CreateImage(includeSha256: true), "ESP32");
        }

        [TestMethod]
        public void ValidateApplicationImage_CorruptSha256_Throws()
        {
            byte[] image = CreateImage(includeSha256: true);
            image[79] ^= 1;

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32FlashController.ValidateApplicationImage(image, "ESP32"));
        }

        [TestMethod]
        public void ValidateApplicationImage_TruncatedSha256_Throws()
        {
            byte[] image = CreateImage(includeSha256: true);
            Array.Resize(ref image, 79);

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32FlashController.ValidateApplicationImage(image, "ESP32"));
        }

        private ExitCodes RunUpdate()
            => Esp32Operations.UpdateClr(
                CreateDevice(),
                _imagePath,
                VerbosityLevel.Quiet,
                () =>
                {
                    _reads++;
                    return _table;
                },
                partitions =>
                {
                    _writes++;
                    _writtenPartitions = partitions;
                    return _writeResult;
                });

        private static Esp32DeviceInfo CreateDevice()
            => new Esp32DeviceInfo("ESP32", "ESP32", "", "40MHz", "", 0, 0, 0x400000, PSRamAvailability.No, 0);

        private static byte[] CreateImage(int chipId = 0, bool includeSha256 = false)
        {
            byte[] image = new byte[includeSha256 ? 80 : 48];
            image[0] = 0xE9;
            image[1] = 1;
            image[12] = (byte)chipId;
            image[13] = (byte)(chipId >> 8);
            image[23] = includeSha256 ? (byte)1 : (byte)0;
            Esp32CommandPacket.WriteUInt32LE(image, 24, 0x3F400000);
            Esp32CommandPacket.WriteUInt32LE(image, 28, 4);
            image[32] = 1;
            image[33] = 2;
            image[34] = 3;
            image[35] = 4;
            image[47] = 0xEF ^ 1 ^ 2 ^ 3 ^ 4;
            if (includeSha256)
            {
                using SHA256 sha256 = SHA256.Create();
                sha256.ComputeHash(image, 0, 48).CopyTo(image, 48);
            }

            return image;
        }

        private static byte[] CreateTable()
        {
            byte[] table = new byte[Esp32PartitionTable.Size];
            for (int i = 0; i < table.Length; i++)
            {
                table[i] = 0xFF;
            }

            WriteEntry(table, 0, 0, 0, 0x20000, 0x10000, "factory");
            WriteEntry(table, 32, 1, 0x84, 0x40000, 0x10000, "deploy");
            WriteEntry(table, 64, 1, 0x83, 0x50000, 0x10000, "config");
            return table;
        }

        private static void WriteEntry(byte[] table, int position, byte type, byte subtype, uint address, uint size, string name)
        {
            Array.Clear(table, position, 32);
            table[position] = 0xAA;
            table[position + 1] = 0x50;
            table[position + 2] = type;
            table[position + 3] = subtype;
            Esp32CommandPacket.WriteUInt32LE(table, position + 4, address);
            Esp32CommandPacket.WriteUInt32LE(table, position + 8, size);
            Encoding.ASCII.GetBytes(name).CopyTo(table, position + 12);
        }
    }
}
