// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
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
        public void DetectDevice_InvalidInputs_ReturnsNull()
        {
            // null, empty, and non-existent paths should all return null
            // without throwing, exercising the guard clauses
            Assert.IsNull(PicoUf2Utility.DetectDevice(null));
            Assert.IsNull(PicoUf2Utility.DetectDevice(""));
            Assert.IsNull(PicoUf2Utility.DetectDevice(@"Z:\no_such_drive_path_ever"));
        }

        [TestMethod]
        [DoNotParallelize]
        public async Task PicoManager_NoUpdateOrDetails_ThrowsNoOperation()
        {
            using var output = new OutputWriterHelper();

            var options = new Options
            {
                Platform = SupportedPlatform.rpi_pico,
                // neither Update, Deploy, MassErase, nor DeviceDetails set
            };

            var manager = new PicoManager(options, VerbosityLevel.Quiet);

            await Assert.ThrowsExceptionAsync<NoOperationPerformedException>(
                () => manager.ProcessAsync());
        }

        [TestMethod]
        public void PicoOperations_InferTargetName_MapsChipTypes()
        {
            MethodInfo method = typeof(PicoOperations).GetMethod(
                "InferTargetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "InferTargetName method not found");

            // null device returns null (caller must handle the error)
            string result = (string)method.Invoke(null, new object[] { null });
            Assert.IsNull(result);

            // RP2040 chip maps to RP_PICO_RP2040
            var rp2040 = new PicoDeviceInfo("RP2040", "", "", "", "");
            result = (string)method.Invoke(null, new object[] { rp2040 });
            Assert.AreEqual("RP_PICO_RP2040", result);

            // RP2350 chip maps to RP_PICO_RP2350
            var rp2350 = new PicoDeviceInfo("RP2350", "", "", "", "");
            result = (string)method.Invoke(null, new object[] { rp2350 });
            Assert.AreEqual("RP_PICO_RP2350", result);
        }

        [TestMethod]
        public void ExitCodes_E3005_Timeout_Exists()
        {
            // verify the E3005 timeout exit code exists
            var code = ExitCodes.E3005;
            Assert.AreEqual(3005, (int)code);
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
        public void TryParseHexAddress_WithPrefix()
        {
            bool parsed = PicoOperations.TryParseHexAddress("0x10000000", out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0x10000000u, result);
        }

        [TestMethod]
        public void TryParseHexAddress_WithoutPrefix()
        {
            bool parsed = PicoOperations.TryParseHexAddress("10000000", out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0x10000000u, result);
        }

        [TestMethod]
        public void TryParseHexAddress_UpperCase0X()
        {
            bool parsed = PicoOperations.TryParseHexAddress("0X10010000", out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0x10010000u, result);
        }

        [TestMethod]
        public void TryParseHexAddress_InvalidAddress()
        {
            bool parsed = PicoOperations.TryParseHexAddress("not_an_address", out uint _);

            Assert.IsFalse(parsed);
        }

        [TestMethod]
        public void TryParseHexAddress_MaxUInt32()
        {
            bool parsed = PicoOperations.TryParseHexAddress("0xFFFFFFFF", out uint result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0xFFFFFFFFu, result);
        }

        [TestMethod]
        public void TryParseHexAddress_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(PicoOperations.TryParseHexAddress(null, out _));
            Assert.IsFalse(PicoOperations.TryParseHexAddress("", out _));
        }

        #endregion

        #region Exit Codes

        [TestMethod]
        public void ExitCodes_PicoRange_E3000_Through_E3006()
        {
            // verify the full range of Pico exit codes exists
            Assert.AreEqual(3000, (int)ExitCodes.E3000);
            Assert.AreEqual(3001, (int)ExitCodes.E3001);
            Assert.AreEqual(3002, (int)ExitCodes.E3002);
            Assert.AreEqual(3003, (int)ExitCodes.E3003);
            Assert.AreEqual(3004, (int)ExitCodes.E3004);
            Assert.AreEqual(3005, (int)ExitCodes.E3005);
            Assert.AreEqual(3006, (int)ExitCodes.E3006);
        }

        #endregion
    }
}
