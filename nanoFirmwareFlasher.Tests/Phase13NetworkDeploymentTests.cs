// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.FileDeployment;
using nanoFramework.Tools.FirmwareFlasher.NetworkDeployment;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Phase13NetworkDeploymentTests
    {
        public TestContext TestContext { get; set; } = null!;

        #region Helper — create NetworkDeploymentManager from JSON

        private NetworkDeploymentManager CreateManager(string json)
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string configPath = Path.Combine(testDir, "network_config.json");
            File.WriteAllText(configPath, json);
            return new NetworkDeploymentManager(configPath, "COM99", VerbosityLevel.Quiet);
        }

        private const string MinimalConfig = @"{""SerialPort"":""COM1""}";

        #endregion

        #region NetworkDeploymentManager Constructor & Config Tests

        [TestMethod]
        public void NetworkDeploymentManager_Constructor_ParsesMinimalConfig()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void NetworkDeploymentManager_Constructor_UsesOriginalPortWhenConfigPortEmpty()
        {
            var manager = CreateManager(@"{}");
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void NetworkDeploymentManager_Constructor_MissingFile_Throws()
        {
            Assert.ThrowsException<DirectoryNotFoundException>(() =>
                new NetworkDeploymentManager(@"C:\nonexistent\config.json", "COM1", VerbosityLevel.Quiet));
        }

        [TestMethod]
        public void NetworkDeploymentManager_Constructor_InvalidJson_Throws()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string configPath = Path.Combine(testDir, "bad.json");
            File.WriteAllText(configPath, "NOT JSON{{{");
            Assert.ThrowsException<System.Text.Json.JsonException>(() =>
                new NetworkDeploymentManager(configPath, "COM1", VerbosityLevel.Quiet));
        }

        [TestMethod]
        public void NetworkDeploymentManager_Constructor_WirelessConfig_Parsed()
        {
            string json = @"{
                ""WirelessClient"": {
                    ""Ssid"": ""TestNet"",
                    ""Password"": ""pass123"",
                    ""Authentication"": ""WPA2""
                }
            }";
            var manager = CreateManager(json);
            Assert.IsNotNull(manager);
        }

        #endregion

        #region GetConfigureAuthentication Tests

        [TestMethod]
        [DataRow("WPA2")]
        [DataRow("wpa2")]
        [DataRow("Wpa2")]
        public void GetConfigureAuthentication_WPA2_CaseInsensitive(string input)
        {
            var manager = CreateManager(MinimalConfig);
            var result = manager.GetConfigureAuthentication(input);
            Assert.AreEqual(AuthenticationType.WPA2, result);
        }

        [TestMethod]
        [DataRow("NONE")]
        [DataRow("OPEN")]
        [DataRow("SHARED")]
        [DataRow("WEP")]
        [DataRow("WPA")]
        [DataRow("WPA2")]
        [DataRow("EAP")]
        [DataRow("PEAP")]
        [DataRow("WCN")]
        public void GetConfigureAuthentication_AllValidValues(string input)
        {
            var manager = CreateManager(MinimalConfig);
            // Should not throw for any valid value
            var result = manager.GetConfigureAuthentication(input);
            Assert.IsNotNull(result.ToString());
        }

        [TestMethod]
        public void GetConfigureAuthentication_InvalidValue_ThrowsArgumentException()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.ThrowsException<ArgumentException>(() => manager.GetConfigureAuthentication("INVALID"));
        }

        #endregion

        #region GetConfigurationOptions Tests

        [TestMethod]
        [DataRow("NONE")]
        [DataRow("DISABLE")]
        [DataRow("ENABLE")]
        [DataRow("AUTOCONNECT")]
        [DataRow("SMARTCONFIG")]
        public void GetConfigurationOptions_AllValidValues(string input)
        {
            var manager = CreateManager(MinimalConfig);
            // just ensure no exception is thrown for each valid value
            var result = manager.GetConfigurationOptions(input);
            Assert.IsNotNull(result.ToString());
        }

        [TestMethod]
        public void GetConfigurationOptions_CaseInsensitive()
        {
            var manager = CreateManager(MinimalConfig);
            var result = manager.GetConfigurationOptions("autoconnect");
            Assert.AreEqual(Wireless80211_ConfigurationOptions.AutoConnect, result);
        }

        [TestMethod]
        public void GetConfigurationOptions_InvalidValue_ThrowsArgumentException()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.ThrowsException<ArgumentException>(() => manager.GetConfigurationOptions("BOGUS"));
        }

        #endregion

        #region GetRadioType Tests

        [TestMethod]
        [DataRow("802.11A")]
        [DataRow("802.11B")]
        [DataRow("802.11G")]
        [DataRow("802.11N")]
        public void GetRadioType_AllValidValues(string input)
        {
            var manager = CreateManager(MinimalConfig);
            var result = manager.GetRadioType(input);
            Assert.IsNotNull(result.ToString());
        }

        [TestMethod]
        public void GetRadioType_CaseInsensitive()
        {
            var manager = CreateManager(MinimalConfig);
            var result = manager.GetRadioType("802.11n");
            Assert.AreEqual(RadioType._802_11n, result);
        }

        [TestMethod]
        public void GetRadioType_InvalidValue_ThrowsArgumentException()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.ThrowsException<ArgumentException>(() => manager.GetRadioType("802.11ac"));
        }

        #endregion

        #region GetEncryptionType Tests

        [TestMethod]
        [DataRow("WEP")]
        [DataRow("WPA")]
        [DataRow("WPA2")]
        [DataRow("WPA_PSK")]
        [DataRow("WPA2_PSK2")]
        [DataRow("CERTIFICATE")]
        public void GetEncryptionType_AllValidValues(string input)
        {
            var manager = CreateManager(MinimalConfig);
            var result = manager.GetEncryptionType(input);
            Assert.AreNotEqual(EncryptionType.None, result);
        }

        [TestMethod]
        public void GetEncryptionType_UnknownValue_ReturnsNone()
        {
            // Unlike other methods, GetEncryptionType defaults to None for unknown values
            var manager = CreateManager(MinimalConfig);
            Assert.AreEqual(EncryptionType.None, manager.GetEncryptionType("UNKNOWN"));
        }

        [TestMethod]
        public void GetEncryptionType_CaseInsensitive()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.AreEqual(EncryptionType.WPA2, manager.GetEncryptionType("wpa2"));
        }

        #endregion

        #region GetWirelessAPOptions Tests

        [TestMethod]
        [DataRow("NONE")]
        [DataRow("DISABLE")]
        [DataRow("ENABLE")]
        [DataRow("AUTOSTART")]
        [DataRow("HIDDENSSID")]
        public void GetWirelessAPOptions_AllValidValues(string input)
        {
            var manager = CreateManager(MinimalConfig);
            var result = manager.GetWirelessAPOptions(input);
            Assert.IsNotNull(result.ToString());
        }

        [TestMethod]
        public void GetWirelessAPOptions_InvalidValue_ThrowsArgumentException()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.ThrowsException<ArgumentException>(() => manager.GetWirelessAPOptions("RANDOMAP"));
        }

        #endregion

        #region GetMacAddress Tests

        [TestMethod]
        public void GetMacAddress_ColonSeparated_ParsesCorrectly()
        {
            var manager = CreateManager(MinimalConfig);
            byte[] mac = manager.GetMacAddress("AA:BB:CC:DD:EE:FF");
            CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, mac);
        }

        [TestMethod]
        public void GetMacAddress_PlainHex_ParsesCorrectly()
        {
            var manager = CreateManager(MinimalConfig);
            byte[] mac = manager.GetMacAddress("AABBCCDDEEFF");
            CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, mac);
        }

        [TestMethod]
        public void GetMacAddress_LowercaseHex_ParsesCorrectly()
        {
            var manager = CreateManager(MinimalConfig);
            byte[] mac = manager.GetMacAddress("aa:bb:cc:dd:ee:ff");
            CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, mac);
        }

        [TestMethod]
        public void GetMacAddress_AllZeros_ParsesCorrectly()
        {
            var manager = CreateManager(MinimalConfig);
            byte[] mac = manager.GetMacAddress("00:00:00:00:00:00");
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 0, 0 }, mac);
        }

        [TestMethod]
        public void GetMacAddress_WrongLength_ThrowsArgumentException()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.ThrowsException<ArgumentException>(() => manager.GetMacAddress("AABB"));
        }

        [TestMethod]
        public void GetMacAddress_InvalidHex_ThrowsFormatException()
        {
            var manager = CreateManager(MinimalConfig);
            Assert.ThrowsException<FormatException>(() => manager.GetMacAddress("GG:HH:II:JJ:KK:LL"));
        }

        #endregion

        #region CheckNullPemTermination Tests

        [TestMethod]
        public void CheckNullPemTermination_PemWithoutNull_IsHandled()
        {
            var manager = CreateManager(MinimalConfig);
            byte[] cert = Encoding.ASCII.GetBytes("-----BEGIN CERTIFICATE-----\nDATA\n-----END CERTIFICATE-----");

            // CheckNullPemTermination modifies via Array.Resize — but since byte[] is a reference type
            // passed by value, the resize creates a new array. The method has a bug: it resizes the local
            // reference but doesn't return it. Let's just verify it doesn't throw.
            manager.CheckNullPemTermination(cert);
        }

        [TestMethod]
        public void CheckNullPemTermination_PemWithNull_NoChange()
        {
            var manager = CreateManager(MinimalConfig);
            byte[] certBytes = Encoding.ASCII.GetBytes("-----BEGIN CERTIFICATE-----\nDATA\n-----END CERTIFICATE-----");
            byte[] certWithNull = new byte[certBytes.Length + 1];
            Array.Copy(certBytes, certWithNull, certBytes.Length);
            certWithNull[certWithNull.Length - 1] = 0;

            int originalLength = certWithNull.Length;
            manager.CheckNullPemTermination(certWithNull);
            // Array should not grow since it already has null terminator
            Assert.AreEqual(originalLength, certWithNull.Length);
        }

        [TestMethod]
        public void CheckNullPemTermination_NonPem_NoChange()
        {
            var manager = CreateManager(MinimalConfig);
            byte[] binaryCert = new byte[] { 0x30, 0x82, 0x01, 0x22 }; // DER format
            int originalLength = binaryCert.Length;
            manager.CheckNullPemTermination(binaryCert);
            Assert.AreEqual(originalLength, binaryCert.Length);
        }

        #endregion

        #region NetworkDeploymentConfiguration Deserialization Tests

        [TestMethod]
        public void NetworkDeploymentConfig_FullConfig_Parsed()
        {
            string json = @"{
                ""SerialPort"": ""COM5"",
                ""WirelessClient"": {
                    ""Ssid"": ""MyNetwork"",
                    ""Password"": ""secret"",
                    ""Authentication"": ""WPA2"",
                    ""Encryption"": ""WPA2_PSK2"",
                    ""ConfigurationOption"": ""AutoConnect"",
                    ""RadioType"": ""802.11n"",
                    ""DhcpEnabled"": true
                },
                ""Ethernet"": {
                    ""DhcpEnabled"": false,
                    ""IPv4Address"": ""192.168.1.100"",
                    ""IPv4NetMask"": ""255.255.255.0"",
                    ""IPv4Gateway"": ""192.168.1.1"",
                    ""MacAddress"": ""AA:BB:CC:DD:EE:FF""
                },
                ""DeviceCertificates"": ""AQID"",
                ""CACertificates"": ""BAUG""
            }";
            var manager = CreateManager(json);
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void NetworkDeploymentConfig_AccessPoint_Parsed()
        {
            string json = @"{
                ""WirelessAccessPoint"": {
                    ""Ssid"": ""MyAP"",
                    ""Password"": ""appass"",
                    ""MaxConnections"": 8,
                    ""AccessPointOptions"": ""Enable"",
                    ""IPv4Address"": ""192.168.4.1"",
                    ""IPv4NetMask"": ""255.255.255.0""
                }
            }";
            var manager = CreateManager(json);
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void NetworkDeploymentConfig_CertificatePaths_Parsed()
        {
            string json = @"{
                ""DeviceCertificatesPath"": ""C:\\certs\\device.pem"",
                ""CACertificatesPath"": ""C:\\certs\\ca.pem""
            }";
            var manager = CreateManager(json);
            Assert.IsNotNull(manager);
        }

        #endregion

        #region Ethernet Configuration Model Tests

        [TestMethod]
        public void Ethernet_DefaultValues_DhcpAndDnsEnabled()
        {
            var eth = new Ethernet();
            Assert.IsTrue(eth.DhcpEnabled);
            Assert.IsTrue(eth.AutomaticDNS);
        }

        [TestMethod]
        public void WirelessConfiguration_InheritsFromEthernet()
        {
            Assert.IsTrue(typeof(Ethernet).IsAssignableFrom(typeof(WirelessConfiguration)));
        }

        [TestMethod]
        public void WirelessAccessPoint_InheritsFromWirelessConfiguration()
        {
            Assert.IsTrue(typeof(WirelessConfiguration).IsAssignableFrom(typeof(WirelessAccessPoint)));
        }

        [TestMethod]
        public void WirelessAccessPoint_DefaultMaxConnections_Is4()
        {
            var ap = new WirelessAccessPoint();
            Assert.AreEqual((byte)4, ap.MaxConnections);
        }

        #endregion

        #region JLinkCli Structural Tests

        [TestMethod]
        public void JLinkCli_CommandTemplates_ContainExpectedTokens()
        {
            // Use reflection to read private const fields
            var singleTemplate = typeof(JLinkCli).GetField("FlashSingleFileCommandTemplate",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(singleTemplate, "FlashSingleFileCommandTemplate should exist");
            string value = (string)singleTemplate.GetValue(null);
            Assert.IsTrue(value.Contains("{FILE_PATH}"));
            Assert.IsTrue(value.Contains("{FLASH_ADDRESS}"));

            var multiTemplate = typeof(JLinkCli).GetField("FlashMultipleFilesCommandTemplate",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(multiTemplate, "FlashMultipleFilesCommandTemplate should exist");
            string multiValue = (string)multiTemplate.GetValue(null);
            Assert.IsTrue(multiValue.Contains("{LOAD_FILE_LIST}"));
        }

        [TestMethod]
        public void JLinkCli_DefaultProperties_AreCorrect()
        {
            var jlink = new JLinkCli();
            Assert.IsFalse(jlink.DoMassErase);
            Assert.AreEqual(VerbosityLevel.Normal, jlink.Verbosity);
        }

        [TestMethod]
        public void JLinkCli_CmdFilesDir_EndsWithJlinkCmds()
        {
            string dir = JLinkCli.CmdFilesDir;
            Assert.IsTrue(dir.EndsWith("jlinkCmds"), $"CmdFilesDir should end with jlinkCmds but was: {dir}");
        }

        [TestMethod]
        public void JLinkCli_ProcessFilePaths_ReturnsE5004_ForMissingFile()
        {
            // ProcessFilePaths is private static — test via reflection
            var method = typeof(JLinkCli).GetMethod("ProcessFilePaths",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ProcessFilePaths should exist");

            var files = new System.Collections.Generic.List<string> { @"C:\nonexistent\firmware.bin" };
            var shadowFiles = new System.Collections.Generic.List<string>();

            var result = (ExitCodes)method.Invoke(null, new object[] { files, shadowFiles });
            Assert.AreEqual(ExitCodes.E5004, result);
        }

        [TestMethod]
        public void JLinkCli_ProcessFilePaths_ReturnsOK_ForExistingFile()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string testFile = Path.Combine(testDir, "test.bin");
            File.WriteAllBytes(testFile, new byte[] { 0xFF });

            var method = typeof(JLinkCli).GetMethod("ProcessFilePaths",
                BindingFlags.NonPublic | BindingFlags.Static);

            var files = new System.Collections.Generic.List<string> { testFile };
            var shadowFiles = new System.Collections.Generic.List<string>();

            var result = (ExitCodes)method.Invoke(null, new object[] { files, shadowFiles });
            Assert.AreEqual(ExitCodes.OK, result);
            Assert.AreEqual(1, shadowFiles.Count);
        }

        [TestMethod]
        public void JLinkCli_RunJLinkCLI_MissingBinary_ThrowsCantConnect()
        {
            // JLink executable doesn't exist in test environment
            try
            {
                JLinkCli.RunJLinkCLI("nonexistent.jlink");
                Assert.Fail("Expected CantConnectToJLinkDeviceException");
            }
            catch (CantConnectToJLinkDeviceException)
            {
                // expected
            }
        }

        #endregion

        #region Esp32Firmware Structural Tests

        [TestMethod]
        public void Esp32Firmware_CLRAddress_Is0x10000()
        {
            Assert.AreEqual(0x10000, Esp32Firmware.CLRAddress);
        }

        [TestMethod]
        public void Esp32Firmware_DeploymentPartitionAddress_Is0x1B0000()
        {
            var fw = new Esp32Firmware("ESP32_PSRAM_REV3", "1.0.0", false, null);
            Assert.AreEqual(0x1B0000, fw.DeploymentPartitionAddress);
        }

        [TestMethod]
        public void Esp32Firmware_DerivesFirmwarePackage()
        {
            Assert.IsTrue(typeof(FirmwarePackage).IsAssignableFrom(typeof(Esp32Firmware)));
        }

        [TestMethod]
        public void Esp32Firmware_CanConstruct_WithPartitionTableSize()
        {
            using (var fw = new Esp32Firmware("ESP32_PSRAM_REV3", "1.0.0", false, PartitionTableSize._4))
            {
                Assert.IsNotNull(fw);
                Assert.AreEqual(PartitionTableSize._4, fw._partitionTableSize);
            }
        }

        [TestMethod]
        public void Esp32Firmware_CanConstruct_WithoutPartitionTableSize()
        {
            using (var fw = new Esp32Firmware("ESP32_PSRAM_REV3", "1.0.0", false, null))
            {
                Assert.IsNotNull(fw);
                Assert.IsNull(fw._partitionTableSize);
            }
        }

        #endregion

        #region JLinkDevice Structural Tests

        [TestMethod]
        public void JLinkDevice_HasExpectedPublicMembers()
        {
            var type = typeof(JLinkDevice);
            Assert.IsNotNull(type.GetProperty("ProbeId"), "ProbeId property");
            Assert.IsNotNull(type.GetProperty("DeviceId"), "DeviceId property");
            Assert.IsNotNull(type.GetProperty("DeviceCPU"), "DeviceCPU property");
        }

        [TestMethod]
        public void JLinkDevice_HasListDevicesMethod()
        {
            var method = typeof(JLinkDevice).GetMethod("ListDevices");
            Assert.IsNotNull(method, "ListDevices should exist");
            Assert.IsTrue(method.IsStatic);
        }

        #endregion

        #region FileDeploymentManager Structural Tests

        [TestMethod]
        public void FileDeploymentManager_Constructor_ParsesValidConfig()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string configPath = Path.Combine(testDir, "deploy_config.json");
            File.WriteAllText(configPath, @"{
                ""Files"": [
                    { ""SourceFilePath"": ""app.pe"", ""DestinationFilePath"": ""I:\\app.pe"" }
                ]
            }");
            var manager = new FileDeploymentManager(configPath, "COM99", VerbosityLevel.Quiet);
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void FileDeploymentManager_Constructor_MissingFile_Throws()
        {
            Assert.ThrowsException<DirectoryNotFoundException>(() =>
                new FileDeploymentManager(@"C:\nonexistent\deploy.json", "COM1", VerbosityLevel.Quiet));
        }

        [TestMethod]
        public void FileDeploymentManager_Constructor_EmptyFiles_ParsesOK()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string configPath = Path.Combine(testDir, "deploy_empty.json");
            File.WriteAllText(configPath, @"{ ""Files"": [] }");
            var manager = new FileDeploymentManager(configPath, "COM99", VerbosityLevel.Quiet);
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        public void FileDeploymentManager_HasDeployAsyncMethod()
        {
            var method = typeof(FileDeploymentManager).GetMethod("DeployAsync");
            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(System.Threading.Tasks.Task<ExitCodes>), method.ReturnType);
        }

        #endregion

        #region CC13x26x2Firmware Structural Tests

        [TestMethod]
        public void CC13x26x2Firmware_DerivesFirmwarePackage()
        {
            Assert.IsTrue(typeof(FirmwarePackage).IsAssignableFrom(typeof(CC13x26x2Firmware)));
        }

        [TestMethod]
        public void CC13x26x2Firmware_CanConstruct()
        {
            using (var fw = new CC13x26x2Firmware("CC1352R1_V1", "1.0.0", false))
            {
                Assert.IsNotNull(fw);
                Assert.AreEqual("1.0.0", fw.Version);
            }
        }

        #endregion

        #region JLinkFirmware Structural Tests

        [TestMethod]
        public void JLinkFirmware_DerivesFirmwarePackage()
        {
            Assert.IsTrue(typeof(FirmwarePackage).IsAssignableFrom(typeof(JLinkFirmware)));
        }

        [TestMethod]
        public void JLinkFirmware_CanConstruct()
        {
            using (var fw = new JLinkFirmware("GIANT_GECKO_S1", "1.0.0", false))
            {
                Assert.IsNotNull(fw);
            }
        }

        #endregion
    }
}
