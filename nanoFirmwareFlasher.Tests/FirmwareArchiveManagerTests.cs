// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        [DataRow(true)]
        [DataRow(false)]
        public void FirmwareArchiveManager_SinglePackage(bool isReferenceTarget)
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            CloudSmithPackageDetail package = GetTargetListHelper.GetTargetList(!isReferenceTarget, false, SupportedPlatform.esp32)[0];
            output.Reset();
            #endregion

            #region Download the package
            var actual = new FirmwareArchiveManager(archiveDirectory);

            // Note that the platform is determined by the logic in the nanoff tool if a target is specified.
            ExitCodes exitCode = actual.DownloadFirmwareFromRepository(false, SupportedPlatform.esp32, package.Name, package.Version, VerbosityLevel.Detailed)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            Assert.IsTrue(output.Output.Contains($"Added target {package.Name} {package.Version} to the archive"));
            #endregion

            #region Assert it is present and can be found via GetTargetList
            Assert.IsTrue(File.Exists(Path.Combine(archiveDirectory, $"{package.Name}-{package.Version}.zip")));

            // List of all packages
            output.Reset();
            List<CloudSmithPackageDetail> list = actual.GetTargetList(false, null, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                $"{package.Name} {package.Version}",
                string.Join("\n", from p in list select $"{p.Name} {p.Version}"));

            // List of esp32 packages
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.esp32, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                $"{package.Name} {package.Version}",
                string.Join("\n", from p in list select $"{p.Name} {p.Version}"));

            // List of stm32 packages - no match
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.stm32, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                "",
                string.Join("\n", from p in list select $"{p.Name} {p.Version}"));
            #endregion
        }

        [TestMethod]
        [TestCategory("CloudSmith")]
        public void FirmwareArchiveManager_TargetLatestVersion()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            CloudSmithPackageDetail package = GetTargetListHelper.GetTargetList(false, false, SupportedPlatform.esp32)[0];
            output.Reset();
            #endregion

            #region Download the latest package for the target
            var actual = new FirmwareArchiveManager(archiveDirectory);

            ExitCodes exitCode = actual.DownloadFirmwareFromRepository(false, SupportedPlatform.esp32, package.Name, null, VerbosityLevel.Quiet)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ExitCodes.OK, exitCode);
            output.AssertAreEqual("");
            #endregion

            #region Assert the package can be found via GetTargetList
            // List of all packages
            output.Reset();
            List<CloudSmithPackageDetail> list = actual.GetTargetList(false, null, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                $"{package.Name} {package.Version}\n",
                string.Join("\n", from p in list orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n'
            );

            // List of esp32 packages
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.esp32, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                $"{package.Name} {package.Version}\n",
                string.Join("\n", from p in list orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n'
            );

            // List of stm32 packages - no match
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.stm32, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                "",
                string.Join("\n", from p in list select $"{p.Name} {p.Version}"));
            #endregion
        }

        [TestMethod]
        [TestCategory("CloudSmith")]
        public void FirmwareArchiveManager_Platform()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            List<CloudSmithPackageDetail> stablePackages = GetTargetListHelper.GetTargetList(null, false, SupportedPlatform.ti_simplelink);
            output.Reset();
            #endregion

            #region Download all packages
            var actual = new FirmwareArchiveManager(archiveDirectory);

            ExitCodes exitCode = actual.DownloadFirmwareFromRepository(false, SupportedPlatform.ti_simplelink, null, null, VerbosityLevel.Quiet)
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
                string.Join("\n", from p in stablePackages orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n',
                string.Join("\n", from p in list orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n'
            );

            // List of esp32 packages
            output.Reset();
            list = actual.GetTargetList(false, SupportedPlatform.ti_simplelink, VerbosityLevel.Quiet);

            output.AssertAreEqual("");
            Assert.IsNotNull(list);
            Assert.AreEqual(
                string.Join("\n", from p in stablePackages orderby p.Name, p.Version select $"{p.Name} {p.Version}") + '\n',
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
