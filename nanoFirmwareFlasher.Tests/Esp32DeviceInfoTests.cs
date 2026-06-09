// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for Esp32DeviceInfo construction, flash size formatting,
    /// and ToString() representation — no hardware required.
    /// </summary>
    [TestClass]
    public class Esp32DeviceInfoTests
    {
        #region Construction

        [TestMethod]
        public void Esp32DeviceInfo_Constructor_SetsAllProperties()
        {
            var info = new Esp32DeviceInfo(
                chipType: "ESP32",
                chipName: "ESP32-D0WDQ6",
                features: "WiFi, BT, Dual Core",
                crystal: "40MHz",
                macAddress: "30:AE:A4:00:11:22",
                flashManufacturerId: 0xEF,
                flashDeviceModelId: 0x4016,
                flashSize: 0x400000,
                psramAvailability: PSRamAvailability.Yes,
                psRamSize: 4);

            Assert.AreEqual("ESP32", info.ChipType);
            Assert.AreEqual("ESP32-D0WDQ6", info.ChipName);
            Assert.AreEqual("WiFi, BT, Dual Core", info.Features);
            Assert.AreEqual("40MHz", info.Crystal);
            Assert.AreEqual("30:AE:A4:00:11:22", info.MacAddress);
            Assert.AreEqual(0xEF, info.FlashManufacturerId);
            Assert.AreEqual(0x4016, info.FlashDeviceId);
            Assert.AreEqual(0x400000, info.FlashSize);
            Assert.AreEqual(PSRamAvailability.Yes, info.PSRamAvailable);
            Assert.AreEqual(4, info.PsRamSize);
        }

        #endregion

        #region GetFlashSizeAsString — static method

        [TestMethod]
        [DataRow(0x400000, "4MB")]
        [DataRow(0x800000, "8MB")]
        [DataRow(0x1000000, "16MB")]
        [DataRow(0x200000, "2MB")]
        [DataRow(0x100000, "1MB")]
        public void GetFlashSizeAsString_MegabyteValues(int flashSize, string expected)
        {
            Assert.AreEqual(expected, Esp32DeviceInfo.GetFlashSizeAsString(flashSize));
        }

        [TestMethod]
        [DataRow(0x8000, "32kB")]
        [DataRow(0x4000, "16kB")]
        [DataRow(0xC00, "3kB")]
        public void GetFlashSizeAsString_KilobyteValues(int flashSize, string expected)
        {
            Assert.AreEqual(expected, Esp32DeviceInfo.GetFlashSizeAsString(flashSize));
        }

        [TestMethod]
        public void GetFlashSizeAsString_Boundary_UsesKBBelow()
        {
            // 0xFFFF is just below 0x10000 — should use kB
            string result = Esp32DeviceInfo.GetFlashSizeAsString(0xFFFF);
            Assert.IsTrue(result.EndsWith("kB"), $"Expected kB but got: {result}");
        }

        [TestMethod]
        public void GetFlashSizeAsString_Boundary_UsesMBAtAndAbove()
        {
            // 0x10000 >= 0x10000, so uses MB path
            string result = Esp32DeviceInfo.GetFlashSizeAsString(0x10000);
            Assert.IsTrue(result.EndsWith("MB"), $"Expected MB but got: {result}");
        }

        #endregion

        #region ToString

        [TestMethod]
        public void ToString_ContainsChipType()
        {
            var info = new Esp32DeviceInfo("ESP32-S3", "ESP32-S3", "WiFi, BLE",
                "40MHz", "DC:54:75:AA:BB:CC", 0x20, 0x4016, 0x1000000,
                PSRamAvailability.Yes, 8);

            string result = info.ToString();
            Assert.IsTrue(result.Contains("ESP32-S3"));
        }

        [TestMethod]
        public void ToString_ContainsFeatures()
        {
            var info = new Esp32DeviceInfo("ESP32", "ESP32-D0WDQ6", "WiFi, BT, Dual Core",
                "40MHz", "30:AE:A4:00:11:22", 0xEF, 0x4016, 0x400000,
                PSRamAvailability.No, 0);

            string result = info.ToString();
            Assert.IsTrue(result.Contains("WiFi, BT, Dual Core"));
        }

        [TestMethod]
        public void ToString_PSRamYes_ShowsSize()
        {
            var info = new Esp32DeviceInfo("ESP32-S3", "ESP32-S3", "WiFi",
                "40MHz", "AA:BB:CC:DD:EE:FF", 0x20, 0x4016, 0x800000,
                PSRamAvailability.Yes, 8);

            string result = info.ToString();
            Assert.IsTrue(result.Contains("PSRAM: 8MB"));
        }

        [TestMethod]
        public void ToString_PSRamNo_ShowsNotAvailable()
        {
            var info = new Esp32DeviceInfo("ESP32-C3", "ESP32-C3", "WiFi, BLE",
                "40MHz", "58:CF:79:11:22:33", 0xC8, 0x4014, 0x400000,
                PSRamAvailability.No, 0);

            string result = info.ToString();
            Assert.IsTrue(result.Contains("not available"));
        }

        [TestMethod]
        public void ToString_PSRamUndetermined()
        {
            var info = new Esp32DeviceInfo("ESP32-S2", "ESP32-S2", "WiFi",
                "40MHz", "AA:BB:CC:DD:EE:FF", 0xC8, 0x4016, 0x400000,
                PSRamAvailability.Undetermined, 0);

            string result = info.ToString();
            Assert.IsTrue(result.Contains("undetermined"));
        }

        [TestMethod]
        public void ToString_DifferentChipTypeAndName_ShowsBoth()
        {
            var info = new Esp32DeviceInfo("ESP32", "ESP32-D0WDQ6 (revision v1.0)",
                "WiFi", "40MHz", "AA:BB:CC:DD:EE:FF", 0xEF, 0x4016, 0x400000,
                PSRamAvailability.No, 0);

            string result = info.ToString();
            Assert.IsTrue(result.Contains("ESP32"));
            Assert.IsTrue(result.Contains("ESP32-D0WDQ6 (revision v1.0)"));
        }

        [TestMethod]
        public void ToString_ContainsMacAddress()
        {
            var info = new Esp32DeviceInfo("ESP32", "ESP32", "WiFi",
                "40MHz", "30:AE:A4:00:11:22", 0xEF, 0x4016, 0x400000,
                PSRamAvailability.No, 0);

            Assert.IsTrue(info.ToString().Contains("30:AE:A4:00:11:22"));
        }

        [TestMethod]
        public void ToString_ContainsCrystal()
        {
            var info = new Esp32DeviceInfo("ESP32", "ESP32", "WiFi",
                "40MHz", "AA:BB:CC:DD:EE:FF", 0xEF, 0x4016, 0x400000,
                PSRamAvailability.No, 0);

            Assert.IsTrue(info.ToString().Contains("40MHz"));
        }

        #endregion

        #region PSRamAvailability enum

        [TestMethod]
        public void PSRamAvailability_HasExpectedValues()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(PSRamAvailability), "Undetermined"));
            Assert.IsTrue(Enum.IsDefined(typeof(PSRamAvailability), "Yes"));
            Assert.IsTrue(Enum.IsDefined(typeof(PSRamAvailability), "No"));
        }

        #endregion
    }
}
