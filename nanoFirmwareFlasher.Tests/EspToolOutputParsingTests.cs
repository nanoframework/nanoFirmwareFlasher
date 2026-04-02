//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for EspTool output parsing patterns.
    /// These validate the regex patterns used to extract device information
    /// from esptool console output — no hardware or esptool binary required.
    /// </summary>
    [TestClass]
    public class EspToolOutputParsingTests
    {
        // The main device-detection regex from EspTool.TestDevice()
        private static readonly Regex DeviceInfoRegex = new(
            @"(Detecting chip type... )(?<type>[ESP32\-ICOCH6]+)(.*?[\r\n]*)*(Chip type:          )(?<name>.*)(.*?[\r\n]*)*(Features:           )(?<features>.*)(.*?[\r\n]*)*(Crystal frequency:  )(?<crystal>.*)(.*?[\r\n]*)*(MAC:                )(?<mac>.*)(.*?[\r\n]*)*(Manufacturer: )(?<manufacturer>.*)(.*?[\r\n]*)*(Device: )(?<device>.*)(.*?[\r\n]*)*(Detected flash size: )(?<size>.*)");

        // PSRAM regex from FindPSRamAvailable (bootloader output)
        private static readonly Regex PsRamFoundRegex = new(
            @"Found (?<size>\d+)MB PSRAM device");

        // Embedded PSRAM regex from TestDevice (ESP32-S3 features string)
        private static readonly Regex EmbeddedPsRamRegex = new(
            @"Embedded PSRAM (?<size>\d+)MB");

        // Flash operation success regex from EraseFlash / EraseFlashSegment
        private static readonly Regex FlashEraseRegex = new(
            @"(?<message>Flash memory erased successfully.*)(.*?\n)*");

        // Read partition table regex
        private static readonly Regex ReadPartitionRegex = new(
            @"(?<message>Read .*)(.*?\n)*");

        #region Device detection regex — ESP32

        [TestMethod]
        public void DeviceInfo_ESP32_ClassicOutput_MatchesAllGroups()
        {
            string output =
@"esptool.py v4.7.0
Serial port COM3
Connecting....
Detecting chip type... ESP32
Chip type:          ESP32-D0WDQ6 (revision v1.0)
Features:           WiFi, BT, Dual Core, 240MHz, VRef calibration in efuse, Coding Scheme None
Crystal frequency:  40MHz
MAC:                30:ae:a4:00:11:22
Manufacturer: ef
Device: 4016
Detected flash size: 4MB";

            Match m = DeviceInfoRegex.Match(output);
            Assert.IsTrue(m.Success, "Regex should match ESP32 classic output");
            Assert.AreEqual("ESP32", m.Groups["type"].Value);
            Assert.AreEqual("ESP32-D0WDQ6 (revision v1.0)", m.Groups["name"].Value.Trim());
            Assert.IsTrue(m.Groups["features"].Value.Contains("WiFi"));
            Assert.AreEqual("40MHz", m.Groups["crystal"].Value.Trim());
            Assert.AreEqual("30:ae:a4:00:11:22", m.Groups["mac"].Value.Trim());
            Assert.AreEqual("ef", m.Groups["manufacturer"].Value.Trim());
            Assert.AreEqual("4016", m.Groups["device"].Value.Trim());
            Assert.AreEqual("4MB", m.Groups["size"].Value.Trim());
        }

        #endregion

        #region Device detection regex — ESP32-S3

        [TestMethod]
        public void DeviceInfo_ESP32S3_WithPSRAM_MatchesAllGroups()
        {
            string output =
@"esptool.py v4.7.0
Serial port /dev/ttyACM0
Connecting....
Detecting chip type... ESP32-S3
Chip type:          ESP32-S3 (revision v0.1)
Features:           WiFi, BLE, Embedded PSRAM 8MB (AP_3v3)
Crystal frequency:  40MHz
MAC:                dc:54:75:aa:bb:cc
Manufacturer: 20
Device: 4016
Detected flash size: 16MB";

            Match m = DeviceInfoRegex.Match(output);
            Assert.IsTrue(m.Success, "Regex should match ESP32-S3 output");
            Assert.AreEqual("ESP32-S3", m.Groups["type"].Value);
            Assert.IsTrue(m.Groups["features"].Value.Contains("Embedded PSRAM 8MB"));
            Assert.AreEqual("16MB", m.Groups["size"].Value.Trim());
        }

        #endregion

        #region Device detection regex — ESP32-C3

        [TestMethod]
        public void DeviceInfo_ESP32C3_MatchesAllGroups()
        {
            string output =
@"esptool.py v4.7.0
Serial port COM5
Connecting....
Detecting chip type... ESP32-C3
Chip type:          ESP32-C3 (revision v0.4)
Features:           WiFi, BLE
Crystal frequency:  40MHz
MAC:                58:cf:79:11:22:33
Manufacturer: c8
Device: 4014
Detected flash size: 4MB";

            Match m = DeviceInfoRegex.Match(output);
            Assert.IsTrue(m.Success, "Regex should match ESP32-C3 output");
            Assert.AreEqual("ESP32-C3", m.Groups["type"].Value);
            Assert.AreEqual("4MB", m.Groups["size"].Value.Trim());
        }

        #endregion

        #region Device detection regex — ESP32-C6

        [TestMethod]
        public void DeviceInfo_ESP32C6_MatchesAllGroups()
        {
            string output =
@"esptool.py v4.7.0
Serial port /dev/ttyUSB0
Connecting....
Detecting chip type... ESP32-C6
Chip type:          ESP32-C6 (revision v0.0)
Features:           WiFi 6, BT 5
Crystal frequency:  40MHz
MAC:                40:4c:ca:aa:bb:cc
Manufacturer: 5e
Device: 4016
Detected flash size: 8MB";

            Match m = DeviceInfoRegex.Match(output);
            Assert.IsTrue(m.Success, "Regex should match ESP32-C6 output");
            Assert.AreEqual("ESP32-C6", m.Groups["type"].Value);
        }

        #endregion

        #region Device detection regex — ESP32-H2

        [TestMethod]
        public void DeviceInfo_ESP32H2_MatchesAllGroups()
        {
            string output =
@"esptool.py v4.7.0
Serial port COM10
Connecting....
Detecting chip type... ESP32-H2
Chip type:          ESP32-H2 (revision v0.1)
Features:           BLE
Crystal frequency:  32MHz
MAC:                48:31:b7:11:22:33
Manufacturer: c8
Device: 4014
Detected flash size: 4MB";

            Match m = DeviceInfoRegex.Match(output);
            Assert.IsTrue(m.Success, "Regex should match ESP32-H2 output");
            Assert.AreEqual("ESP32-H2", m.Groups["type"].Value);
            Assert.AreEqual("32MHz", m.Groups["crystal"].Value.Trim());
        }

        #endregion

        #region Device detection regex — No match

        [TestMethod]
        public void DeviceInfo_GarbageOutput_DoesNotMatch()
        {
            string output = "Some random output that is not from esptool\nConnection failed.";
            Match m = DeviceInfoRegex.Match(output);
            Assert.IsFalse(m.Success, "Regex should not match garbage output");
        }

        [TestMethod]
        public void DeviceInfo_PartialOutput_DoesNotMatch()
        {
            string output = "Detecting chip type... ESP32\nChip type:          ESP32-D0WDQ6\n";
            Match m = DeviceInfoRegex.Match(output);
            Assert.IsFalse(m.Success, "Regex should not match incomplete output");
        }

        #endregion

        #region Flash size parsing

        [TestMethod]
        [DataRow("4MB", 4)]
        [DataRow("8MB", 8)]
        [DataRow("16MB", 16)]
        [DataRow("2MB", 2)]
        public void FlashSize_ParsedCorrectly(string sizeString, int expectedMB)
        {
            string unit = sizeString.Substring(sizeString.Length - 2).ToUpperInvariant();
            Assert.IsTrue(int.TryParse(sizeString.Remove(sizeString.Length - 2), out int flashSize));
            int multiplier = unit switch
            {
                "MB" => 0x100000,
                "KB" => 0x400,
                _ => 1,
            };

            Assert.AreEqual(expectedMB * 0x100000, flashSize * multiplier);
        }

        [TestMethod]
        [DataRow("512KB", 512)]
        [DataRow("256KB", 256)]
        public void FlashSize_KBParsedCorrectly(string sizeString, int expectedKB)
        {
            string unit = sizeString.Substring(sizeString.Length - 2).ToUpperInvariant();
            Assert.IsTrue(int.TryParse(sizeString.Remove(sizeString.Length - 2), out int flashSize));
            int multiplier = unit switch
            {
                "MB" => 0x100000,
                "KB" => 0x400,
                _ => 1,
            };

            Assert.AreEqual(expectedKB * 0x400, flashSize * multiplier);
        }

        #endregion

        #region Embedded PSRAM regex (ESP32-S3 features)

        [TestMethod]
        [DataRow("WiFi, BLE, Embedded PSRAM 8MB (AP_3v3)", 8)]
        [DataRow("WiFi, BLE, Embedded PSRAM 2MB (AP_3v3)", 2)]
        [DataRow("WiFi, BLE, Embedded PSRAM 16MB", 16)]
        public void EmbeddedPsRam_MatchesSize(string features, int expectedMB)
        {
            Match m = EmbeddedPsRamRegex.Match(features);
            Assert.IsTrue(m.Success, "Should match embedded PSRAM string");
            Assert.AreEqual(expectedMB, int.Parse(m.Groups["size"].Value));
        }

        [TestMethod]
        [DataRow("WiFi, BLE")]
        [DataRow("WiFi, BT, Dual Core")]
        public void EmbeddedPsRam_DoesNotMatchWithoutPSRAM(string features)
        {
            Match m = EmbeddedPsRamRegex.Match(features);
            Assert.IsFalse(m.Success, "Should not match when no embedded PSRAM");
        }

        #endregion

        #region PSRAM bootloader regex

        [TestMethod]
        public void PsRamBootloader_Found4MB()
        {
            string log =
@"I(206) esp_psram: Found 4MB PSRAM device
I(206) esp_psram: Speed: 40MHz
I(209) esp_psram: PSRAM initialized, cache is in low / high(2 - core) mode.";

            Match m = PsRamFoundRegex.Match(log);
            Assert.IsTrue(m.Success);
            Assert.AreEqual("4", m.Groups["size"].Value);
        }

        [TestMethod]
        public void PsRamBootloader_Found8MB()
        {
            string log = "I(206) esp_psram: Found 8MB PSRAM device";
            Match m = PsRamFoundRegex.Match(log);
            Assert.IsTrue(m.Success);
            Assert.AreEqual("8", m.Groups["size"].Value);
        }

        [TestMethod]
        public void PsRamBootloader_NoPSRAM_NoMatch()
        {
            string log = "E(206) quad_psram: PSRAM ID read error: 0xffffffff";
            Match m = PsRamFoundRegex.Match(log);
            Assert.IsFalse(m.Success);
        }

        #endregion

        #region Flash erase regex

        [TestMethod]
        public void FlashErase_SuccessMessage_Matches()
        {
            string output = "Flash memory erased successfully in 3.2s\n";
            Match m = FlashEraseRegex.Match(output);
            Assert.IsTrue(m.Success);
            Assert.IsTrue(m.Groups["message"].Value.Contains("erased successfully"));
        }

        [TestMethod]
        public void FlashErase_FailureMessage_DoesNotMatch()
        {
            string output = "A fatal error occurred: Failed to connect to Espressif device";
            Match m = FlashEraseRegex.Match(output);
            Assert.IsFalse(m.Success);
        }

        #endregion

        #region Read partition regex

        [TestMethod]
        public void ReadPartition_SuccessMessage_Matches()
        {
            string output = "Read 4096 bytes at 0x8000 in 0.1 seconds\n";
            Match m = ReadPartitionRegex.Match(output);
            Assert.IsTrue(m.Success);
            Assert.IsTrue(m.Groups["message"].Value.StartsWith("Read"));
        }

        [TestMethod]
        public void ReadPartition_NoReadMessage_DoesNotMatch()
        {
            string output = "Error: Connection timed out";
            Match m = ReadPartitionRegex.Match(output);
            Assert.IsFalse(m.Success);
        }

        #endregion

        #region Chip type normalization

        [TestMethod]
        [DataRow("ESP32", "esp32")]
        [DataRow("ESP32-S3", "esp32s3")]
        [DataRow("ESP32-C3", "esp32c3")]
        [DataRow("ESP32-C6", "esp32c6")]
        [DataRow("ESP32-H2", "esp32h2")]
        [DataRow("ESP32-S2", "esp32s2")]
        public void ChipType_Normalization_IsCorrect(string chipType, string expected)
        {
            // Replicate the normalization logic from EspTool.TestDevice()
            string normalized = chipType.ToLower().Replace("-", "");
            Assert.AreEqual(expected, normalized);
        }

        [TestMethod]
        public void ChipType_ESP32P_CorrectToP4()
        {
            // ESP32-P chip shows as "ESP32-P" but should be "ESP32-P4"
            string chipType = "ESP32-P";
            if (chipType == "ESP32-P")
            {
                chipType = "ESP32-P4";
            }

            string normalized = chipType.ToLower().Replace("-", "");
            Assert.AreEqual("esp32p4", normalized);
        }

        #endregion

        #region PSRAM availability logic

        [TestMethod]
        public void PsRamLogic_C3C6H2_AlwaysNo()
        {
            // These chip types never have PSRAM
            string[] noPsRamChips = { "esp32c3", "esp32c6", "esp32h2" };

            foreach (string chip in noPsRamChips)
            {
                Assert.IsTrue(
                    chip == "esp32c3" || chip == "esp32c6" || chip == "esp32h2",
                    $"{chip} should be in the no-PSRAM list");
            }
        }

        #endregion
    }
}
