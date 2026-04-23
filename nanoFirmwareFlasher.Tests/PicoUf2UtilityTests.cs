// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class PicoUf2UtilityTests
    {
        public TestContext TestContext { get; set; } = null!;

        #region ConvertBinToUf2 tests

        [TestMethod]
        public void ConvertBinToUf2_EmptyInput_ReturnsEmpty()
        {
            byte[] result = PicoUf2Utility.ConvertBinToUf2([], 0x10000000, PicoUf2Utility.FAMILY_ID_RP2040);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [TestMethod]
        public void ConvertBinToUf2_SingleByte_ProducesSingleBlock()
        {
            byte[] input = [0xAB];
            byte[] result = PicoUf2Utility.ConvertBinToUf2(input, 0x10000000, PicoUf2Utility.FAMILY_ID_RP2040);

            Assert.AreEqual(512, result.Length);

            // verify magic numbers
            Assert.AreEqual(0x0A324655u, BitConverter.ToUInt32(result, 0));   // magic start 0
            Assert.AreEqual(0x9E5D5157u, BitConverter.ToUInt32(result, 4));   // magic start 1
            Assert.AreEqual(0x0AB16F30u, BitConverter.ToUInt32(result, 508)); // magic end

            // verify flags (family ID present)
            Assert.AreEqual(0x00002000u, BitConverter.ToUInt32(result, 8));

            // verify address
            Assert.AreEqual(0x10000000u, BitConverter.ToUInt32(result, 12));

            // verify payload size (actual data length for partial block)
            Assert.AreEqual(1u, BitConverter.ToUInt32(result, 16));

            // verify block index and total
            Assert.AreEqual(0u, BitConverter.ToUInt32(result, 20)); // block 0
            Assert.AreEqual(1u, BitConverter.ToUInt32(result, 24)); // 1 block total

            // verify family ID
            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2040, BitConverter.ToUInt32(result, 28));

            // verify payload data
            Assert.AreEqual(0xAB, result[32]);

            // verify padding is zero
            for (int i = 33; i < 508; i++)
            {
                Assert.AreEqual(0, result[i], $"Byte at offset {i} should be zero padding");
            }
        }

        [TestMethod]
        public void ConvertBinToUf2_ExactlyOneBlock_256Bytes()
        {
            byte[] input = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                input[i] = (byte)(i & 0xFF);
            }

            byte[] result = PicoUf2Utility.ConvertBinToUf2(input, 0x10000000, PicoUf2Utility.FAMILY_ID_RP2350_ARM);

            Assert.AreEqual(512, result.Length);

            // verify payload size
            Assert.AreEqual(256u, BitConverter.ToUInt32(result, 16));

            // verify block counts
            Assert.AreEqual(0u, BitConverter.ToUInt32(result, 20));
            Assert.AreEqual(1u, BitConverter.ToUInt32(result, 24));

            // verify family ID
            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2350_ARM, BitConverter.ToUInt32(result, 28));

            // verify all 256 payload bytes
            for (int i = 0; i < 256; i++)
            {
                Assert.AreEqual(input[i], result[32 + i], $"Payload byte {i} mismatch");
            }
        }

        [TestMethod]
        public void ConvertBinToUf2_MultipleBlocks_CorrectCount()
        {
            // 513 bytes should produce 3 blocks (256 + 256 + 1)
            byte[] input = new byte[513];
            input[0] = 0x01;
            input[256] = 0x02;
            input[512] = 0x03;

            byte[] result = PicoUf2Utility.ConvertBinToUf2(input, 0x10000000, PicoUf2Utility.FAMILY_ID_RP2040);

            Assert.AreEqual(3 * 512, result.Length);

            // verify block 0
            Assert.AreEqual(0u, BitConverter.ToUInt32(result, 20));
            Assert.AreEqual(3u, BitConverter.ToUInt32(result, 24));
            Assert.AreEqual(0x10000000u, BitConverter.ToUInt32(result, 12));
            Assert.AreEqual(256u, BitConverter.ToUInt32(result, 16));
            Assert.AreEqual(0x01, result[32]);

            // verify block 1
            int block1 = 512;
            Assert.AreEqual(1u, BitConverter.ToUInt32(result, block1 + 20));
            Assert.AreEqual(3u, BitConverter.ToUInt32(result, block1 + 24));
            Assert.AreEqual(0x10000100u, BitConverter.ToUInt32(result, block1 + 12)); // base + 256
            Assert.AreEqual(256u, BitConverter.ToUInt32(result, block1 + 16));
            Assert.AreEqual(0x02, result[block1 + 32]);

            // verify block 2
            int block2 = 2 * 512;
            Assert.AreEqual(2u, BitConverter.ToUInt32(result, block2 + 20));
            Assert.AreEqual(3u, BitConverter.ToUInt32(result, block2 + 24));
            Assert.AreEqual(0x10000200u, BitConverter.ToUInt32(result, block2 + 12)); // base + 512
            Assert.AreEqual(1u, BitConverter.ToUInt32(result, block2 + 16)); // actual data length for final partial block
            Assert.AreEqual(0x03, result[block2 + 32]);

            // verify end magic on all blocks
            Assert.AreEqual(0x0AB16F30u, BitConverter.ToUInt32(result, 508));
            Assert.AreEqual(0x0AB16F30u, BitConverter.ToUInt32(result, block1 + 508));
            Assert.AreEqual(0x0AB16F30u, BitConverter.ToUInt32(result, block2 + 508));
        }

        [TestMethod]
        public void ConvertBinToUf2_AddressProgression()
        {
            // 512 bytes = 2 blocks, verify addresses increment by 256
            byte[] input = new byte[512];
            uint baseAddress = 0x10000000;

            byte[] result = PicoUf2Utility.ConvertBinToUf2(input, baseAddress, PicoUf2Utility.FAMILY_ID_RP2040);

            Assert.AreEqual(2 * 512, result.Length);
            Assert.AreEqual(baseAddress, BitConverter.ToUInt32(result, 12));
            Assert.AreEqual(baseAddress + 256, BitConverter.ToUInt32(result, 512 + 12));
        }

        [TestMethod]
        public void ConvertBinToUf2_RP2350FamilyId()
        {
            byte[] input = [0xFF];
            byte[] result = PicoUf2Utility.ConvertBinToUf2(input, 0x10000000, PicoUf2Utility.FAMILY_ID_RP2350_ARM);

            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2350_ARM, BitConverter.ToUInt32(result, 28));
        }

        [TestMethod]
        public void ConvertBinToUf2_RoundTrip_DataIntegrity()
        {
            // generate a realistic firmware size (e.g. 1KB)
            byte[] input = new byte[1024];
            var rng = new Random(42);
            rng.NextBytes(input);

            byte[] uf2 = PicoUf2Utility.ConvertBinToUf2(input, 0x10000000, PicoUf2Utility.FAMILY_ID_RP2040);

            Assert.AreEqual(4 * 512, uf2.Length); // 1024 / 256 = 4 blocks

            byte[] extracted = PicoUf2Utility.ExtractBinFromUf2(uf2);

            Assert.IsNotNull(extracted);
            CollectionAssert.AreEqual(input, extracted);
        }

        #endregion

        #region DeployUf2File tests

        [TestMethod]
        public void DeployUf2File_WritesToDrive()
        {
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string drivePath = Path.Combine(testDirectory, "fakeDrive");
            Directory.CreateDirectory(drivePath);

            byte[] uf2Data = PicoUf2Utility.ConvertBinToUf2([0x01, 0x02, 0x03], 0x10000000, PicoUf2Utility.FAMILY_ID_RP2040);

            ExitCodes result = PicoUf2Utility.DeployUf2File(uf2Data, drivePath);

            Assert.AreEqual(ExitCodes.OK, result);
            Assert.IsTrue(File.Exists(Path.Combine(drivePath, "firmware.uf2")));

            byte[] written = File.ReadAllBytes(Path.Combine(drivePath, "firmware.uf2"));
            CollectionAssert.AreEqual(uf2Data, written);
        }

        [TestMethod]
        public void DeployUf2File_InvalidPath_ReturnsError()
        {
            string invalidPath = Path.Combine("Z:\\", "nonexistent_drive_path_for_test", Guid.NewGuid().ToString());

            ExitCodes result = PicoUf2Utility.DeployUf2File([0x01], invalidPath);

            Assert.AreEqual(ExitCodes.E3002, result);
        }

        #endregion

        #region DetectDevice tests

        [TestMethod]
        public void DetectDevice_RP2040_ParsesInfoFile()
        {
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string drivePath = Path.Combine(testDirectory, "RPI-RP2");
            Directory.CreateDirectory(drivePath);

            File.WriteAllText(Path.Combine(drivePath, "INFO_UF2.TXT"),
                "UF2 Bootloader v3.0\r\n" +
                "Model: Raspberry Pi RP2040\r\n" +
                "Board-ID: RPI-RP2\r\n");

            PicoDeviceInfo result = PicoUf2Utility.DetectDevice(drivePath);

            Assert.IsNotNull(result);
            Assert.AreEqual("RP2040", result.ChipType);
            Assert.AreEqual("RPI-RP2", result.BoardId);
            Assert.AreEqual("UF2 Bootloader v3.0", result.BootloaderVersion);
            Assert.AreEqual(drivePath, result.DrivePath);
            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2040, result.FamilyId);
        }

        [TestMethod]
        public void DetectDevice_RP2350_ParsesInfoFile()
        {
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string drivePath = Path.Combine(testDirectory, "RP2350");
            Directory.CreateDirectory(drivePath);

            File.WriteAllText(Path.Combine(drivePath, "INFO_UF2.TXT"),
                "UF2 Bootloader v1.0\r\n" +
                "Model: Raspberry Pi RP2350\r\n" +
                "Board-ID: RP2350\r\n");

            PicoDeviceInfo result = PicoUf2Utility.DetectDevice(drivePath);

            Assert.IsNotNull(result);
            Assert.AreEqual("RP2350", result.ChipType);
            Assert.AreEqual("RP2350", result.BoardId);
            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2350_ARM, result.FamilyId);
        }

        [TestMethod]
        public void DetectDevice_RP2040_NoModel_InfersFromBoardId()
        {
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string drivePath = Path.Combine(testDirectory, "RPI-RP2");
            Directory.CreateDirectory(drivePath);

            File.WriteAllText(Path.Combine(drivePath, "INFO_UF2.TXT"),
                "UF2 Bootloader v3.0\r\n" +
                "Board-ID: RPI-RP2\r\n");

            PicoDeviceInfo result = PicoUf2Utility.DetectDevice(drivePath);

            Assert.IsNotNull(result);
            Assert.AreEqual("RP2040", result.ChipType);
            Assert.AreEqual("RPI-RP2", result.BoardId);
            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2040, result.FamilyId);
        }

        [TestMethod]
        public void DetectDevice_RP2350_NoModel_InfersFromBoardId()
        {
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string drivePath = Path.Combine(testDirectory, "RP2350");
            Directory.CreateDirectory(drivePath);

            File.WriteAllText(Path.Combine(drivePath, "INFO_UF2.TXT"),
                "UF2 Bootloader v1.0\r\n" +
                "Board-ID: SOME_BOARD_RP2350\r\n");

            PicoDeviceInfo result = PicoUf2Utility.DetectDevice(drivePath);

            Assert.IsNotNull(result);
            Assert.AreEqual("RP2350", result.ChipType);
            Assert.AreEqual("SOME_BOARD_RP2350", result.BoardId);
            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2350_ARM, result.FamilyId);
        }

        [TestMethod]
        public void DetectDevice_MissingInfoFile_ReturnsNull()
        {
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string drivePath = Path.Combine(testDirectory, "empty_drive");
            Directory.CreateDirectory(drivePath);

            PicoDeviceInfo result = PicoUf2Utility.DetectDevice(drivePath);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void DetectDevice_UnknownModel_ReturnsNull()
        {
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string drivePath = Path.Combine(testDirectory, "minimal");
            Directory.CreateDirectory(drivePath);

            File.WriteAllText(Path.Combine(drivePath, "INFO_UF2.TXT"), "Some unknown content\r\n");

            PicoDeviceInfo result = PicoUf2Utility.DetectDevice(drivePath);

            Assert.IsNull(result);
        }

        #endregion

        #region PicoDeviceInfo tests

        [TestMethod]
        public void PicoDeviceInfo_FamilyId_RP2040()
        {
            var info = new PicoDeviceInfo("RP2040", "RPI-RP2", "v3.0", "/media/RPI-RP2", "RPI-RP2");

            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2040, info.FamilyId);
        }

        [TestMethod]
        public void PicoDeviceInfo_FamilyId_RP2350()
        {
            var info = new PicoDeviceInfo("RP2350", "RP2350", "v1.0", "/Volumes/RP2350", "RP2350");

            Assert.AreEqual(PicoUf2Utility.FAMILY_ID_RP2350_ARM, info.FamilyId);
        }

        [TestMethod]
        public void PicoDeviceInfo_ToString_ContainsAllFields()
        {
            var info = new PicoDeviceInfo("RP2040", "RPI-RP2", "v3.0", "D:\\", "RPI-RP2");
            string output = info.ToString();

            Assert.IsTrue(output.Contains("RP2040"));
            Assert.IsTrue(output.Contains("RPI-RP2"));
            Assert.IsTrue(output.Contains("v3.0"));
        }

        #endregion

        #region Family ID constants

        [TestMethod]
        public void FamilyId_RP2040_MatchesSpec()
        {
            Assert.AreEqual(0xE48BFF56u, PicoUf2Utility.FAMILY_ID_RP2040);
        }

        [TestMethod]
        public void FamilyId_RP2350_MatchesSpec()
        {
            Assert.AreEqual(0xE48BFF59u, PicoUf2Utility.FAMILY_ID_RP2350_ARM);
        }

        #endregion

        #region PadUf2ToFullFlash tests

        [TestMethod]
        public void PadUf2ToFullFlash_NullInput_CoversEntireFlash()
        {
            uint flashBase = 0x10000000;
            uint flashSize = 1024; // 4 blocks of 256
            uint familyId = PicoUf2Utility.FAMILY_ID_RP2040;

            byte[] result = PicoUf2Utility.PadUf2ToFullFlash(null, flashBase, flashSize, familyId);

            int expectedBlocks = (int)(flashSize / 256);
            Assert.AreEqual(expectedBlocks * 512, result.Length);

            // every block should be valid UF2 with zero payload
            for (int i = 0; i < expectedBlocks; i++)
            {
                int off = i * 512;
                Assert.AreEqual(0x0A324655u, BitConverter.ToUInt32(result, off));       // magic start 0
                Assert.AreEqual(0x9E5D5157u, BitConverter.ToUInt32(result, off + 4));   // magic start 1
                Assert.AreEqual(0x0AB16F30u, BitConverter.ToUInt32(result, off + 508)); // magic end
                Assert.AreEqual((uint)i, BitConverter.ToUInt32(result, off + 20));       // block index
                Assert.AreEqual((uint)expectedBlocks, BitConverter.ToUInt32(result, off + 24)); // total blocks
                Assert.AreEqual(flashBase + (uint)(i * 256), BitConverter.ToUInt32(result, off + 12)); // address
                Assert.AreEqual(familyId, BitConverter.ToUInt32(result, off + 28));

                // data area should be all zeros
                for (int d = 0; d < 256; d++)
                {
                    Assert.AreEqual(0, result[off + 32 + d], $"Block {i} data byte {d} should be zero");
                }
            }
        }

        [TestMethod]
        public void PadUf2ToFullFlash_PreservesExistingBlockPayload()
        {
            uint flashBase = 0x10000000;
            uint flashSize = 768; // 3 blocks of 256
            uint familyId = PicoUf2Utility.FAMILY_ID_RP2040;

            // create a 1-block UF2 at the second address (flashBase + 256)
            byte[] firmware = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                firmware[i] = (byte)(i ^ 0xAA);
            }

            byte[] inputUf2 = PicoUf2Utility.ConvertBinToUf2(firmware, flashBase + 256, familyId);
            Assert.AreEqual(512, inputUf2.Length); // sanity: 1 block

            byte[] result = PicoUf2Utility.PadUf2ToFullFlash(inputUf2, flashBase, flashSize, familyId);

            int expectedBlocks = 3;
            Assert.AreEqual(expectedBlocks * 512, result.Length);

            // block 1 (flashBase + 256) should carry the original payload
            int block1Off = 1 * 512;
            for (int i = 0; i < 256; i++)
            {
                Assert.AreEqual(firmware[i], result[block1Off + 32 + i],
                    $"Preserved payload byte {i} mismatch");
            }

            // blocks 0 and 2 should be zero-filled data
            foreach (int blockIdx in new[] { 0, 2 })
            {
                int off = blockIdx * 512;
                for (int d = 0; d < 256; d++)
                {
                    Assert.AreEqual(0, result[off + 32 + d],
                        $"Pad block {blockIdx} data byte {d} should be zero");
                }
            }
        }

        [TestMethod]
        public void PadUf2ToFullFlash_RenumbersAllBlocks()
        {
            uint flashBase = 0x10000000;
            uint flashSize = 1024; // 4 blocks
            uint familyId = PicoUf2Utility.FAMILY_ID_RP2040;

            // input UF2 with 1 block — its original numbering is 0/1
            byte[] inputUf2 = PicoUf2Utility.ConvertBinToUf2(new byte[256], flashBase, familyId);

            byte[] result = PicoUf2Utility.PadUf2ToFullFlash(inputUf2, flashBase, flashSize, familyId);

            int expectedBlocks = 4;

            for (int i = 0; i < expectedBlocks; i++)
            {
                int off = i * 512;
                uint blockNo = BitConverter.ToUInt32(result, off + 20);
                uint totalBlocks = BitConverter.ToUInt32(result, off + 24);

                Assert.AreEqual((uint)i, blockNo, $"Block {i} index mismatch");
                Assert.AreEqual((uint)expectedBlocks, totalBlocks, $"Block {i} total mismatch");
            }
        }

        [TestMethod]
        public void PadUf2ToFullFlash_AddressesAreContiguous()
        {
            uint flashBase = 0x10000000;
            uint flashSize = 2048; // 8 blocks
            uint familyId = PicoUf2Utility.FAMILY_ID_RP2350_ARM;

            byte[] result = PicoUf2Utility.PadUf2ToFullFlash(null, flashBase, flashSize, familyId);

            int expectedBlocks = 8;

            for (int i = 0; i < expectedBlocks; i++)
            {
                uint addr = BitConverter.ToUInt32(result, i * 512 + 12);
                Assert.AreEqual(flashBase + (uint)(i * 256), addr, $"Block {i} address mismatch");
            }
        }

        [TestMethod]
        public void PadUf2ToFullFlash_ResultPassesValidation()
        {
            using var output = new OutputWriterHelper();

            uint flashBase = 0x10000000;
            uint flashSize = 1024;
            uint familyId = PicoUf2Utility.FAMILY_ID_RP2040;

            byte[] inputUf2 = PicoUf2Utility.ConvertBinToUf2(new byte[128], flashBase, familyId);
            byte[] result = PicoUf2Utility.PadUf2ToFullFlash(inputUf2, flashBase, flashSize, familyId);

            bool valid = PicoUf2Utility.ValidateUf2Data(result, VerbosityLevel.Diagnostic);

            Assert.IsTrue(valid, "Padded UF2 should pass validation");
        }

        #endregion
    }
}
