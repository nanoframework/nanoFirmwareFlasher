// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for CC13x26x2Device input validation paths.
    /// These exercise parameter checking without requiring actual hardware or DSLite.exe.
    /// </summary>
    [TestClass]
    public class CC13x26x2DeviceTests
    {
        #region Constructor

        [TestMethod]
        public void CC13x26x2Device_Constructor_SetsConfigurationFile()
        {
            var device = new CC13x26x2Device("test_config.ccxml");
            Assert.AreEqual("test_config.ccxml", device.ConfigurationFile);
        }

        [TestMethod]
        public void CC13x26x2Device_DefaultVerbosity_IsNormal()
        {
            var device = new CC13x26x2Device("config.ccxml");
            Assert.AreEqual(VerbosityLevel.Normal, device.Verbosity);
        }

        [TestMethod]
        public void CC13x26x2Device_DefaultDoMassErase_IsFalse()
        {
            var device = new CC13x26x2Device("config.ccxml");
            Assert.IsFalse(device.DoMassErase);
        }

        #endregion

        #region FlashHexFiles — file existence validation

        [TestMethod]
        public void FlashHexFiles_NonExistentFile_ReturnsE5003()
        {
            var device = new CC13x26x2Device("config.ccxml");
            var result = device.FlashHexFiles(new List<string> { @"C:\nonexistent\firmware.hex" });
            Assert.AreEqual(ExitCodes.E5003, result);
        }

        [TestMethod]
        public void FlashHexFiles_MixedExistenceFiles_ReturnsE5003()
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                var device = new CC13x26x2Device("config.ccxml");
                var result = device.FlashHexFiles(new List<string>
                {
                    tempFile,
                    @"C:\nonexistent\missing.hex"
                });

                Assert.AreEqual(ExitCodes.E5003, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        #endregion

        #region FlashBinFiles — file existence validation

        [TestMethod]
        public void FlashBinFiles_NonExistentFile_ReturnsE5003()
        {
            var device = new CC13x26x2Device("config.ccxml");
            var result = device.FlashBinFiles(
                new List<string> { @"C:\nonexistent\app.bin" },
                new List<string> { "0x00000000" });

            Assert.AreEqual(ExitCodes.E5003, result);
        }

        #endregion

        #region FlashBinFiles — address validation

        [TestMethod]
        public void FlashBinFiles_MismatchedFilesAndAddresses_ReturnsE5009()
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                var device = new CC13x26x2Device("config.ccxml");
                var result = device.FlashBinFiles(
                    new List<string> { tempFile },
                    new List<string> { "0x00000000", "0x00010000" });

                Assert.AreEqual(ExitCodes.E5009, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void FlashBinFiles_EmptyAddress_ReturnsE5007()
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                var device = new CC13x26x2Device("config.ccxml");
                var result = device.FlashBinFiles(
                    new List<string> { tempFile },
                    new List<string> { "" });

                Assert.AreEqual(ExitCodes.E5007, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void FlashBinFiles_NullAddress_ReturnsE5007()
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                var device = new CC13x26x2Device("config.ccxml");
                var result = device.FlashBinFiles(
                    new List<string> { tempFile },
                    new List<string> { null });

                Assert.AreEqual(ExitCodes.E5007, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void FlashBinFiles_AddressWithout0xPrefix_ReturnsE5008()
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                var device = new CC13x26x2Device("config.ccxml");
                var result = device.FlashBinFiles(
                    new List<string> { tempFile },
                    new List<string> { "00000000" });

                Assert.AreEqual(ExitCodes.E5008, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void FlashBinFiles_InvalidHexAddress_ReturnsE5008()
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                var device = new CC13x26x2Device("config.ccxml");
                var result = device.FlashBinFiles(
                    new List<string> { tempFile },
                    new List<string> { "0xGGGG" });

                Assert.AreEqual(ExitCodes.E5008, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        #endregion

        #region Properties

        [TestMethod]
        public void CC13x26x2Device_DoMassErase_CanBeSet()
        {
            var device = new CC13x26x2Device("config.ccxml")
            {
                DoMassErase = true
            };

            Assert.IsTrue(device.DoMassErase);
        }

        [TestMethod]
        public void CC13x26x2Device_Verbosity_CanBeSet()
        {
            var device = new CC13x26x2Device("config.ccxml")
            {
                Verbosity = VerbosityLevel.Diagnostic
            };

            Assert.AreEqual(VerbosityLevel.Diagnostic, device.Verbosity);
        }

        #endregion
    }
}
