// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Phase 3 of the verbs + words CLI proposal: tests for <see cref="VerbOptionsMapper"/>,
    /// which adapts each new verb option class into the legacy flat <see cref="Options"/> bag
    /// so the existing platform managers can be reused unchanged.
    /// </summary>
    [TestClass]
    public class VerbOptionsMapperTests
    {
        // ======================================================================
        // FlashOptions
        // ======================================================================

        #region FlashOptions

        [TestMethod]
        public void Flash_AlwaysSetsUpdate()
        {
            var legacy = new FlashOptions { TargetName = "ESP_WROVER_KIT" }.ToLegacyOptions();

            Assert.IsTrue(legacy.Update);
        }

        [TestMethod]
        public void Flash_NoInterface_LeavesAllInterfaceFlagsFalse()
        {
            var legacy = new FlashOptions { TargetName = "ESP_WROVER_KIT" }.ToLegacyOptions();

            Assert.IsFalse(legacy.NativeDfuUpdate);
            Assert.IsFalse(legacy.NativeStLinkUpdate);
            Assert.IsFalse(legacy.NativeSwdUpdate);
            Assert.IsFalse(legacy.DfuUpdate);
            Assert.IsFalse(legacy.JtagUpdate);
        }

        [TestMethod]
        public void Flash_BaudRate_MapsThroughToLegacyOptions()
        {
            var legacy = new FlashOptions { TargetName = "ESP_WROVER_KIT", BaudRate = 921600 }.ToLegacyOptions();

            Assert.AreEqual(921600, legacy.BaudRate);
        }

        [TestMethod]
        public void Flash_Esp32TuningParams_MapThroughToLegacyOptions()
        {
            var legacy = new FlashOptions
            {
                TargetName = "ESP_WROVER_KIT",
                Esp32FlashMode = "qio",
                Esp32FlashFrequency = 80,
                Esp32PartitionTableSize = PartitionTableSize._8
            }.ToLegacyOptions();

            Assert.AreEqual("qio", legacy.Esp32FlashMode);
            Assert.AreEqual(80, legacy.Esp32FlashFrequency);
            Assert.AreEqual(PartitionTableSize._8, legacy.Esp32PartitionTableSize);
        }

        [TestMethod]
        public void Flash_DeviceIdWithoutInterface_StillMapsToJLinkDeviceId()
        {
            // EFM32 (J-Link) has no "interface" concept like STM32, so deviceid
            // must reach JLinkDeviceId even when Interface is null.
            var legacy = new FlashOptions
            {
                TargetName = "SL_STK3701A",
                DeviceId = "jlink-probe-1"
            }.ToLegacyOptions();

            Assert.AreEqual("jlink-probe-1", legacy.JLinkDeviceId);
            Assert.IsNull(legacy.DfuDeviceId);
            Assert.IsNull(legacy.JtagDeviceId);
        }

        [TestMethod]
        public void Flash_VcpBaudRate_MapsThroughToLegacyOptions()
        {
            var legacy = new FlashOptions
            {
                TargetName = "SL_STK3701A",
                VcpBaudRate = 460800
            }.ToLegacyOptions();

            Assert.AreEqual(460800, legacy.SetVcpBaudRate);
        }

        [TestMethod]
        public void Flash_NoVcpBaudRate_StaysNull()
        {
            var legacy = new FlashOptions { TargetName = "SL_STK3701A" }.ToLegacyOptions();

            Assert.IsNull(legacy.SetVcpBaudRate);
        }

        [TestMethod]
        public void Flash_InterfaceDfu_MapsToNativeDfuUpdate()
        {
            var legacy = new FlashOptions
            {
                TargetName = "ST_STM32F769I_DISCOVERY",
                Interface = FlashInterface.Dfu,
                DeviceId = "abc123"
            }.ToLegacyOptions();

            Assert.IsTrue(legacy.NativeDfuUpdate);
            Assert.AreEqual("abc123", legacy.DfuDeviceId);
            Assert.IsFalse(legacy.DfuUpdate);
        }

        [TestMethod]
        public void Flash_InterfaceJtag_MapsToNativeStLinkUpdate()
        {
            var legacy = new FlashOptions
            {
                TargetName = "ST_STM32F769I_DISCOVERY",
                Interface = FlashInterface.Jtag,
                DeviceId = "probe1"
            }.ToLegacyOptions();

            Assert.IsTrue(legacy.NativeStLinkUpdate);
            Assert.AreEqual("probe1", legacy.JtagDeviceId);
            Assert.IsFalse(legacy.JtagUpdate);
        }

        [TestMethod]
        public void Flash_InterfaceNativeSwd_MapsToNativeSwdUpdate()
        {
            var legacy = new FlashOptions
            {
                TargetName = "ST_STM32F769I_DISCOVERY",
                Interface = FlashInterface.NativeSwd,
                DeviceId = "probe2"
            }.ToLegacyOptions();

            Assert.IsTrue(legacy.NativeSwdUpdate);
            Assert.AreEqual("probe2", legacy.JtagDeviceId);
        }

        [TestMethod]
        public void Flash_TargetGiven_NanoDeviceIsFalse()
        {
            var legacy = new FlashOptions { TargetName = "ESP_WROVER_KIT", SerialPort = "COM7" }.ToLegacyOptions();

            Assert.IsFalse(legacy.NanoDevice);
        }

        [TestMethod]
        public void Flash_NoTargetOrPlatformButSerialPort_SetsNanoDevice()
        {
            var legacy = new FlashOptions { SerialPort = "COM7" }.ToLegacyOptions();

            Assert.IsTrue(legacy.NanoDevice);
        }

        [TestMethod]
        public void Flash_NoTargetPlatformOrSerialPort_NanoDeviceIsFalse()
        {
            var legacy = new FlashOptions().ToLegacyOptions();

            Assert.IsFalse(legacy.NanoDevice);
        }

        [TestMethod]
        public void Flash_NullListProperties_MappedToEmptyLists()
        {
            var legacy = new FlashOptions { TargetName = "ESP_WROVER_KIT" }.ToLegacyOptions();

            Assert.IsNotNull(legacy.HexFile);
            Assert.IsNotNull(legacy.BinFile);
            Assert.IsNotNull(legacy.FlashAddress);
            Assert.AreEqual(0, legacy.HexFile.Count);
        }

        [TestMethod]
        public void Flash_NoBackupFlash_NoBackupPathOrFile()
        {
            // "backupflash" not given => no whole-flash backup at all (tool default)
            var legacy = new FlashOptions { TargetName = "ESP_WROVER_KIT" }.ToLegacyOptions();

            Assert.IsTrue(string.IsNullOrEmpty(legacy.BackupPath));
            Assert.IsTrue(string.IsNullOrEmpty(legacy.BackupFile));
        }

        [TestMethod]
        public void Flash_BackupFlash_SplitsIntoDirectoryAndFileName()
        {
            var legacy = new FlashOptions
            {
                TargetName = "ESP_WROVER_KIT",
                BackupFlash = Path.Combine("backups", "esp32-flash-backup.bin")
            }.ToLegacyOptions();

            Assert.AreEqual("backups", legacy.BackupPath);
            Assert.AreEqual("esp32-flash-backup.bin", legacy.BackupFile);
        }

        [TestMethod]
        public void Flash_BackupFlashWithFileNameOnly_UsesCurrentDirectory()
        {
            var legacy = new FlashOptions
            {
                TargetName = "ESP_WROVER_KIT",
                BackupFlash = "esp32-flash-backup.bin"
            }.ToLegacyOptions();

            Assert.AreEqual(".", legacy.BackupPath);
            Assert.AreEqual("esp32-flash-backup.bin", legacy.BackupFile);
        }

        #endregion


        // ======================================================================
        // DeployOptions
        // ======================================================================

        #region DeployOptions

        [TestMethod]
        public void Deploy_WithImage_SetsDeployTrue()
        {
            var legacy = new DeployOptions { DeploymentImage = "app.bin" }.ToLegacyOptions();

            Assert.IsTrue(legacy.Deploy);
            Assert.AreEqual("app.bin", legacy.DeploymentImage);
        }

        [TestMethod]
        public void Deploy_WithFileOnly_DeployIsFalse()
        {
            var legacy = new DeployOptions { FileDeployment = "deploy.json" }.ToLegacyOptions();

            Assert.IsFalse(legacy.Deploy);
            Assert.AreEqual("deploy.json", legacy.FileDeployment);
        }

        [TestMethod]
        public void Deploy_ImageWithoutTargetButSerialPort_SetsNanoDevice()
        {
            var legacy = new DeployOptions { DeploymentImage = "app.bin", SerialPort = "COM7" }.ToLegacyOptions();

            Assert.IsTrue(legacy.NanoDevice);
        }

        [TestMethod]
        public void Deploy_ImageWithTarget_NanoDeviceIsFalse()
        {
            var legacy = new DeployOptions
            {
                DeploymentImage = "app.bin",
                TargetName = "ESP_WROVER_KIT",
                SerialPort = "COM7"
            }.ToLegacyOptions();

            Assert.IsFalse(legacy.NanoDevice);
        }

        #endregion

        // ======================================================================
        // ListOptions
        // ======================================================================

        #region ListOptions

        [TestMethod]
        public void List_Targets_MapsListTargets()
        {
            var legacy = new ListOptions { Targets = true }.ToLegacyOptions();

            Assert.IsTrue(legacy.ListTargets);
        }

        [TestMethod]
        [DataRow(true, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        public void List_DfuJtagOrNativeSwd_AutoSetsStm32Platform(bool dfu, bool jtag, bool nativeSwd)
        {
            var legacy = new ListOptions { Dfu = dfu, Jtag = jtag, NativeSwd = nativeSwd }.ToLegacyOptions();

            Assert.AreEqual(SupportedPlatform.stm32, legacy.Platform);
        }

        [TestMethod]
        public void List_JLink_AutoSetsEfm32Platform()
        {
            var legacy = new ListOptions { JLink = true }.ToLegacyOptions();

            Assert.AreEqual(SupportedPlatform.efm32, legacy.Platform);
        }

        [TestMethod]
        public void List_ExplicitPlatform_NotOverridden()
        {
            var legacy = new ListOptions { Dfu = true, Platform = SupportedPlatform.esp32 }.ToLegacyOptions();

            Assert.AreEqual(SupportedPlatform.esp32, legacy.Platform);
        }

        [TestMethod]
        public void List_Dfu_MapsToNativeDfuDevicesOnly()
        {
            var legacy = new ListOptions { Dfu = true }.ToLegacyOptions();

            Assert.IsTrue(legacy.ListNativeDfuDevices);
            Assert.IsFalse(legacy.ListDevicesInDfuMode);
        }

        [TestMethod]
        public void List_Jtag_MapsToNativeStLinkDevicesOnly()
        {
            var legacy = new ListOptions { Jtag = true }.ToLegacyOptions();

            Assert.IsTrue(legacy.ListNativeStLinkDevices);
            Assert.IsFalse(legacy.ListJtagDevices);
        }

        #endregion

        // ======================================================================
        // DetailsOptions / IdentifyOptions
        // ======================================================================

        #region DetailsOptions and IdentifyOptions

        [TestMethod]
        public void Details_SetsDeviceDetails()
        {
            var legacy = new DetailsOptions { TargetName = "ESP_WROVER_KIT" }.ToLegacyOptions();

            Assert.IsTrue(legacy.DeviceDetails);
        }

        [TestMethod]
        public void Details_NoTargetOrPlatformButSerialPort_SetsNanoDevice()
        {
            var legacy = new DetailsOptions { SerialPort = "COM7" }.ToLegacyOptions();

            Assert.IsTrue(legacy.NanoDevice);
        }

        [TestMethod]
        public void Identify_SetsIdentifyFirmware()
        {
            var legacy = new IdentifyOptions { Platform = SupportedPlatform.rpi_pico }.ToLegacyOptions();

            Assert.IsTrue(legacy.IdentifyFirmware);
        }

        [TestMethod]
        public void Identify_NoPlatformButSerialPort_SetsNanoDeviceAndUpdate()
        {
            var legacy = new IdentifyOptions { SerialPort = "COM7" }.ToLegacyOptions();

            Assert.IsTrue(legacy.NanoDevice);
            Assert.IsTrue(legacy.Update);
        }

        [TestMethod]
        public void Identify_PlatformGiven_NanoDeviceIsFalse()
        {
            var legacy = new IdentifyOptions { Platform = SupportedPlatform.rpi_pico, SerialPort = "COM7" }.ToLegacyOptions();

            Assert.IsFalse(legacy.NanoDevice);
        }

        #endregion

        // ======================================================================
        // CacheOptions
        // ======================================================================

        #region CacheOptions

        [TestMethod]
        public void Cache_Clear_MapsClearCache()
        {
            var legacy = new CacheOptions { Clear = true }.ToLegacyOptions();

            Assert.IsTrue(legacy.ClearCache);
            Assert.IsFalse(legacy.UpdateFwArchive);
        }

        [TestMethod]
        public void Cache_Download_MapsUpdateFwArchive()
        {
            var legacy = new CacheOptions
            {
                Download = true,
                FwArchivePath = "./fw-cache",
                Platform = SupportedPlatform.esp32
            }.ToLegacyOptions();

            Assert.IsTrue(legacy.UpdateFwArchive);
            Assert.IsFalse(legacy.ClearCache);
            Assert.AreEqual("./fw-cache", legacy.FwArchivePath);
        }

        #endregion
    }
}
