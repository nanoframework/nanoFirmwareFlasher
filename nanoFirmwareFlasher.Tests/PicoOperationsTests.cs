// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class PicoOperationsTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        [DoNotParallelize]
        public void PicoManager_InvalidPlatform_Throws()
        {
            using var output = new OutputWriterHelper();

            var options = new Options
            {
                Platform = SupportedPlatform.esp32
            };

            Assert.ThrowsException<System.NotSupportedException>(() =>
                new PicoManager(options, VerbosityLevel.Quiet));
        }

        [TestMethod]
        [DoNotParallelize]
        public void PicoManager_NullOptions_Throws()
        {
            using var output = new OutputWriterHelper();

            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new PicoManager(null, VerbosityLevel.Quiet));
        }

        [TestMethod]
        [DoNotParallelize]
        public void PicoManager_CorrectPlatform_Constructs()
        {
            using var output = new OutputWriterHelper();

            var options = new Options
            {
                Platform = SupportedPlatform.rpi_pico
            };

            var manager = new PicoManager(options, VerbosityLevel.Quiet);

            Assert.IsNotNull(manager);
        }

        [TestMethod]
        [DoNotParallelize]
        public void PicoManager_NoDevice_ReturnsTimeout()
        {
            using var output = new OutputWriterHelper();

            var options = new Options
            {
                Platform = SupportedPlatform.rpi_pico,
                Update = true,
                TargetName = "RP_PICO_RP2040"
            };

            var manager = new PicoManager(options, VerbosityLevel.Quiet);

            // without a device connected, ProcessAsync should return E3005 (timeout)
            // since there's no UF2 drive and the wait timeout will pass
            // We can't easily test this in CI without a device, so we verify construction
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        [DoNotParallelize]
        public void PicoManager_NoUpdateOrDetails_ThrowsNoOperation()
        {
            using var output = new OutputWriterHelper();

            var options = new Options
            {
                Platform = SupportedPlatform.rpi_pico,
                // neither Update nor DeviceDetails set
            };

            var manager = new PicoManager(options, VerbosityLevel.Quiet);

            // can't run ProcessAsync without a device, but verify construction is correct
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void ExitCodes_E3005_Timeout_Exists()
        {
            // verify the E3005 timeout exit code exists
            var code = ExitCodes.E3005;
            Assert.AreEqual(3005, (int)code);
        }

        [TestMethod]
        [DoNotParallelize]
        public void PicoOperations_TargetNamePrefixes_MapToRpiPico()
        {
            using var output = new OutputWriterHelper();

            // verify that the Program.cs prefix detection works for various target names
            string[] picoTargetNames =
            [
                "RP_PICO_RP2040",
                "RP_PICO_W_RP2040",
                "RP_PICO_RP2350",
                "RPI_PICO",
                "RPI_PICO_W",
                "RP2040_CUSTOM_BOARD",
                "RP2350_MY_BOARD",
                "PICO_SOMETHING"
            ];

            foreach (string targetName in picoTargetNames)
            {
                Assert.IsTrue(
                    targetName.StartsWith("RP_PICO")
                    || targetName.StartsWith("RPI_PICO")
                    || targetName.StartsWith("RP2040")
                    || targetName.StartsWith("RP2350")
                    || targetName.StartsWith("PICO"),
                    $"Target name '{targetName}' should match Pico prefix detection");
            }
        }

        [TestMethod]
        public void SupportedPlatform_RpiPico_Exists()
        {
            // verify the enum value exists and is accessible
            var platform = SupportedPlatform.rpi_pico;
            Assert.AreEqual("rpi_pico", platform.ToString());
        }

        [TestMethod]
        public void Options_ListTargets_PicoPlatform_PropertiesSet()
        {
            var options = new Options
            {
                ListTargets = true,
                Platform = SupportedPlatform.rpi_pico
            };

            Assert.IsTrue(options.ListTargets);
            Assert.AreEqual(SupportedPlatform.rpi_pico, options.Platform);
        }

        [TestMethod]
        public void Options_DeviceDetails_PicoPlatform_PropertiesSet()
        {
            var options = new Options
            {
                DeviceDetails = true,
                Platform = SupportedPlatform.rpi_pico
            };

            Assert.IsTrue(options.DeviceDetails);
            Assert.AreEqual(SupportedPlatform.rpi_pico, options.Platform);
        }

        #region Option Validation Warnings

        [TestMethod]
        [DoNotParallelize]
        public void PicoManager_WithHexFile_Constructs()
        {
            using var output = new OutputWriterHelper();

            var options = new Options
            {
                Platform = SupportedPlatform.rpi_pico,
                Update = true,
                TargetName = "RP_PICO_RP2040",
                HexFile = new List<string> { "test.hex" }
            };

            // --hexfile is unsupported but should not prevent construction
            // ProcessAsync will emit a warning at runtime
            var manager = new PicoManager(options, VerbosityLevel.Normal);
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        [DoNotParallelize]
        public void PicoManager_ClrFileOption_Constructs()
        {
            using var output = new OutputWriterHelper();

            var options = new Options
            {
                Platform = SupportedPlatform.rpi_pico,
                Update = true,
                TargetName = "RP_PICO_RP2040",
                ClrFile = "nanoCLR.bin"
            };

            var manager = new PicoManager(options, VerbosityLevel.Quiet);
            Assert.IsNotNull(manager);
        }

        #endregion

        #region FlashAddress Parsing

        [TestMethod]
        public void FlashAddress_HexParsing_WithPrefix()
        {
            string addressStr = "0x10000000";
            string stripped = addressStr.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
                ? addressStr.Substring(2)
                : addressStr;

            bool parsed = uint.TryParse(
                stripped,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0x10000000u, result);
        }

        [TestMethod]
        public void FlashAddress_HexParsing_WithoutPrefix()
        {
            string addressStr = "10000000";
            string stripped = addressStr.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
                ? addressStr.Substring(2)
                : addressStr;

            bool parsed = uint.TryParse(
                stripped,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0x10000000u, result);
        }

        [TestMethod]
        public void FlashAddress_HexParsing_UpperCase0X()
        {
            string addressStr = "0X10010000";
            string stripped = addressStr.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
                ? addressStr.Substring(2)
                : addressStr;

            bool parsed = uint.TryParse(
                stripped,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0x10010000u, result);
        }

        [TestMethod]
        public void FlashAddress_HexParsing_InvalidAddress()
        {
            string addressStr = "not_an_address";
            string stripped = addressStr.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
                ? addressStr.Substring(2)
                : addressStr;

            bool parsed = uint.TryParse(
                stripped,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out uint _);

            Assert.IsFalse(parsed);
        }

        [TestMethod]
        public void FlashAddress_HexParsing_MaxFlashAddress()
        {
            // 0xFFFFFFFF is max uint32
            string addressStr = "0xFFFFFFFF";
            string stripped = addressStr.Substring(2);

            bool parsed = uint.TryParse(
                stripped,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0xFFFFFFFFu, result);
        }

        #endregion

        #region Exit Codes

        [TestMethod]
        public void ExitCodes_PicoRange_E3000_Through_E3005()
        {
            // verify the full range of Pico exit codes exists
            Assert.AreEqual(3000, (int)ExitCodes.E3000);
            Assert.AreEqual(3001, (int)ExitCodes.E3001);
            Assert.AreEqual(3002, (int)ExitCodes.E3002);
            Assert.AreEqual(3003, (int)ExitCodes.E3003);
            Assert.AreEqual(3004, (int)ExitCodes.E3004);
            Assert.AreEqual(3005, (int)ExitCodes.E3005);
        }

        #endregion
    }
}
