﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Verify that the options to nanoff are passed correctly to the low-level classes.
    /// This cannot be done for the update of firmware, at least not for ESP32, as that
    /// requires a connection to a real device.
    /// </summary>
    [TestClass]
    [TestCategory("Firmware archive")]
    [DoNotParallelize] // because of static variables in the programs
    public sealed class ToolFirmwareArchiveTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void FirmwareArchive_Platform_UpdateArchive_ListTargets()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            List<CloudSmithPackageDetail> expectedPackages = GetTargetListHelper.GetTargetList(null, false, SupportedPlatform.ti_simplelink);
            #endregion

            #region List empty archive
            int actual = Program.Main(["--listtargets", "--platform", $"{SupportedPlatform.ti_simplelink}", "--fromarchive", "--archivepath", archiveDirectory])
                    .GetAwaiter().GetResult();
            Assert.AreEqual((int)ExitCodes.OK, actual);
            #endregion

            #region Update archive
            output.Reset();
            actual = Program.Main(["--updatearchive", "--platform", $"{SupportedPlatform.ti_simplelink}", "--archivepath", archiveDirectory])
                .GetAwaiter().GetResult();
            Assert.AreEqual((int)ExitCodes.OK, actual);
            #endregion

            #region List filled archive
            output.Reset();
            actual = Program.Main(["--listtargets", "--platform", $"{SupportedPlatform.ti_simplelink}", "--fromarchive", "--archivepath", archiveDirectory])
                    .GetAwaiter().GetResult();
            Assert.AreEqual((int)ExitCodes.OK, actual);

            foreach (var package in expectedPackages)
            {
                Assert.IsTrue(output.Output.Contains($"{package.Name}{Environment.NewLine}    {package.Version}"), $"{package.Name} - {package.Version}");
            }
            #endregion
        }

        [TestMethod]
        public void FirmwareArchive_Target_UpdateArchive_ListTargets()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");
            List<CloudSmithPackageDetail> allPackages = GetTargetListHelper.GetTargetList(null, false, SupportedPlatform.ti_simplelink);
            #endregion

            #region Update archive
            output.Reset();
            int actual = Program.Main(["--updatearchive", "--target", $"{allPackages[0].Name}", "--archivepath", archiveDirectory])
                .GetAwaiter().GetResult();
            Assert.AreEqual((int)ExitCodes.OK, actual);
            #endregion

            #region List filled archive
            output.Reset();
            actual = Program.Main(["--listtargets", "--platform", $"{SupportedPlatform.ti_simplelink}", "--fromarchive", "--archivepath", archiveDirectory])
                    .GetAwaiter().GetResult();
            Assert.AreEqual((int)ExitCodes.OK, actual);

            foreach (var package in allPackages)
            {
                var expectPresent = package == allPackages[0];
                Assert.AreEqual(expectPresent, output.Output.Contains($"{package.Name}{Environment.NewLine}    {package.Version}"), $"{package.Name} - {package.Version}");
            }
            #endregion
        }

        [TestMethod]
        public void FirmwareArchive_ListTargets_InvalidArguments()
        {
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");

            int actual = Program.Main(["--listtargets", "--platform", $"{SupportedPlatform.ti_simplelink}", "--fromarchive", "--verbosity", "diagnostic"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, actual);
            Assert.IsTrue(output.Output.Contains("--archivepath is required when --fromarchive is specified."));
        }

        [TestMethod]
        public void FirmwareArchive_UpdateArchive_InvalidArguments()
        {
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");

            int actual = Program.Main(["--updatearchive", "--platform", $"{SupportedPlatform.ti_simplelink}", "--fromarchive", "--verbosity", "diagnostic"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, actual);
            Assert.IsTrue(output.Output.Contains("Incompatible option --fromarchive combined with --updatearchive."));

            output.Reset();
            actual = Program.Main(["--updatearchive", "--platform", $"{SupportedPlatform.ti_simplelink}", "--verbosity", "diagnostic"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, actual);
            Assert.IsTrue(output.Output.Contains("--archivepath is required when --updatearchive is specified."));

            output.Reset();
            actual = Program.Main(["--updatearchive", "--archivepath", $"{SupportedPlatform.ti_simplelink}", "--verbosity", "diagnostic"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, actual);
            Assert.IsTrue(output.Output.Contains("--platform or --target is required when --updatearchive is specified."));
        }

        [TestMethod]
        public void FirmwareArchive_UpdateFirmware_InvalidArguments()
        {
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string archiveDirectory = Path.Combine(testDirectory, "archive");

            int actual = Program.Main(["--serialport", "COM3", "--target", "SOME_TARGET", "--fromarchive", "--verbosity", "diagnostic"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, actual);
            Assert.IsTrue(output.Output.Contains("--archivepath is required when --fromarchive is specified."));

            output.Reset();
            actual = Program.Main(["--serialport", "COM3", "--target", "SOME_TARGET", "--archivepath", archiveDirectory, "--verbosity", "diagnostic"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, actual);
            Assert.IsTrue(output.Output.Contains("--fromarchive is required when --archivepath is specified."));
        }
    }
}
