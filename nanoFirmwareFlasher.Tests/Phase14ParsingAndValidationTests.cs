// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Phase 14: Tests for verbosity parsing, platform inference from target names,
    /// platform inference from options, early constraint validation, and STM32 CLI error parsing.
    /// No ESP32 code changes involved.
    /// </summary>
    [TestClass]
    public class Phase14ParsingAndValidationTests
    {
        // ======================================================================
        // Options.ParseVerbosity
        // ======================================================================

        #region ParseVerbosity

        [TestMethod]
        [DataRow("q", VerbosityLevel.Quiet)]
        [DataRow("quiet", VerbosityLevel.Quiet)]
        [DataRow("m", VerbosityLevel.Minimal)]
        [DataRow("minimal", VerbosityLevel.Minimal)]
        [DataRow("n", VerbosityLevel.Normal)]
        [DataRow("normal", VerbosityLevel.Normal)]
        [DataRow("d", VerbosityLevel.Detailed)]
        [DataRow("detailed", VerbosityLevel.Detailed)]
        [DataRow("diag", VerbosityLevel.Diagnostic)]
        [DataRow("diagnostic", VerbosityLevel.Diagnostic)]
        public void ParseVerbosity_ValidInputs_ReturnsExpectedLevel(string input, VerbosityLevel expected)
        {
            Assert.AreEqual(expected, Options.ParseVerbosity(input));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ParseVerbosity_InvalidInput_ThrowsArgumentException()
        {
            Options.ParseVerbosity("invalid");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ParseVerbosity_Empty_ThrowsArgumentException()
        {
            Options.ParseVerbosity("");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ParseVerbosity_CaseSensitive_UpperQ_ThrowsArgumentException()
        {
            // The parser is case-sensitive, matching Program.cs behavior
            Options.ParseVerbosity("Q");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ParseVerbosity_Null_ThrowsArgumentException()
        {
            Options.ParseVerbosity(null);
        }

        #endregion

        // ======================================================================
        // SupportedPlatformExtensions.InferFromTargetName
        // ======================================================================

        #region InferFromTargetName

        [TestMethod]
        [DataRow("ESP_WROVER_KIT", SupportedPlatform.esp32)]
        [DataRow("ESP32_PICO", SupportedPlatform.esp32)]
        [DataRow("M5Stack", SupportedPlatform.esp32)]
        [DataRow("M5StickC", SupportedPlatform.esp32)]
        [DataRow("FEATHER_S2", SupportedPlatform.esp32)]
        [DataRow("ESPKALUGA_1", SupportedPlatform.esp32)]
        public void InferFromTargetName_Esp32Targets_ReturnsEsp32(string target, SupportedPlatform expected)
        {
            Assert.AreEqual(expected, SupportedPlatformExtensions.InferFromTargetName(target));
        }

        [TestMethod]
        [DataRow("ST_STM32F769I_DISCOVERY", SupportedPlatform.stm32)]
        [DataRow("ST_NUCLEO144_F746ZG", SupportedPlatform.stm32)]
        [DataRow("MBN_QUAIL", SupportedPlatform.stm32)]
        [DataRow("NETDUINO3_WIFI", SupportedPlatform.stm32)]
        [DataRow("GHI_FEZ_CERB40_NF", SupportedPlatform.stm32)]
        [DataRow("IngenuityMicro_ELECTRON_NF", SupportedPlatform.stm32)]
        [DataRow("WeAct_F411CE", SupportedPlatform.stm32)]
        [DataRow("ORGPAL_PALTHREE", SupportedPlatform.stm32)]
        [DataRow("PybStick2x", SupportedPlatform.stm32)]
        [DataRow("NESHTEC_NESHNODE_V1", SupportedPlatform.stm32)]
        public void InferFromTargetName_Stm32Targets_ReturnsStm32(string target, SupportedPlatform expected)
        {
            Assert.AreEqual(expected, SupportedPlatformExtensions.InferFromTargetName(target));
        }

        [TestMethod]
        [DataRow("TI_CC1352R1_LAUNCHXL", SupportedPlatform.ti_simplelink)]
        [DataRow("TI_CC3220SF_LAUNCHXL", SupportedPlatform.ti_simplelink)]
        public void InferFromTargetName_TiTargets_ReturnsTiSimplelink(string target, SupportedPlatform expected)
        {
            Assert.AreEqual(expected, SupportedPlatformExtensions.InferFromTargetName(target));
        }

        [TestMethod]
        [DataRow("SL_STK3701A", SupportedPlatform.efm32)]
        public void InferFromTargetName_SilabsTargets_ReturnsEfm32(string target, SupportedPlatform expected)
        {
            Assert.AreEqual(expected, SupportedPlatformExtensions.InferFromTargetName(target));
        }

        [TestMethod]
        public void InferFromTargetName_UnknownTarget_ReturnsNull()
        {
            Assert.IsNull(SupportedPlatformExtensions.InferFromTargetName("CUSTOM_BOARD_XYZ"));
        }

        [TestMethod]
        public void InferFromTargetName_Null_ReturnsNull()
        {
            Assert.IsNull(SupportedPlatformExtensions.InferFromTargetName(null));
        }

        [TestMethod]
        public void InferFromTargetName_Empty_ReturnsNull()
        {
            Assert.IsNull(SupportedPlatformExtensions.InferFromTargetName(""));
        }

        #endregion

        // ======================================================================
        // Options.ValidateInterfaceOptions (already partially tested, new edge cases)
        // ======================================================================

        #region ValidateInterfaceOptions_Additional

        [TestMethod]
        public void ValidateInterfaceOptions_AllNativeOptions_ReturnsError()
        {
            var o = new Options
            {
                NativeDfuUpdate = true,
                NativeSwdUpdate = true,
                NativeStLinkUpdate = true
            };

            string error = Options.ValidateInterfaceOptions(o);
            Assert.IsNotNull(error);
            StringAssert.Contains(error, "--nativedfu");
            StringAssert.Contains(error, "--nativeswd");
            StringAssert.Contains(error, "--nativestlink");
        }

        #endregion

        // ======================================================================
        // Options.ValidateEarlyConstraints
        // ======================================================================

        #region ValidateEarlyConstraints

        [TestMethod]
        public void ValidateEarlyConstraints_NoIssues_ReturnsNull()
        {
            var o = new Options();
            Assert.IsNull(Options.ValidateEarlyConstraints(o));
        }

        [TestMethod]
        public void ValidateEarlyConstraints_DeployWithoutImage_ReturnsE9000()
        {
            var o = new Options { Deploy = true };
            var result = Options.ValidateEarlyConstraints(o);
            Assert.IsNotNull(result);
            Assert.AreEqual(ExitCodes.E9000, result.Value.Code);
            StringAssert.Contains(result.Value.Message, "--deploy");
            StringAssert.Contains(result.Value.Message, "--image");
        }

        [TestMethod]
        public void ValidateEarlyConstraints_DeployWithImage_ReturnsNull()
        {
            var o = new Options { Deploy = true, DeploymentImage = "app.bin" };
            Assert.IsNull(Options.ValidateEarlyConstraints(o));
        }

        [TestMethod]
        public void ValidateEarlyConstraints_BinFileWithoutAddress_ReturnsE9000()
        {
            var o = new Options { BinFile = new[] { "firmware.bin" } };
            var result = Options.ValidateEarlyConstraints(o);
            Assert.IsNotNull(result);
            Assert.AreEqual(ExitCodes.E9000, result.Value.Code);
            StringAssert.Contains(result.Value.Message, "--binfile");
            StringAssert.Contains(result.Value.Message, "--address");
        }

        [TestMethod]
        public void ValidateEarlyConstraints_BinFileWithAddress_ReturnsNull()
        {
            var o = new Options
            {
                BinFile = new[] { "firmware.bin" },
                FlashAddress = new[] { "0x08000000" }
            };
            Assert.IsNull(Options.ValidateEarlyConstraints(o));
        }

        [TestMethod]
        public void ValidateEarlyConstraints_UpdateArchiveWithFromArchive_ReturnsE9000()
        {
            var o = new Options { UpdateFwArchive = true, FromFwArchive = true };
            var result = Options.ValidateEarlyConstraints(o);
            Assert.IsNotNull(result);
            Assert.AreEqual(ExitCodes.E9000, result.Value.Code);
            StringAssert.Contains(result.Value.Message, "--fromarchive");
            StringAssert.Contains(result.Value.Message, "--updatearchive");
        }

        #endregion

        // ======================================================================
        // StmDeviceBase.GetErrorMessageFromSTM32CLI
        // ======================================================================

        #region GetErrorMessageFromSTM32CLI

        [TestMethod]
        public void GetErrorMessageFromSTM32CLI_ErrorPattern_ExtractsMessage()
        {
            string cliOutput = "Some output\r\nError: Flash memory programming failed.\r\nMore output";
            string result = StmDeviceBase.GetErrorMessageFromSTM32CLI(cliOutput);
            Assert.IsFalse(string.IsNullOrEmpty(result));
            StringAssert.Contains(result, "Flash memory programming failed");
        }

        [TestMethod]
        public void GetErrorMessageFromSTM32CLI_DevUsbCommErr_ReturnsUsbMessage()
        {
            string cliOutput = "DEV_USB_COMM_ERR";
            string result = StmDeviceBase.GetErrorMessageFromSTM32CLI(cliOutput);
            Assert.AreEqual("USB communication error. Please unplug and plug again the ST device.", result);
        }

        [TestMethod]
        public void GetErrorMessageFromSTM32CLI_NoError_ReturnsEmpty()
        {
            string cliOutput = "No problems here, everything connected fine.";
            string result = StmDeviceBase.GetErrorMessageFromSTM32CLI(cliOutput);
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void GetErrorMessageFromSTM32CLI_EmptyInput_ReturnsEmpty()
        {
            string result = StmDeviceBase.GetErrorMessageFromSTM32CLI("");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void GetErrorMessageFromSTM32CLI_ErrorCaseInsensitive_ExtractsMessage()
        {
            string cliOutput = "error: connection failed.";
            string result = StmDeviceBase.GetErrorMessageFromSTM32CLI(cliOutput);
            Assert.IsFalse(string.IsNullOrEmpty(result));
        }

        [TestMethod]
        public void GetErrorMessageFromSTM32CLI_MultipleErrors_ExtractsFirst()
        {
            string cliOutput = "Error: First problem.\r\nError: Second problem.";
            string result = StmDeviceBase.GetErrorMessageFromSTM32CLI(cliOutput);
            StringAssert.Contains(result, "First problem");
        }

        [TestMethod]
        public void GetErrorMessageFromSTM32CLI_ErrorAndDevUsbCommErr_PrefersRegex()
        {
            // regex match takes priority over DEV_USB_COMM_ERR
            string cliOutput = "Error: Some other error.\r\nDEV_USB_COMM_ERR";
            string result = StmDeviceBase.GetErrorMessageFromSTM32CLI(cliOutput);
            StringAssert.Contains(result, "Some other error");
        }

        #endregion

        // ======================================================================
        // Utilities (existing public methods coverage)
        // ======================================================================

        #region Utilities

        [TestMethod]
        public void Utilities_MakePathAbsolute_RelativePath_BecomesAbsolute()
        {
            string basePath = @"C:\Projects";
            string relative = "firmware.bin";
            string result = Utilities.MakePathAbsolute(basePath, relative);
            Assert.IsTrue(System.IO.Path.IsPathRooted(result));
            StringAssert.Contains(result, "firmware.bin");
        }

        [TestMethod]
        public void Utilities_MakePathAbsolute_AlreadyAbsolute_Unchanged()
        {
            string basePath = @"C:\Projects";
            string absolute = @"C:\Other\firmware.bin";
            string result = Utilities.MakePathAbsolute(basePath, absolute);
            Assert.AreEqual(absolute, result);
        }

        [TestMethod]
        public void Utilities_ExecutingPath_NotNullOrEmpty()
        {
            Assert.IsFalse(string.IsNullOrEmpty(Utilities.ExecutingPath));
        }

        #endregion

        // ======================================================================
        // VerbosityLevel enum coverage
        // ======================================================================

        #region VerbosityLevel_Enum

        [TestMethod]
        public void VerbosityLevel_HasExpectedValues()
        {
            Assert.AreEqual(0, (int)VerbosityLevel.Quiet);
            Assert.AreEqual(1, (int)VerbosityLevel.Minimal);
            Assert.AreEqual(2, (int)VerbosityLevel.Normal);
            Assert.AreEqual(3, (int)VerbosityLevel.Detailed);
            Assert.AreEqual(4, (int)VerbosityLevel.Diagnostic);
        }

        [TestMethod]
        public void VerbosityLevel_ComparisonOrder()
        {
            Assert.IsTrue(VerbosityLevel.Quiet < VerbosityLevel.Minimal);
            Assert.IsTrue(VerbosityLevel.Minimal < VerbosityLevel.Normal);
            Assert.IsTrue(VerbosityLevel.Normal < VerbosityLevel.Detailed);
            Assert.IsTrue(VerbosityLevel.Detailed < VerbosityLevel.Diagnostic);
        }

        #endregion

        // ======================================================================
        // SupportedPlatform enum coverage
        // ======================================================================

        #region SupportedPlatform_Enum

        [TestMethod]
        public void SupportedPlatform_HasExpectedValues()
        {
            Assert.AreEqual(0, (int)SupportedPlatform.esp32);
            Assert.AreEqual(1, (int)SupportedPlatform.stm32);
            Assert.AreEqual(2, (int)SupportedPlatform.ti_simplelink);
        }

        [TestMethod]
        public void SupportedPlatform_Efm32_Exists()
        {
            // efm32 follows ti_simplelink
            SupportedPlatform p = SupportedPlatform.efm32;
            Assert.AreEqual("efm32", p.ToString());
        }

        [TestMethod]
        public void SupportedPlatform_Nxp_Exists()
        {
            SupportedPlatform p = SupportedPlatform.nxp;
            Assert.AreEqual("nxp", p.ToString());
        }

        #endregion

        // ======================================================================
        // ExitCodes referenced in validations
        // ======================================================================

        #region ExitCodes

        [TestMethod]
        public void ExitCodes_E6001_Exists()
        {
            Assert.AreEqual(6001, (int)ExitCodes.E6001);
        }

        [TestMethod]
        public void ExitCodes_E9000_Exists()
        {
            Assert.AreEqual(9000, (int)ExitCodes.E9000);
        }

        [TestMethod]
        public void ExitCodes_OK_IsZero()
        {
            Assert.AreEqual(0, (int)ExitCodes.OK);
        }

        #endregion

        // ======================================================================
        // Integration: full pipeline inference + validation
        // ======================================================================

        #region Integration

        [TestMethod]
        public void Integration_InferPlatformFromTarget_ThenValidateConstraints()
        {
            // Simulates: nanoff --target ST_NUCLEO144_F746ZG --update --jtag
            var o = new Options
            {
                TargetName = "ST_NUCLEO144_F746ZG",
                Update = true,
                JtagUpdate = true
            };

            // Step 1: infer platform
            var platform = SupportedPlatformExtensions.InferFromTargetName(o.TargetName);
            Assert.AreEqual(SupportedPlatform.stm32, platform);

            // Step 2: validate interface (only one selected)
            string ifError = Options.ValidateInterfaceOptions(o);
            Assert.IsNull(ifError);

            // Step 3: validate early constraints
            var earlyError = Options.ValidateEarlyConstraints(o);
            Assert.IsNull(earlyError);
        }

        [TestMethod]
        public void Integration_ParseVerbosity_ThenInferAndValidate()
        {
            // Simulates: nanoff -v d --target SL_STK3701A --update
            var level = Options.ParseVerbosity("d");
            Assert.AreEqual(VerbosityLevel.Detailed, level);

            var platform = SupportedPlatformExtensions.InferFromTargetName("SL_STK3701A");
            Assert.AreEqual(SupportedPlatform.efm32, platform);
        }

        #endregion
    }
}
