// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Phase 1 of the verbs + words CLI proposal: tests for the new per-verb option
    /// classes (<see cref="FlashOptions"/>, <see cref="DeployOptions"/>, <see cref="ListOptions"/>,
    /// <see cref="DetailsOptions"/>, <see cref="IdentifyOptions"/>, <see cref="DriversOptions"/>,
    /// <see cref="CacheOptions"/>) and their shared <see cref="VerbOptionsBase"/>.
    ///
    /// These tests parse plain <c>--flag value</c> style arguments (CommandLineParser's native
    /// syntax); the bare-word tokenizer/normalizer that enables <c>nanoff flash platform esp32</c>
    /// is a later phase and is not exercised here.
    /// </summary>
    [TestClass]
    public class VerbOptionsTests
    {
        private static ParserResult<object> Parse(params string[] args)
        {
            return new Parser(config => config.HelpWriter = null)
                .ParseArguments<FlashOptions, DeployOptions, ListOptions, DetailsOptions, IdentifyOptions, DriversOptions, CacheOptions>(args);
        }

        // ======================================================================
        // Verb selection
        // ======================================================================

        #region Verb selection

        [TestMethod]
        public void Parse_FlashVerb_SelectsFlashOptions()
        {
            var result = Parse("flash", "--target", "ESP_WROVER_KIT");

            Assert.IsInstanceOfType(result.Value, typeof(FlashOptions));
            Assert.AreEqual("ESP_WROVER_KIT", ((FlashOptions)result.Value).TargetName);
        }

        [TestMethod]
        public void Parse_DeployVerb_SelectsDeployOptions()
        {
            var result = Parse("deploy", "--image", "app.bin");

            Assert.IsInstanceOfType(result.Value, typeof(DeployOptions));
            Assert.AreEqual("app.bin", ((DeployOptions)result.Value).DeploymentImage);
        }

        [TestMethod]
        public void Parse_ListVerb_SelectsListOptions()
        {
            var result = Parse("list", "--targets");

            Assert.IsInstanceOfType(result.Value, typeof(ListOptions));
            Assert.IsTrue(((ListOptions)result.Value).Targets);
        }

        [TestMethod]
        public void Parse_DetailsVerb_SelectsDetailsOptions()
        {
            var result = Parse("details", "--serialport", "COM11");

            Assert.IsInstanceOfType(result.Value, typeof(DetailsOptions));
            Assert.AreEqual("COM11", ((DetailsOptions)result.Value).SerialPort);
        }

        [TestMethod]
        public void Parse_IdentifyVerb_SelectsIdentifyOptions()
        {
            var result = Parse("identify", "--serialport", "COM11");

            Assert.IsInstanceOfType(result.Value, typeof(IdentifyOptions));
            Assert.AreEqual("COM11", ((IdentifyOptions)result.Value).SerialPort);
        }

        [TestMethod]
        public void Parse_DriversVerb_SelectsDriversOptions()
        {
            var result = Parse("drivers", "--jtag");

            Assert.IsInstanceOfType(result.Value, typeof(DriversOptions));
            Assert.IsTrue(((DriversOptions)result.Value).Jtag);
        }

        [TestMethod]
        public void Parse_CacheVerb_SelectsCacheOptions()
        {
            var result = Parse("cache", "--clear");

            Assert.IsInstanceOfType(result.Value, typeof(CacheOptions));
            Assert.IsTrue(((CacheOptions)result.Value).Clear);
        }

        [TestMethod]
        public void Parse_UnknownVerb_NotParsed()
        {
            var result = Parse("bogus", "--target", "ESP_WROVER_KIT");

            Assert.AreEqual(ParserResultType.NotParsed, result.Tag);
        }

        #endregion

        // ======================================================================
        // VerbOptionsBase (shared options)
        // ======================================================================

        #region VerbOptionsBase

        [TestMethod]
        public void Parse_NoVerbosity_DefaultsToNormal()
        {
            var result = Parse("flash", "--target", "ESP_WROVER_KIT");

            var options = (FlashOptions)result.Value;
            Assert.AreEqual("n", options.Verbosity);
            Assert.AreEqual(VerbosityLevel.Normal, options.GetVerbosityLevel());
        }

        [TestMethod]
        [DataRow("q", VerbosityLevel.Quiet)]
        [DataRow("quiet", VerbosityLevel.Quiet)]
        [DataRow("m", VerbosityLevel.Minimal)]
        [DataRow("d", VerbosityLevel.Detailed)]
        [DataRow("diag", VerbosityLevel.Diagnostic)]
        public void Parse_Verbosity_SetOnAnyVerb(string value, VerbosityLevel expected)
        {
            var result = Parse("cache", "--clear", "--verbosity", value);

            var options = (CacheOptions)result.Value;
            Assert.AreEqual(expected, options.GetVerbosityLevel());
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentException))]
        public void GetVerbosityLevel_InvalidValue_Throws()
        {
            var options = new CacheOptions { Verbosity = "invalid" };
            options.GetVerbosityLevel();
        }

        [TestMethod]
        public void Parse_SuppressVersionCheck_DefaultsFalse()
        {
            var result = Parse("flash", "--target", "ESP_WROVER_KIT");

            Assert.IsFalse(((FlashOptions)result.Value).SuppressNanoFFVersionCheck);
        }

        #endregion

        // ======================================================================
        // FlashOptions
        // ======================================================================

        #region FlashOptions

        [TestMethod]
        public void Parse_FlashInterface_ParsesEnumCaseInsensitive()
        {
            var result = Parse("flash", "--target", "ST_STM32F769I_DISCOVERY", "--interface", "jtag");

            Assert.AreEqual(FlashInterface.Jtag, ((FlashOptions)result.Value).Interface);
        }

        [TestMethod]
        public void Parse_FlashNoInterface_DefaultsToNull()
        {
            var result = Parse("flash", "--target", "ESP_WROVER_KIT");

            Assert.IsNull(((FlashOptions)result.Value).Interface);
        }

        [TestMethod]
        public void Parse_FlashNoBaud_DefaultsTo1500000()
        {
            var result = Parse("flash", "--target", "ESP_WROVER_KIT");

            Assert.AreEqual(1500000, ((FlashOptions)result.Value).BaudRate);
        }

        [TestMethod]
        public void Parse_FlashBaud_SetsCustomValue()
        {
            var result = Parse("flash", "--target", "ESP_WROVER_KIT", "--baud", "921600");

            Assert.AreEqual(921600, ((FlashOptions)result.Value).BaudRate);
        }

        [TestMethod]
        public void Parse_FlashNoFlashModeOrFreq_DefaultsMatchLegacy()
        {
            var options = (FlashOptions)Parse("flash", "--target", "ESP_WROVER_KIT").Value;

            Assert.AreEqual("dio", options.Esp32FlashMode);
            Assert.AreEqual(40, options.Esp32FlashFrequency);
            Assert.IsNull(options.Esp32PartitionTableSize);
        }

        [TestMethod]
        public void Parse_FlashModeFreqAndPartitionTableSize_SetCustomValues()
        {
            var options = (FlashOptions)Parse(
                "flash", "--target", "ESP_WROVER_KIT",
                "--flashmode", "qio",
                "--flashfreq", "80",
                "--partitiontablesize", "4").Value;

            Assert.AreEqual("qio", options.Esp32FlashMode);
            Assert.AreEqual(80, options.Esp32FlashFrequency);
            Assert.AreEqual(PartitionTableSize._4, options.Esp32PartitionTableSize);
        }

        [TestMethod]
        public void FlashOptions_Validate_Valid_ReturnsNull()
        {
            var options = new FlashOptions { TargetName = "ESP_WROVER_KIT" };

            Assert.IsNull(FlashOptions.Validate(options));
        }

        [TestMethod]
        public void FlashOptions_Validate_BinFileWithoutAddress_ReturnsError()
        {
            var options = new FlashOptions { BinFile = new[] { "app.bin" } };

            Assert.IsNotNull(FlashOptions.Validate(options));
        }

        [TestMethod]
        public void FlashOptions_Validate_BinFileWithAddress_ReturnsNull()
        {
            var options = new FlashOptions
            {
                BinFile = new[] { "app.bin" },
                FlashAddress = new[] { "0x08000000" }
            };

            Assert.IsNull(FlashOptions.Validate(options));
        }

        [TestMethod]
        public void FlashOptions_Validate_FromArchiveWithoutPath_ReturnsError()
        {
            var options = new FlashOptions { TargetName = "ESP_WROVER_KIT", FromFwArchive = true };

            Assert.IsNotNull(FlashOptions.Validate(options));
        }

        [TestMethod]
        public void FlashOptions_Validate_FromArchiveWithPath_ReturnsNull()
        {
            var options = new FlashOptions
            {
                TargetName = "ESP_WROVER_KIT",
                FromFwArchive = true,
                FwArchivePath = "./fw-cache"
            };

            Assert.IsNull(FlashOptions.Validate(options));
        }

        #endregion

        // ======================================================================
        // DeployOptions
        // ======================================================================

        #region DeployOptions

        [TestMethod]
        public void DeployOptions_Validate_NoTarget_ReturnsError()
        {
            Assert.IsNotNull(DeployOptions.Validate(new DeployOptions()));
        }

        [TestMethod]
        public void DeployOptions_Validate_ImageOnly_ReturnsNull()
        {
            Assert.IsNull(DeployOptions.Validate(new DeployOptions { DeploymentImage = "app.bin" }));
        }

        [TestMethod]
        public void DeployOptions_Validate_ImageAndFile_ReturnsError()
        {
            var options = new DeployOptions { DeploymentImage = "app.bin", FileDeployment = "deploy.json" };

            Assert.IsNotNull(DeployOptions.Validate(options));
        }

        #endregion

        // ======================================================================
        // ListOptions
        // ======================================================================

        #region ListOptions

        [TestMethod]
        public void ListOptions_Validate_NoFlag_ReturnsError()
        {
            Assert.IsNotNull(ListOptions.Validate(new ListOptions()));
        }

        [TestMethod]
        public void ListOptions_Validate_SingleFlag_ReturnsNull()
        {
            Assert.IsNull(ListOptions.Validate(new ListOptions { Jtag = true }));
        }

        [TestMethod]
        public void ListOptions_Validate_MultipleFlags_ReturnsError()
        {
            Assert.IsNotNull(ListOptions.Validate(new ListOptions { Jtag = true, Dfu = true }));
        }

        #endregion

        // ======================================================================
        // DriversOptions
        // ======================================================================

        #region DriversOptions

        [TestMethod]
        public void DriversOptions_Validate_NoFlag_ReturnsError()
        {
            Assert.IsNotNull(DriversOptions.Validate(new DriversOptions()));
        }

        [TestMethod]
        public void DriversOptions_Validate_SingleFlag_ReturnsNull()
        {
            Assert.IsNull(DriversOptions.Validate(new DriversOptions { Dfu = true }));
        }

        [TestMethod]
        public void DriversOptions_Validate_MultipleFlags_ReturnsError()
        {
            Assert.IsNotNull(DriversOptions.Validate(new DriversOptions { Dfu = true, Xds = true }));
        }

        #endregion

        // ======================================================================
        // CacheOptions
        // ======================================================================

        #region CacheOptions

        [TestMethod]
        public void CacheOptions_Validate_NoFlag_ReturnsError()
        {
            Assert.IsNotNull(CacheOptions.Validate(new CacheOptions()));
        }

        [TestMethod]
        public void CacheOptions_Validate_ClearAndDownload_ReturnsError()
        {
            Assert.IsNotNull(CacheOptions.Validate(new CacheOptions { Clear = true, Download = true }));
        }

        [TestMethod]
        public void CacheOptions_Validate_Clear_ReturnsNull()
        {
            Assert.IsNull(CacheOptions.Validate(new CacheOptions { Clear = true }));
        }

        [TestMethod]
        public void CacheOptions_Validate_DownloadWithoutArchivePath_ReturnsError()
        {
            var options = new CacheOptions { Download = true, Platform = SupportedPlatform.esp32 };

            Assert.IsNotNull(CacheOptions.Validate(options));
        }

        [TestMethod]
        public void CacheOptions_Validate_DownloadWithoutPlatformOrTarget_ReturnsError()
        {
            var options = new CacheOptions { Download = true, FwArchivePath = "./fw-cache" };

            Assert.IsNotNull(CacheOptions.Validate(options));
        }

        [TestMethod]
        public void CacheOptions_Validate_DownloadComplete_ReturnsNull()
        {
            var options = new CacheOptions
            {
                Download = true,
                FwArchivePath = "./fw-cache",
                Platform = SupportedPlatform.esp32
            };

            Assert.IsNull(CacheOptions.Validate(options));
        }

        #endregion
    }
}
