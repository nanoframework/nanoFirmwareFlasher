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
    [TestCategory("Firmware archive")]
    public sealed class FirmwareArchiveManagerTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void FirmwareArchiveManager_EmptyArchive()
        {
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");

            var actual = new FirmwareArchiveManager(archiveDirectory);

            List<CloudSmithPackageDetail> list = actual.GetTargetList(false, null, VerbosityLevel.Diagnostic);

            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);
            output.AssertAreEqual($"Listing  targets from firmware archive '{archiveDirectory}'...");
        }

        [TestMethod]
        [TestCategory("CloudSmith")]
        [DataRow(true, true)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(false, false)]
        public void FirmwareArchiveManager_SinglePackage(bool isReferenceTarget, bool keepAllVersions)
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            Directory.CreateDirectory(archiveDirectory);
            Dictionary<string, List<CloudSmithPackageDetail>> packages = GetTargetListHelper.GetTargetList(!isReferenceTarget, false, SupportedPlatform.esp32, false)
                                                                            .GroupBy(p => p.Name)
                                                                            .ToDictionary(g => g.Key, g => g.ToList());
            CloudSmithPackageDetail? packageOldVersion = null;
            CloudSmithPackageDetail? packageLatestVersion = null;
            foreach (List<CloudSmithPackageDetail> packageList in packages.Values)
            {
                if (packageList.Count > 1)
                {
                    Version? latest = null;
                    foreach (CloudSmithPackageDetail p in packageList)
                    {
                        var version = Version.Parse(p.Version);
                        if (packageLatestVersion is null || version > latest)
                        {
                            packageOldVersion = packageLatestVersion ?? p;
                            packageLatestVersion = p;
                            latest = version;
                        }
                        else
                        {
                            packageOldVersion = p;
                        }
                    }
                }
            }
            if (packageLatestVersion is null || packageOldVersion is null)
            {
                Assert.Inconclusive("No target found in CloudSmith with two firmware versions");
            }

            // Other target should not be deleted, even if keepAllVersions = false
            string otherTarget = "OTHER_TARGET";
            string otherTargetVersion = "1.0.0.0";
            string otherTargetBasePath = Path.Combine(archiveDirectory, $"{otherTarget}-{otherTargetVersion}.zip");
            File.WriteAllText(otherTargetBasePath, "");
            File.WriteAllText(otherTargetBasePath + ".json", $@"{{ ""Name"": ""{otherTarget}"", ""Version"": ""{otherTargetVersion}"", ""Platform"": ""{SupportedPlatform.esp32}"" }}");

            output.Reset();
            #endregion

            #region Download the old package
            var actual = new FirmwareArchiveManager(archiveDirectory);

            // Note that the platform is determined by the logic in the nanoff tool if a target is specified.
            ExitCodes exitCode = actual.DownloadFirmwareFromRepository(false, SupportedPlatform.esp32, packageOldVersion.Name, packageOldVersion.Version, keepAllVersions, VerbosityLevel.Detailed)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(output.Output.Contains($"Added target {packageOldVersion.Name} {packageOldVersion.Version} to the archive"));
            #endregion

            #region Assert it is present and can be found via GetTargetList
            Assert.IsTrue(File.Exists(Path.Combine(archiveDirectory, $"{packageOldVersion.Name}-{packageOldVersion.Version}.zip")));

            // List of all packages
            output.Reset();
            List<CloudSmithPackageDetail> list = actual.GetTargetList(false, null, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            string present = string.Join("\n", from p in list select $"{p.Name} {p.Version}");
            Assert.IsTrue(present.Contains($"{packageOldVersion.Name} {packageOldVersion.Version}"));
            Assert.IsFalse(present.Contains($"{packageLatestVersion.Name} {packageLatestVersion.Version}"));
            Assert.IsTrue(present.Contains($"{otherTarget} {otherTargetVersion}"));

            // List of esp32 packages
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.esp32, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            present = string.Join("\n", from p in list select $"{p.Name} {p.Version}");
            Assert.IsTrue(present.Contains($"{packageOldVersion.Name} {packageOldVersion.Version}"));
            Assert.IsFalse(present.Contains($"{packageLatestVersion.Name} {packageLatestVersion.Version}"));
            Assert.IsTrue(present.Contains($"{otherTarget} {otherTargetVersion}"));

            // List of stm32 packages - no match
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.stm32, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                "",
                string.Join("\n", from p in list select $"{p.Name} {p.Version}"));
            #endregion

            #region Download the latest version of the package (without specifying version number)
            output.Reset();
            actual = new FirmwareArchiveManager(archiveDirectory);

            // Note that the platform is determined by the logic in the nanoff tool if a target is specified.
            exitCode = actual.DownloadFirmwareFromRepository(false, SupportedPlatform.esp32, packageLatestVersion.Name, null, keepAllVersions, VerbosityLevel.Detailed)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(output.Output.Contains($"Added target {packageLatestVersion.Name} {packageLatestVersion.Version} to the archive"));
            #endregion

            #region Assert the latest version is present, and the old version is present only for keepAllVersions = true
            Assert.IsTrue(File.Exists(Path.Combine(archiveDirectory, $"{packageLatestVersion.Name}-{packageLatestVersion.Version}.zip")));
            Assert.AreEqual(keepAllVersions, File.Exists(Path.Combine(archiveDirectory, $"{packageOldVersion.Name}-{packageOldVersion.Version}.zip")));

            // List of all packages
            output.Reset();
            list = actual.GetTargetList(false, null, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            present = string.Join("\n", from p in list select $"{p.Name} {p.Version}");
            Assert.IsTrue(present.Contains($"{packageLatestVersion.Name} {packageLatestVersion.Version}"), "New version");
            Assert.AreEqual(keepAllVersions, present.Contains($"{packageOldVersion.Name} {packageOldVersion.Version}"), "Old version");
            #endregion
        }

        [TestMethod]
        [TestCategory("CloudSmith")]
        [DataRow(true)]
        [DataRow(false)]
        public void FirmwareArchiveManager_nanoCLR(bool keepAllVersions)
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            string targetName = "WIN_DLL_nanoCLR";
            List<CloudSmithPackageDetail> packages = (from p in GetTargetListHelper.GetTargetList(false, false, null, false)
                                                      where p.Name == targetName
                                                      select p).ToList();
            CloudSmithPackageDetail? packageOldVersion = null;
            CloudSmithPackageDetail? packageLatestVersion = null;
            if (packages.Count > 1)
            {
                Version? latest = null;
                foreach (CloudSmithPackageDetail p in packages)
                {
                    var version = Version.Parse(p.Version);
                    if (packageLatestVersion is null || version > latest)
                    {
                        packageOldVersion = packageLatestVersion ?? p;
                        packageLatestVersion = p;
                        latest = version;
                    }
                    else
                    {
                        packageOldVersion = p;
                    }
                }
            }
            if (packageLatestVersion is null || packageOldVersion is null)
            {
                Assert.Inconclusive("CloudSmith does not have two firmware versions");
            }
            #endregion

            #region Download the old version of the package
            var actual = new FirmwareArchiveManager(archiveDirectory);

            ExitCodes exitCode = actual.DownloadFirmwareFromRepository(false, null, targetName, packageOldVersion.Version, keepAllVersions, VerbosityLevel.Detailed)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(output.Output.Contains($"Added target {targetName} "));
            #endregion

            #region Assert it is present
            var targetDirectory = Path.Combine(archiveDirectory, $"{targetName}-{packageOldVersion.Version}");
            Assert.IsTrue(Directory.Exists(targetDirectory));
            Assert.IsTrue(File.Exists(Path.Combine(targetDirectory, "nanoFramework.nanoCLR.dll")));
            Assert.IsTrue(File.Exists($"{targetDirectory}.json"));

            // List of all packages
            output.Reset();
            List<CloudSmithPackageDetail> list = actual.GetTargetList(false, null, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            string present = string.Join("\n", from p in list select $"{p.Name} {p.Version}");
            Assert.IsTrue(present.Contains($"{targetName} {packageOldVersion.Version}"));
            #endregion

            #region Download the latest version of the package
            output.Reset();
            actual = new FirmwareArchiveManager(archiveDirectory);

            exitCode = actual.DownloadFirmwareFromRepository(false, null, targetName, null, keepAllVersions, VerbosityLevel.Detailed)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(output.Output.Contains($"Added target {targetName} "));
            #endregion

            #region Assert it is present and the old package is present depending on keepAllVersions
            targetDirectory = Path.Combine(archiveDirectory, $"{targetName}-{packageLatestVersion.Version}");
            Assert.IsTrue(Directory.Exists(targetDirectory));
            Assert.IsTrue(File.Exists(Path.Combine(targetDirectory, "nanoFramework.nanoCLR.dll")));
            Assert.IsTrue(File.Exists($"{targetDirectory}.json"));

            targetDirectory = Path.Combine(archiveDirectory, $"{targetName}-{packageOldVersion.Version}");
            Assert.AreEqual(keepAllVersions, Directory.Exists(targetDirectory));

            // List of all packages
            output.Reset();
            list = actual.GetTargetList(false, null, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            present = string.Join("\n", from p in list select $"{p.Name} {p.Version}");
            Assert.IsTrue(present.Contains($"{targetName} {packageLatestVersion.Version}"), "New version");
            Assert.AreEqual(keepAllVersions, present.Contains($"{targetName} {packageOldVersion.Version}"), "Old version");
            #endregion
        }

        [TestMethod]
        [TestCategory("CloudSmith")]
        [DataRow(true)]
        [DataRow(false)]
        public void FirmwareArchiveManager_Platform(bool keepAllVersions)
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            Directory.CreateDirectory(archiveDirectory);

            string targetNoLongerPresent = "NO_LONGER_PRESENT";
            string targetNoLongerPresentVersion = "1.0.0.0";
            string targetNoLongerPresentBasePath = Path.Combine(archiveDirectory, $"{targetNoLongerPresent}-{targetNoLongerPresentVersion}.zip");
            File.WriteAllText(targetNoLongerPresentBasePath, "");
            File.WriteAllText(targetNoLongerPresentBasePath + ".json", $@"{{ ""Name"": ""{targetNoLongerPresent}"", ""Version"": ""{targetNoLongerPresentVersion}"", ""Platform"": ""{SupportedPlatform.ti_simplelink}"" }}");

            List<CloudSmithPackageDetail> expectedPackages = GetTargetListHelper.GetTargetList(null, false, SupportedPlatform.ti_simplelink);
            if (keepAllVersions)
            {
                expectedPackages.Add(new CloudSmithPackageDetail
                {
                    Name = targetNoLongerPresent,
                    Version = targetNoLongerPresentVersion
                });
            }

            output.Reset();
            #endregion

            #region Download all packages
            var actual = new FirmwareArchiveManager(archiveDirectory);

            ExitCodes exitCode = actual.DownloadFirmwareFromRepository(false, SupportedPlatform.ti_simplelink, null, null, keepAllVersions, VerbosityLevel.Quiet)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            output.AssertAreEqual("");
            #endregion

            #region Assert the packages can be found via GetTargetList
            // List of all packages
            output.Reset();
            List<CloudSmithPackageDetail> list = actual.GetTargetList(false, null, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                string.Join("\n", from p in expectedPackages orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n',
                string.Join("\n", from p in list orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n'
            );

            // List of ti_simplelink packages
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.ti_simplelink, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                string.Join("\n", from p in expectedPackages orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n',
                string.Join("\n", from p in list orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n'
            );

            // List of stm32 packages - no match
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.esp32, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                "",
                string.Join("\n", from p in list select $"{p.Name} {p.Version}"));
            #endregion
        }
    }
}
