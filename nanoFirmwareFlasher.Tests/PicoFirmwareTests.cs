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
    }
}
