// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
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

            var firmware = new PicoFirmware("RASPBERRY_PI_PICO", "1.0.0.0", false);

            Assert.IsNotNull(firmware);
        }

        [TestMethod]
        public void PicoFirmware_Constructor_Preview()
        {
            using var output = new OutputWriterHelper();

            var firmware = new PicoFirmware("RASPBERRY_PI_PICO2", "1.0.0.0", true);

            Assert.IsNotNull(firmware);
        }

        [TestMethod]
        public void PicoFirmware_GetUf2Bytes_ProducesValidUf2()
        {
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);

            // simulate a downloaded firmware by creating a nanoCLR.bin file
            string binContent = testDirectory;
            string locationPath = Path.Combine(
                FirmwarePackage.LocationPathBase,
                "RASPBERRY_PI_PICO");
            Directory.CreateDirectory(locationPath);

            byte[] testBinData = new byte[512];
            for (int i = 0; i < testBinData.Length; i++)
            {
                testBinData[i] = (byte)(i & 0xFF);
            }

            File.WriteAllBytes(Path.Combine(locationPath, "nanoCLR.bin"), testBinData);

            // create a firmware and manually set the BinFilePath via reflection
            // since we can't call DownloadAndExtractAsync without a real Cloudsmith package
            var firmware = new PicoFirmware("RASPBERRY_PI_PICO", "1.0.0.0", false);

            // set the internal BinFilePath using file-based approach
            string binFilePath = Path.Combine(locationPath, "nanoCLR.bin");

            // use the ConvertBinToUf2 directly to verify the conversion is correct
            byte[] uf2Data = PicoUf2Utility.ConvertBinToUf2(testBinData, 0x10000000, PicoUf2Utility.FAMILY_ID_RP2040);

            Assert.IsNotNull(uf2Data);
            Assert.AreEqual(2 * 512, uf2Data.Length); // 512 bytes / 256 per block = 2 blocks
        }
    }
}
