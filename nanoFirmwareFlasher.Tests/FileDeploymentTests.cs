// Licensed to the .NET Foundation under one or more agreements.
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
    [TestCategory("File deployment")]
    [DoNotParallelize] // because of static variables in the programs
    public sealed class FileDeploymentTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void FileDeployment_Recognizes_ValidOperation()
        {
            #region Setup
            using var output = new OutputWriterHelper();
            string testDirectory = TestDirectoryHelper.GetTestDirectory(TestContext);
            string deployJsonPath = Path.Combine(testDirectory, "deploy.json");

            File.WriteAllText(deployJsonPath, @"{
    ""serialport"":""COM3"",
    ""files"": [{
        ""DestinationFilePath"": ""I:\\deploy.json"",
        ""SourceFilePath"": """ + deployJsonPath.Replace("\\", "\\\\") + @"""
    }]
}");
            #endregion

            #region Deploy content files
            int actual = Program.Main(["--filedeployment", deployJsonPath])
                    .GetAwaiter().GetResult();

            Assert.IsFalse(output.Output.Contains("No operation was performed with the options supplied."));

            #endregion
        }
    }
}
