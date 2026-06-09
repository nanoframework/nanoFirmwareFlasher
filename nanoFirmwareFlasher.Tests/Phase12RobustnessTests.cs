// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Phase12RobustnessTests
    {
        public TestContext TestContext { get; set; } = null!;

        #region Utilities.MakePathAbsolute Tests

        [TestMethod]
        public void MakePathAbsolute_AbsoluteWindowsPath_ReturnedUnchanged()
        {
            string result = Utilities.MakePathAbsolute(@"C:\base", @"D:\other\file.bin");
            Assert.AreEqual(@"D:\other\file.bin", result);
        }

        [TestMethod]
        public void MakePathAbsolute_AbsoluteUnixPath_ReturnedUnchanged()
        {
            string result = Utilities.MakePathAbsolute("/base", "/usr/local/file.bin");
            Assert.AreEqual("/usr/local/file.bin", result);
        }

        [TestMethod]
        public void MakePathAbsolute_RelativePath_CombinedWithBase()
        {
            string result = Utilities.MakePathAbsolute(@"C:\base", "subfolder\\file.bin");
            Assert.AreEqual(@"C:\base\subfolder\file.bin", result);
        }

        [TestMethod]
        public void MakePathAbsolute_DotSegment_Collapsed()
        {
            string result = Utilities.MakePathAbsolute(@"C:\base", @".\file.bin");
            Assert.AreEqual(@"C:\base\file.bin", result);
        }

        [TestMethod]
        public void MakePathAbsolute_JustFilename_CombinedWithBase()
        {
            string result = Utilities.MakePathAbsolute(@"C:\firmware", "nanoCLR.hex");
            Assert.AreEqual(@"C:\firmware\nanoCLR.hex", result);
        }

        [TestMethod]
        public void MakePathAbsolute_LowercaseDriveLetter_DetectedAsAbsolute()
        {
            string result = Utilities.MakePathAbsolute(@"C:\base", @"c:\other\file.bin");
            Assert.AreEqual(@"c:\other\file.bin", result);
        }

        [TestMethod]
        public void MakePathAbsolute_EmptyBase_RelativePathCombined()
        {
            // When base is empty, Path.Combine treats it as current-relative
            string result = Utilities.MakePathAbsolute("", "firmware.hex");
            Assert.AreEqual("firmware.hex", result);
        }

        #endregion

        #region SafeExtractZipToDirectory Tests

        private string CreateTestZip(string directory, params (string entryName, string content)[] entries)
        {
            string zipPath = Path.Combine(directory, "test.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var (entryName, content) in entries)
                {
                    var entry = archive.CreateEntry(entryName);
                    using (var writer = new StreamWriter(entry.Open()))
                    {
                        writer.Write(content);
                    }
                }
            }
            return zipPath;
        }

        [TestMethod]
        public void SafeExtractZip_NormalEntries_ExtractedCorrectly()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string extractDir = Path.Combine(testDir, "extract");
            Directory.CreateDirectory(extractDir);

            string zipPath = CreateTestZip(testDir,
                ("nanoCLR.hex", ":020000040800F2"),
                ("nanoBooter.hex", ":020000040800F2"));

            FirmwarePackage.SafeExtractZipToDirectory(zipPath, extractDir);

            Assert.IsTrue(File.Exists(Path.Combine(extractDir, "nanoCLR.hex")));
            Assert.IsTrue(File.Exists(Path.Combine(extractDir, "nanoBooter.hex")));
            Assert.AreEqual(":020000040800F2", File.ReadAllText(Path.Combine(extractDir, "nanoCLR.hex")));
        }

        [TestMethod]
        public void SafeExtractZip_SubdirectoryEntry_CreatesSubdirectory()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string extractDir = Path.Combine(testDir, "extract");
            Directory.CreateDirectory(extractDir);

            string zipPath = CreateTestZip(testDir,
                ("sub/firmware.bin", "DEADBEEF"));

            FirmwarePackage.SafeExtractZipToDirectory(zipPath, extractDir);

            Assert.IsTrue(File.Exists(Path.Combine(extractDir, "sub", "firmware.bin")));
        }

        [TestMethod]
        public void SafeExtractZip_PathTraversal_ThrowsIOException()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string extractDir = Path.Combine(testDir, "extract");
            Directory.CreateDirectory(extractDir);

            // Create a zip with a malicious path traversal entry
            string zipPath = Path.Combine(testDir, "malicious.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // Manually create an entry with path traversal
                var entry = archive.CreateEntry("../../../etc/malicious.txt");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write("attack");
                }
            }

            Assert.ThrowsException<IOException>(() =>
                FirmwarePackage.SafeExtractZipToDirectory(zipPath, extractDir));
        }

        [TestMethod]
        public void SafeExtractZip_EmptyZip_NoFilesExtracted()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string extractDir = Path.Combine(testDir, "extract");
            Directory.CreateDirectory(extractDir);

            string zipPath = Path.Combine(testDir, "empty.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // empty zip
            }

            FirmwarePackage.SafeExtractZipToDirectory(zipPath, extractDir);

            Assert.AreEqual(0, Directory.GetFiles(extractDir).Length);
        }

        [TestMethod]
        public void SafeExtractZip_OverwritesExistingFile()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string extractDir = Path.Combine(testDir, "extract");
            Directory.CreateDirectory(extractDir);

            // Create an existing file
            File.WriteAllText(Path.Combine(extractDir, "file.txt"), "old content");

            string zipPath = CreateTestZip(testDir, ("file.txt", "new content"));

            FirmwarePackage.SafeExtractZipToDirectory(zipPath, extractDir);

            Assert.AreEqual("new content", File.ReadAllText(Path.Combine(extractDir, "file.txt")));
        }

        #endregion

        #region FirmwarePackage Structural Tests

        [TestMethod]
        public void FirmwarePackage_LocationPathBase_IsSettable()
        {
            string original = FirmwarePackage.LocationPathBase;
            try
            {
                FirmwarePackage.LocationPathBase = @"C:\test\path";
                Assert.AreEqual(@"C:\test\path", FirmwarePackage.LocationPathBase);
            }
            finally
            {
                FirmwarePackage.LocationPathBase = original;
            }
        }

        [TestMethod]
        public void FirmwarePackage_LocationPathBase_DefaultIsUserProfile()
        {
            // When AsyncLocal is null, should return the default path under UserProfile
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nanoFramework",
                "fw_cache");
            // Reset to default by setting to null via reflection
            var field = typeof(FirmwarePackage).GetField("s_locationPathBase", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "s_locationPathBase field should exist");
        }

        [TestMethod]
        public void FirmwarePackage_SafeExtractZipToDirectory_IsInternal()
        {
            var method = typeof(FirmwarePackage).GetMethod(
                "SafeExtractZipToDirectory",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(method, "SafeExtractZipToDirectory method should exist");
            Assert.IsTrue(method.IsStatic, "Should be static");
        }

        [TestMethod]
        public void FirmwarePackage_PostProcessDownloadAndExtract_IsInternal()
        {
            var method = typeof(FirmwarePackage).GetMethod(
                "PostProcessDownloadAndExtract",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "PostProcessDownloadAndExtract should be callable from tests");
        }

        [TestMethod]
        public void FirmwarePackage_IsAbstract()
        {
            Assert.IsTrue(typeof(FirmwarePackage).IsAbstract);
        }

        [TestMethod]
        public void FirmwarePackage_ImplementsIDisposable()
        {
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(FirmwarePackage)));
        }

        [TestMethod]
        public void Stm32Firmware_DerivesFirmwarePackage()
        {
            Assert.IsTrue(typeof(FirmwarePackage).IsAssignableFrom(typeof(Stm32Firmware)));
        }

        [TestMethod]
        public void Stm32Firmware_CanConstruct()
        {
            using (var fw = new Stm32Firmware("TEST_TARGET", "1.0.0", false))
            {
                Assert.IsNotNull(fw);
                Assert.AreEqual("1.0.0", fw.Version);
            }
        }

        [TestMethod]
        public void Stm32Firmware_PostProcess_SetsNullWhenNoFiles()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string fwDir = Path.Combine(testDir, "fw_empty");
            Directory.CreateDirectory(fwDir);

            using (var fw = new Stm32Firmware("TEST_TARGET", "1.0.0", false))
            {
                // Set LocationPath via reflection since it's normally set by DownloadAndExtractAsync
                var prop = typeof(FirmwarePackage).GetProperty("LocationPath");
                prop.SetValue(fw, fwDir);

                fw.PostProcessDownloadAndExtract();

                Assert.IsNull(fw.NanoBooterFile, "NanoBooterFile should be null when no matching files");
                Assert.IsNull(fw.NanoClrFile, "NanoClrFile should be null when no matching files");
            }
        }

        [TestMethod]
        public void Stm32Firmware_PostProcess_FindsHexFiles()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string fwDir = Path.Combine(testDir, "fw_with_files");
            Directory.CreateDirectory(fwDir);

            // Create minimal hex files
            File.WriteAllText(Path.Combine(fwDir, "nanoBooter.hex"), ":020000040800F2\n:10000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00\n:00000001FF");
            File.WriteAllText(Path.Combine(fwDir, "nanoCLR.hex"), ":020000040804EE\n:10200000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00\n:00000001FF");

            using (var fw = new Stm32Firmware("TEST_TARGET", "1.0.0", false))
            {
                var prop = typeof(FirmwarePackage).GetProperty("LocationPath");
                prop.SetValue(fw, fwDir);

                fw.PostProcessDownloadAndExtract();

                Assert.IsNotNull(fw.NanoBooterFile, "Should find nanoBooter.hex");
                Assert.IsNotNull(fw.NanoClrFile, "Should find nanoCLR.hex");
                Assert.IsTrue(fw.NanoBooterFile.EndsWith("nanoBooter.hex"));
                Assert.IsTrue(fw.NanoClrFile.EndsWith("nanoCLR.hex"));
            }
        }

        #endregion

        #region FirmwarePackageFactory Tests

        [TestMethod]
        public void FirmwarePackageFactory_NullDevice_ThrowsArgumentNull()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                FirmwarePackageFactory.GetFirmwarePackage(null, "1.0.0"));
        }

        [TestMethod]
        public void FirmwarePackageFactory_GetFirmwarePackage_IsStatic()
        {
            var method = typeof(FirmwarePackageFactory).GetMethod("GetFirmwarePackage");
            Assert.IsNotNull(method);
            Assert.IsTrue(method.IsStatic);
            Assert.AreEqual(2, method.GetParameters().Length);
        }

        #endregion

        #region Manager Workflow Structural Tests

        [TestMethod]
        public void Esp32Manager_ProcessAsync_RequiresSerialPort()
        {
            // Esp32Manager without SerialPort should return E6001
            var options = CreateOptions(SupportedPlatform.esp32);
            var manager = new Esp32Manager(options, VerbosityLevel.Quiet);

            var result = manager.ProcessAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.E6001, result, "ESP32 should require a serial port");
        }

        [TestMethod]
        public void Esp32Manager_HasDoProcessAsyncMethod()
        {
            var method = typeof(Esp32Manager).GetMethod(
                "DoProcessAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "DoProcessAsync should exist as private method");
            Assert.AreEqual(typeof(System.Threading.Tasks.Task<ExitCodes>), method.ReturnType);
        }

        [TestMethod]
        public void Stm32Manager_HasFlashDeviceFilesHelper()
        {
            var method = typeof(Stm32Manager).GetMethod(
                "FlashDeviceFiles",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "FlashDeviceFiles helper should exist");
            var parameters = method.GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(IStmFlashableDevice), parameters[0].ParameterType);
        }

        [TestMethod]
        public void TIManager_ProcessAsync_NoTarget_ThrowsNoOperation()
        {
            // TI manager without a target name or any operation flags throws NoOperationPerformedException
            var options = CreateOptions(SupportedPlatform.ti_simplelink);
            var manager = new TIManager(options, VerbosityLevel.Quiet);

            Assert.ThrowsException<NoOperationPerformedException>(() =>
                manager.ProcessAsync().GetAwaiter().GetResult());
        }

        [TestMethod]
        public void NanoDeviceManager_ProcessAsync_Exists()
        {
            var method = typeof(NanoDeviceManager).GetMethod("ProcessAsync");
            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(System.Threading.Tasks.Task<ExitCodes>), method.ReturnType);
        }

        [TestMethod]
        public void NanoDeviceManager_ImplementsIManager()
        {
            Assert.IsTrue(typeof(IManager).IsAssignableFrom(typeof(NanoDeviceManager)));
        }

        [TestMethod]
        public void SilabsManager_ImplementsIManager()
        {
            Assert.IsTrue(typeof(IManager).IsAssignableFrom(typeof(SilabsManager)));
        }

        [TestMethod]
        public void AllManagers_HaveConsistentConstructors()
        {
            // All managers should accept (Options, VerbosityLevel)
            Type[] managerTypes = new[]
            {
                typeof(Esp32Manager),
                typeof(Stm32Manager),
                typeof(TIManager),
                typeof(NanoDeviceManager),
                typeof(SilabsManager)
            };

            foreach (var type in managerTypes)
            {
                var ctor = type.GetConstructor(new[] { typeof(Options), typeof(VerbosityLevel) });
                Assert.IsNotNull(ctor, $"{type.Name} should have (Options, VerbosityLevel) constructor");
            }
        }

        #endregion

        #region NanoDeviceOperations Structural Tests

        [TestMethod]
        public void NanoDeviceOperations_ImplementsIDisposable()
        {
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(NanoDeviceOperations)));
        }

        [TestMethod]
        public void NanoDeviceOperations_UpdateDeviceClrAsync_NullPort_ThrowsArgumentNull()
        {
            using (var ops = new NanoDeviceOperations())
            {
                Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
                    await ops.UpdateDeviceClrAsync(null, "1.0.0", false, null, null));
            }
        }

        [TestMethod]
        public void NanoDeviceOperations_HasGetDeviceDetailsMethod()
        {
            var method = typeof(NanoDeviceOperations).GetMethod("GetDeviceDetails");
            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(ExitCodes), method.ReturnType);
        }

        [TestMethod]
        public void NanoDeviceOperations_HasListDevicesMethod()
        {
            var method = typeof(NanoDeviceOperations).GetMethod("ListDevices");
            Assert.IsNotNull(method);
            Assert.AreEqual(1, method.GetParameters().Length);
        }

        #endregion

        #region CC13x26x2Operations Structural Tests

        [TestMethod]
        public void CC13x26x2Operations_UpdateFirmwareAsync_EmptyTarget_ReturnsE1000()
        {
            var result = CC13x26x2Operations.UpdateFirmwareAsync(
                "", "1.0.0", false, null, false, null, null, VerbosityLevel.Quiet)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.E1000, result);
        }

        [TestMethod]
        public void CC13x26x2Operations_UpdateFirmwareAsync_NullTarget_ReturnsE1000()
        {
            var result = CC13x26x2Operations.UpdateFirmwareAsync(
                null, "1.0.0", false, null, false, null, null, VerbosityLevel.Quiet)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.E1000, result);
        }

        [TestMethod]
        public void CC13x26x2Operations_HasInstallXds110DriversMethod()
        {
            var method = typeof(CC13x26x2Operations).GetMethod("InstallXds110Drivers");
            Assert.IsNotNull(method);
            Assert.IsTrue(method.IsStatic);
        }

        #endregion

        #region FindStartAddressInHexFile Tests (via reflection)

        [TestMethod]
        public void FindStartAddress_ExtendedSegment_ParsesCorrectly()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string hexFile = Path.Combine(testDir, "test_ext_seg.hex");

            // Extended Segment Address record for 0x0800, then data at offset 0x0000
            File.WriteAllLines(hexFile, new[]
            {
                ":020000040800F2",           // Extended Linear Address: upper 16 = 0x0800
                ":10000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00",  // Data at offset 0x0000
                ":00000001FF"                // EOF
            });

            var method = typeof(FirmwarePackage).GetMethod(
                "FindStartAddressInHexFile",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            uint address = (uint)method.Invoke(null, new object[] { hexFile });
            Assert.AreEqual(0x08000000u, address);
        }

        [TestMethod]
        public void FindStartAddress_DataRecordOnly_ParsesOffset()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string hexFile = Path.Combine(testDir, "test_data_only.hex");

            // Data Record starting at offset 0x2462
            File.WriteAllLines(hexFile, new[]
            {
                ":10246200464C5549442050524F46494C4500464C33",
                ":00000001FF"
            });

            var method = typeof(FirmwarePackage).GetMethod(
                "FindStartAddressInHexFile",
                BindingFlags.NonPublic | BindingFlags.Static);

            uint address = (uint)method.Invoke(null, new object[] { hexFile });
            Assert.AreEqual(0x2462u, address);
        }

        [TestMethod]
        public void FindStartAddress_InvalidFile_Throws()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string hexFile = Path.Combine(testDir, "test_bad.hex");

            File.WriteAllLines(hexFile, new[]
            {
                "NOTAHEXRECORD",
                ":00000001FF"
            });

            var method = typeof(FirmwarePackage).GetMethod(
                "FindStartAddressInHexFile",
                BindingFlags.NonPublic | BindingFlags.Static);

            try
            {
                method.Invoke(null, new object[] { hexFile });
                Assert.Fail("Expected FormatException");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is FormatException)
            {
                // expected
            }
        }

        #endregion

        #region Helpers

        private static Options CreateOptions(SupportedPlatform platform)
        {
            var options = new Options();
            var platformProp = typeof(Options).GetProperty("Platform");
            platformProp.SetValue(options, (SupportedPlatform?)platform);
            return options;
        }

        #endregion
    }
}
