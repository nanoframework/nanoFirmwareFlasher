// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class PicoFirmwareTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void PicoFirmware_Constructor_SetsProperties()
        {
            using var output = new OutputWriterHelper();

            var firmware = new PicoFirmware("RP_PICO_RP2040", "1.0.0.0", false);

            Assert.IsNotNull(firmware);
        }

        [TestMethod]
        public void PicoFirmware_Constructor_Preview()
        {
            using var output = new OutputWriterHelper();

            var firmware = new PicoFirmware("RP_PICO_RP2350", "1.0.0.0", true);

            Assert.IsNotNull(firmware);
        }

        [TestMethod]
        public void PicoFirmware_GetUf2Bytes_ProducesValidUf2()
        {
            using var output = new OutputWriterHelper();

            // simulate a downloaded firmware by creating a nanoCLR.bin file
            string locationPath = Path.Combine(
                FirmwarePackage.LocationPathBase,
                "RP_PICO_RP2040");
            Directory.CreateDirectory(locationPath);

            byte[] testBinData = new byte[512];
            for (int i = 0; i < testBinData.Length; i++)
            {
                testBinData[i] = (byte)(i & 0xFF);
            }

            string binFilePath = Path.Combine(locationPath, "nanoCLR.bin");
            File.WriteAllBytes(binFilePath, testBinData);

            // create a firmware and set BinFilePath via reflection
            // since we can't call DownloadAndExtractAsync without a real Cloudsmith package
            var firmware = new PicoFirmware("RP_PICO_RP2040", "1.0.0.0", false);

            typeof(PicoFirmware)
                .GetProperty("BinFilePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(firmware, binFilePath);

            // exercise GetUf2Bytes
            byte[] uf2Data = firmware.GetUf2Bytes(PicoUf2Utility.FAMILY_ID_RP2040);

            Assert.IsNotNull(uf2Data);
            Assert.AreEqual(2 * 512, uf2Data.Length); // 512 bytes / 256 per block = 2 blocks
        }

        [TestMethod]
        public async Task PicoFirmware_DownloadAndExtractAsync_ReextractsWhenUf2Exists()
        {
            using var output = new OutputWriterHelper();

            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            Directory.CreateDirectory(archiveDirectory);

            // isolate the firmware cache so the test doesn't touch ~/.nanoFramework/fw_cache
            FirmwarePackage.LocationPathBase = Path.Combine(testDirectory, "cache");

            string targetName = "RP_PICO_RP2040";
            string version = "1.0.0.0";
            string zipFilePath = Path.Combine(archiveDirectory, $"{targetName}-{version}.zip");

            using (var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry binEntry = archive.CreateEntry("nanoCLR.bin");

                using (var entryStream = binEntry.Open())
                {
                    entryStream.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 }, 0, 4);
                }

                ZipArchiveEntry uf2Entry = archive.CreateEntry("nanoCLR.uf2");

                using (var entryStream = uf2Entry.Open())
                {
                    entryStream.Write(new byte[] { 0x55, 0x46, 0x32, 0x0A }, 0, 4);
                }
            }

            var firmware = new PicoFirmware(targetName, version, false);
            ExitCodes exitCode = await firmware.DownloadAndExtractAsync(archiveDirectory);
            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsNotNull(firmware.Uf2FilePath);
            Assert.IsTrue(firmware.Uf2FilePath.EndsWith("nanoCLR.uf2"));

            firmware = new PicoFirmware(targetName, version, false);
            exitCode = await firmware.DownloadAndExtractAsync(archiveDirectory);
            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsNotNull(firmware.Uf2FilePath);
            Assert.IsTrue(firmware.Uf2FilePath.EndsWith("nanoCLR.uf2"));
            
            string cacheDirectory = Path.Combine(FirmwarePackage.LocationPathBase, targetName);
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, "nanoCLR.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, "nanoCLR.uf2")));
        }
    }
}
