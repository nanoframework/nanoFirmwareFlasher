// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.FileDeployment;

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

        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

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

        #region FileDeploymentConfiguration deserialization

        [TestMethod]
        public void FileDeploymentConfig_Deserialize_BasicJson()
        {
            string json = @"{
                ""serialport"": ""COM5"",
                ""files"": [
                    { ""DestinationFilePath"": ""I:\\app.pe"", ""SourceFilePath"": ""C:\\build\\app.pe"" }
                ]
            }";

            var config = JsonSerializer.Deserialize<FileDeploymentConfiguration>(json, s_jsonOptions);

            Assert.IsNotNull(config);
            Assert.AreEqual("COM5", config.SerialPort);
            Assert.IsNotNull(config.Files);
            Assert.AreEqual(1, config.Files.Count);
            Assert.AreEqual("I:\\app.pe", config.Files[0].DestinationFilePath);
            Assert.AreEqual("C:\\build\\app.pe", config.Files[0].SourceFilePath);
        }

        [TestMethod]
        public void FileDeploymentConfig_Deserialize_MultipleFiles()
        {
            string json = @"{
                ""serialport"": ""COM3"",
                ""files"": [
                    { ""DestinationFilePath"": ""I:\\file1.txt"", ""SourceFilePath"": ""C:\\src\\file1.txt"" },
                    { ""DestinationFilePath"": ""I:\\file2.dat"", ""SourceFilePath"": ""C:\\src\\file2.dat"" },
                    { ""DestinationFilePath"": ""I:\\config.json"", ""SourceFilePath"": ""C:\\src\\config.json"" }
                ]
            }";

            var config = JsonSerializer.Deserialize<FileDeploymentConfiguration>(json, s_jsonOptions);

            Assert.AreEqual(3, config.Files.Count);
        }

        [TestMethod]
        public void FileDeploymentConfig_Deserialize_DeleteFile_NullSource()
        {
            string json = @"{
                ""serialport"": ""COM3"",
                ""files"": [
                    { ""DestinationFilePath"": ""I:\\obsolete.pe"", ""SourceFilePath"": null }
                ]
            }";

            var config = JsonSerializer.Deserialize<FileDeploymentConfiguration>(json, s_jsonOptions);

            Assert.IsNotNull(config.Files);
            Assert.AreEqual(1, config.Files.Count);
            Assert.IsNull(config.Files[0].SourceFilePath);
            Assert.AreEqual("I:\\obsolete.pe", config.Files[0].DestinationFilePath);
        }

        [TestMethod]
        public void FileDeploymentConfig_Deserialize_EmptySource_IsDeleteOperation()
        {
            string json = @"{
                ""serialport"": ""COM3"",
                ""files"": [
                    { ""DestinationFilePath"": ""I:\\delete-me.pe"", ""SourceFilePath"": """" }
                ]
            }";

            var config = JsonSerializer.Deserialize<FileDeploymentConfiguration>(json, s_jsonOptions);

            Assert.AreEqual(string.Empty, config.Files[0].SourceFilePath);
        }

        [TestMethod]
        public void FileDeploymentConfig_Deserialize_NoSerialPort()
        {
            string json = @"{
                ""files"": [
                    { ""DestinationFilePath"": ""I:\\app.pe"", ""SourceFilePath"": ""C:\\app.pe"" }
                ]
            }";

            var config = JsonSerializer.Deserialize<FileDeploymentConfiguration>(json, s_jsonOptions);

            Assert.IsNull(config.SerialPort);
        }

        [TestMethod]
        public void FileDeploymentConfig_Deserialize_EmptyFiles()
        {
            string json = @"{ ""serialport"": ""COM3"", ""files"": [] }";

            var config = JsonSerializer.Deserialize<FileDeploymentConfiguration>(json, s_jsonOptions);

            Assert.IsNotNull(config.Files);
            Assert.AreEqual(0, config.Files.Count);
        }

        [TestMethod]
        public void FileDeploymentConfig_CaseInsensitive()
        {
            string json = @"{ ""SERIALPORT"": ""COM8"", ""FILES"": [] }";

            var config = JsonSerializer.Deserialize<FileDeploymentConfiguration>(json, s_jsonOptions);

            Assert.AreEqual("COM8", config.SerialPort);
        }

        #endregion

        #region DeploymentFile model

        [TestMethod]
        public void DeploymentFile_Properties_SetCorrectly()
        {
            var file = new DeploymentFile
            {
                DestinationFilePath = "I:\\target.bin",
                SourceFilePath = "C:\\local\\source.bin"
            };

            Assert.AreEqual("I:\\target.bin", file.DestinationFilePath);
            Assert.AreEqual("C:\\local\\source.bin", file.SourceFilePath);
        }

        [TestMethod]
        public void DeploymentFile_DefaultValues_AreNull()
        {
            var file = new DeploymentFile();
            Assert.IsNull(file.DestinationFilePath);
            Assert.IsNull(file.SourceFilePath);
        }

        #endregion

        #region FileDeploymentManager constructor validation

        [TestMethod]
        public void FileDeploymentManager_Constructor_ParsesJsonFile()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string jsonPath = Path.Combine(testDir, "test-config.json");

            File.WriteAllText(jsonPath, @"{
                ""serialport"": ""COM7"",
                ""files"": [
                    { ""DestinationFilePath"": ""I:\\app.pe"", ""SourceFilePath"": """ + jsonPath.Replace("\\", "\\\\") + @""" }
                ]
            }");

            // Constructor should not throw — it just parses the JSON
            var manager = new FileDeploymentManager(jsonPath, "COM3", VerbosityLevel.Quiet);
            Assert.IsNotNull(manager);
        }

        [TestMethod]
        [ExpectedException(typeof(DirectoryNotFoundException))]
        public void FileDeploymentManager_Constructor_ThrowsForMissingFile()
        {
            new FileDeploymentManager(@"C:\nonexistent\deploy.json", "COM3", VerbosityLevel.Quiet);
        }

        [TestMethod]
        [ExpectedException(typeof(JsonException))]
        public void FileDeploymentManager_Constructor_ThrowsForInvalidJson()
        {
            string testDir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string jsonPath = Path.Combine(testDir, "bad.json");

            File.WriteAllText(jsonPath, "not valid json {{{");

            new FileDeploymentManager(jsonPath, "COM3", VerbosityLevel.Quiet);
        }

        #endregion
    }
}
