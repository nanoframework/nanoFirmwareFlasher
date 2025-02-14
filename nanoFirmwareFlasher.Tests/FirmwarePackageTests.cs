// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class FirmwarePackageTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        [TestCategory("CloudSmith")]
        public void FirmwarePackage_ListReferenceTargets()
        {
            using var output = new OutputWriterHelper();

            #region Get the stable packages

            List<CloudSmithPackageDetail> stable = FirmwarePackage.GetTargetList(
                false,
                false,
                null,
                VerbosityLevel.Diagnostic);

            Assert.IsNotNull(stable);
            Assert.AreNotEqual(0, stable.Count);

            #endregion

            #region Get the preview packages

            output.Reset();
            List<CloudSmithPackageDetail> preview = FirmwarePackage.GetTargetList(
                false,
                true,
                null,
                VerbosityLevel.Quiet);

            Assert.IsNotNull(preview);

            // Assert that the preview packages are not part of the stable package list
            foreach (CloudSmithPackageDetail previewPackage in preview)
            {
                Assert.IsFalse((from s in stable
                                where s.Name == previewPackage.Name && s.Version == previewPackage.Version
                                select s).Any());
            }

            #endregion

            #region Get the stable esp32 packages

            output.Reset();
            List<CloudSmithPackageDetail> stableEsp32 = FirmwarePackage.GetTargetList(
                false,
                false,
                SupportedPlatform.esp32,
                VerbosityLevel.Diagnostic);

            Assert.IsNotNull(stableEsp32);
            Assert.AreNotEqual(0, stableEsp32.Count);

            // Assert that there are more stable packages than for the esp32
            Assert.IsTrue(stableEsp32.Count < stable.Count);

            // Assert that all esp32 packages are in the stable list
            foreach (CloudSmithPackageDetail esp32Package in stableEsp32)
            {
                Assert.IsTrue(stable.Any(s => s.Name == esp32Package.Name && s.Version == esp32Package.Version),
                              $"Package {esp32Package.Name} with version {esp32Package.Version} is not in the stable list.");
            }

            #endregion
        }

        [TestMethod]
        [TestCategory("CloudSmith")]
        public void FirmwarePackage_ListCommunityTargets()
        {
            using var output = new OutputWriterHelper();

            #region Get the stable packages
            List<CloudSmithPackageDetail> stable = FirmwarePackage.GetTargetList(true, false, null, VerbosityLevel.Diagnostic);
            Assert.IsNotNull(stable);
            Assert.AreNotEqual(0, stable.Count);
            
            #endregion

            #region Get the stable esp32 packages
            output.Reset();
            List<CloudSmithPackageDetail> stableEsp32 = FirmwarePackage.GetTargetList(true, false, SupportedPlatform.esp32, VerbosityLevel.Quiet);
            Assert.AreNotEqual(0, stableEsp32.Count);
            output.AssertAreEqual("");

            // Assert that there are more stable packages than for the esp32
            Assert.IsTrue(stableEsp32.Count < stable.Count);

            // Assert that all esp32 packages are in the stable list
            foreach (CloudSmithPackageDetail esp32Package in stableEsp32)
            {
                Assert.IsTrue((from s in stable
                               where s.Name == esp32Package.Name && s.Version == esp32Package.Version
                               select s).Any());
            }
            #endregion
        }

        [TestMethod]
        [TestCategory("CloudSmith")]
        [DataRow(true)]
        [DataRow(false)]
        public void FirmwarePackage_Download(bool isReferenceTarget)
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string cacheDirectory = Path.Combine(testDirectory, TestDirectoryHelper.LocationPathBase_RelativePath);
            List<CloudSmithPackageDetail> stable = GetTargetListHelper.GetTargetList(!isReferenceTarget, false, SupportedPlatform.esp32, false);
            CloudSmithPackageDetail? newerPackage = null;
            CloudSmithPackageDetail? package = null;
            for (int i = 0; i < stable.Count; i++)
            {
                for (int j = i + 1; j < stable.Count; j++)
                {
                    if (stable[i].Name == stable[j].Name && stable[i].Version != stable[j].Version)
                    {
                        if (new Version(stable[i].Version) > new Version(stable[j].Version))
                        {
                            newerPackage = stable[i];
                            package = stable[j];
                        }
                        else
                        {
                            newerPackage = stable[j];
                            package = stable[i];
                        }
                        break;
                    }
                }
                if (newerPackage is not null)
                {
                    break;
                }
            }
            if (newerPackage is null || package is null)
            {
                Assert.Inconclusive("No ESP32 package available with two versions???");
            }
            #endregion

            #region Download older version
            var actual = new Esp32Firmware(package!.Name, package.Version, false, null);
            ExitCodes exitCode = actual.DownloadAndExtractAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(Directory.Exists(Path.Combine(cacheDirectory, package.Name)));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, package.Name, $"{package.Name}-{package.Version}.zip")));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, package.Name, "nanoCLR.bin")));
            #endregion

            #region Download newer version
            DateTime modified = File.GetLastWriteTimeUtc(Path.Combine(cacheDirectory, package.Name, "nanoCLR.bin"));

            actual = new Esp32Firmware(package!.Name, newerPackage.Version, false, null);
            exitCode = actual.DownloadAndExtractAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(Directory.Exists(Path.Combine(cacheDirectory, package.Name)));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, package.Name, $"{newerPackage.Name}-{newerPackage.Version}.zip")));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, package.Name, "nanoCLR.bin")));
            Assert.AreNotEqual(modified, File.GetLastWriteTimeUtc(Path.Combine(cacheDirectory, package.Name, "nanoCLR.bin")));
            #endregion
        }


        [TestMethod]
        [TestCategory("CloudSmith")]
        public void FirmwarePackage_VirtualDevice_Download()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string cacheDirectory = Path.Combine(testDirectory, TestDirectoryHelper.LocationPathBase_RelativePath);
            string targetName = "WIN_DLL_nanoCLR";
            #endregion

            #region Download Virtual Device firmware
            var actual = new Esp32Firmware(targetName, null, false, null);
            ExitCodes exitCode = actual.DownloadAndExtractAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(Directory.Exists(Path.Combine(cacheDirectory, targetName)));
            string[] directories = (from d in Directory.GetDirectories(Path.Combine(cacheDirectory, targetName))
                                    select Path.GetFileName(d)).ToArray();
            Assert.AreEqual(1, (from d in directories
                                where d.StartsWith(targetName + "-")
                                select d).Count());
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, targetName, directories[0], "nanoFramework.nanoCLR.dll")));
            Assert.AreEqual(1, directories.Length);
            #endregion
        }


        [TestMethod]
        [TestCategory("CloudSmith")]
        public void FirmwarePackage_LegacyVirtualDevice_Download()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string cacheDirectory = Path.Combine(testDirectory, TestDirectoryHelper.LocationPathBase_RelativePath);
            string targetName = "WIN32_nanoCLR";
            #endregion

            #region Download Virtual Device firmware
            var actual = new Esp32Firmware(targetName, null, false, null);
            ExitCodes exitCode = actual.DownloadAndExtractAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(Directory.Exists(Path.Combine(cacheDirectory, targetName)));
            string[] directories = (from d in Directory.GetDirectories(Path.Combine(cacheDirectory, targetName))
                                    select Path.GetFileName(d)).ToArray();
            Assert.AreEqual(1, (from d in directories
                                where d.StartsWith(targetName + "-")
                                select d).Count());
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, targetName, directories[0], "nanoFramework.nanoCLR.dll")));
            Assert.AreEqual(1, directories.Length);
            #endregion
        }

        [TestMethod]
        [TestCategory("Firmware archive")]
        public void FirmwarePackage_FromArchive()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            Directory.CreateDirectory(archiveDirectory);
            string cacheDirectory = Path.Combine(testDirectory, TestDirectoryHelper.LocationPathBase_RelativePath);
            CloudSmithPackageDetail package = GetTargetListHelper.GetTargetList(false, false, SupportedPlatform.ti_simplelink)[0];
            var firmware = new Esp32Firmware(package.Name, package.Version, false, null);
            ExitCodes exitCode = firmware.DownloadAndExtractAsync(null).GetAwaiter().GetResult();
            if (ExitCodes.OK != exitCode)
            {
                Assert.Inconclusive("Cannot download the ESP32 package.");
            }
            string testTarget1Name = "TARGET1_IN_ARCHIVE_ONLY";
            string testTarget2Name = "TARGET2_IN_ARCHIVE_ONLY";
            string testVersion = "1.23.4.5";
            string testOldVersion = "1.6.7.8";

            // Copy the package to the archive directory, modified for TARGET1_IN_ARCHIVE_ONLY and TARGET2_IN_ARCHIVE_ONLY
            File.Copy(Path.Combine(cacheDirectory, package.Name, $"{package.Name}-{package.Version}.zip"), Path.Combine(archiveDirectory, $"{testTarget1Name}-{testVersion}.zip"));
            File.WriteAllText(Path.Combine(archiveDirectory, $"{testTarget1Name}-{testVersion}.zip.json"), $@"{{ ""Name"": ""{testTarget1Name}"", ""Version"": ""{testVersion}"", ""Platform"": ""esp32"" }}");
            File.Copy(Path.Combine(cacheDirectory, package.Name, $"{package.Name}-{package.Version}.zip"), Path.Combine(archiveDirectory, $"{testTarget2Name}-{testVersion}.zip"));
            File.WriteAllText(Path.Combine(archiveDirectory, $"{testTarget2Name}-{testVersion}.zip.json"), $@"{{ ""Name"": ""{testTarget2Name}"", ""Version"": ""{testVersion}"", ""Platform"": ""esp32"" }}");

            // Create an invalid package for an earlier version of TARGET2_IN_ARCHIVE_ONLY - this should not be used
            File.WriteAllText(Path.Combine(archiveDirectory, $"{testTarget2Name}-{testOldVersion}.zip"), "");
            File.WriteAllText(Path.Combine(archiveDirectory, $"{testTarget2Name}-{testOldVersion}.zip.json"), $@"{{ ""Name"": ""{testTarget2Name}"", ""Version"": ""{testOldVersion}"", ""Platform"": ""esp32"" }}");
            #endregion

            #region Get package that exist in the archive directory but not in the repository; include version
            output.Reset();
            var actual = new Esp32Firmware(testTarget1Name, testVersion, false, null);
            exitCode = actual.DownloadAndExtractAsync(archiveDirectory).GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            output.AssertAreEqual("");
            Assert.IsTrue(Directory.Exists(Path.Combine(cacheDirectory, testTarget1Name)));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, testTarget1Name, $"{testTarget1Name}-{testVersion}.zip")));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, testTarget1Name, "nanoCLR.bin")));
            #endregion

            #region Get package that exist in the archive directory but not in the repository; do not include version
            output.Reset();
            actual = new Esp32Firmware(testTarget2Name, null, false, null);
            exitCode = actual.DownloadAndExtractAsync(archiveDirectory).GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            output.AssertAreEqual("");
            Assert.IsTrue(Directory.Exists(Path.Combine(cacheDirectory, testTarget2Name)));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, testTarget2Name, $"{testTarget2Name}-{testVersion}.zip")));
            Assert.IsTrue(File.Exists(Path.Combine(cacheDirectory, testTarget2Name, "nanoCLR.bin")));
            #endregion

            #region Get package that does not exist in the archive directory
            testTarget1Name = "MISSING_TARGET";

            actual = new Esp32Firmware(testTarget1Name, testVersion, false, null);
            exitCode = actual.DownloadAndExtractAsync(archiveDirectory).GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.E9015, exitCode);
            output.AssertAreEqual("");
            Assert.IsFalse(File.Exists(Path.Combine(cacheDirectory, testTarget1Name, $"{testTarget1Name}-{testVersion}.zip")));
            #endregion
        }
    }
}
