// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Phase 2 of the verbs + words CLI proposal: tests for <see cref="VerbTokenizer"/>,
    /// which rewrites bare-word arguments (e.g. <c>flash target ESP_WROVER_KIT masserase</c>)
    /// into the <c>--keyword value</c> form CommandLineParser understands.
    /// </summary>
    [TestClass]
    public class VerbTokenizerTests
    {
        // ======================================================================
        // Normalize: basic behavior
        // ======================================================================

        #region Normalize basics

        [TestMethod]
        public void Normalize_Null_ReturnsEmptyArray()
        {
            CollectionAssert.AreEqual(System.Array.Empty<string>(), VerbTokenizer.Normalize(null));
        }

        [TestMethod]
        public void Normalize_Empty_ReturnsEmptyArray()
        {
            CollectionAssert.AreEqual(System.Array.Empty<string>(), VerbTokenizer.Normalize(new string[0]));
        }

        [TestMethod]
        public void Normalize_UnknownVerb_PassesThroughUnchanged()
        {
            var input = new[] { "bogus", "target", "ESP_WROVER_KIT" };

            CollectionAssert.AreEqual(input, VerbTokenizer.Normalize(input));
        }

        [TestMethod]
        public void Normalize_VerbWord_NeverPrefixed()
        {
            var result = VerbTokenizer.Normalize(new[] { "flash", "target", "ESP_WROVER_KIT" });

            Assert.AreEqual("flash", result[0]);
        }

        #endregion

        // ======================================================================
        // Normalize: value-keywords vs. flag-keywords
        // ======================================================================

        #region Value and flag keywords

        [TestMethod]
        public void Normalize_ValueKeyword_PrefixedAndValuePassedThrough()
        {
            var result = VerbTokenizer.Normalize(new[] { "flash", "target", "ESP_WROVER_KIT" });

            CollectionAssert.AreEqual(new[] { "flash", "--target", "ESP_WROVER_KIT" }, result);
        }

        [TestMethod]
        public void Normalize_FlagKeyword_PrefixedWithNoValueConsumed()
        {
            var result = VerbTokenizer.Normalize(new[] { "flash", "target", "ESP_WROVER_KIT", "masserase" });

            CollectionAssert.AreEqual(new[] { "flash", "--target", "ESP_WROVER_KIT", "--masserase" }, result);
        }

        [TestMethod]
        public void Normalize_FullSentence_MatchesDocExample()
        {
            var result = VerbTokenizer.Normalize(new[]
            {
                "flash", "platform", "esp32", "serialport", "COM7", "masserase", "reset"
            });

            CollectionAssert.AreEqual(
                new[] { "flash", "--platform", "esp32", "--serialport", "COM7", "--masserase", "--reset" },
                result);
        }

        [TestMethod]
        public void Normalize_UnrecognizedWord_TreatedAsValue()
        {
            // "jtag" is not a keyword in the flash verb's vocabulary (it's an enum value for "interface")
            var result = VerbTokenizer.Normalize(new[] { "flash", "target", "ST_STM32F769I_DISCOVERY", "interface", "jtag" });

            CollectionAssert.AreEqual(
                new[] { "flash", "--target", "ST_STM32F769I_DISCOVERY", "--interface", "jtag" },
                result);
        }

        [TestMethod]
        public void Normalize_MultiValueKeyword_ValuesPassThroughUntouched()
        {
            var result = VerbTokenizer.Normalize(new[] { "flash", "binfile", "app.bin", "address", "0x08000000" });

            CollectionAssert.AreEqual(
                new[] { "flash", "--binfile", "app.bin", "--address", "0x08000000" },
                result);
        }

        #endregion

        // ======================================================================
        // Normalize: dashed tokens and help/version
        // ======================================================================

        #region Dashed tokens and help/version

        [TestMethod]
        public void Normalize_AlreadyDashedToken_PassesThroughUnchanged()
        {
            var result = VerbTokenizer.Normalize(new[] { "flash", "--target", "ESP_WROVER_KIT" });

            CollectionAssert.AreEqual(new[] { "flash", "--target", "ESP_WROVER_KIT" }, result);
        }

        [TestMethod]
        public void Normalize_MixedDashedAndBareWords_BothWork()
        {
            var result = VerbTokenizer.Normalize(new[] { "flash", "--target", "ESP_WROVER_KIT", "masserase" });

            CollectionAssert.AreEqual(new[] { "flash", "--target", "ESP_WROVER_KIT", "--masserase" }, result);
        }

        [TestMethod]
        public void Normalize_BareHelp_RewrittenToDashedHelp()
        {
            CollectionAssert.AreEqual(new[] { "--help" }, VerbTokenizer.Normalize(new[] { "help" }));
        }

        [TestMethod]
        public void Normalize_BareVersion_RewrittenToDashedVersion()
        {
            CollectionAssert.AreEqual(new[] { "--version" }, VerbTokenizer.Normalize(new[] { "version" }));
        }

        [TestMethod]
        public void Normalize_DashedHelp_PassesThroughUnchanged()
        {
            CollectionAssert.AreEqual(new[] { "--help" }, VerbTokenizer.Normalize(new[] { "--help" }));
        }

        [TestMethod]
        public void Normalize_HelpAfterVerb_RewrittenToDashedHelp()
        {
            CollectionAssert.AreEqual(new[] { "flash", "--help" }, VerbTokenizer.Normalize(new[] { "flash", "help" }));
        }

        #endregion

        // ======================================================================
        // GetKeywordMap
        // ======================================================================

        #region GetKeywordMap

        [TestMethod]
        public void GetKeywordMap_Flash_TargetIsValueKeyword()
        {
            var map = VerbTokenizer.GetKeywordMap(typeof(FlashOptions));

            Assert.IsTrue(map.ContainsKey("target"));
            Assert.IsFalse(map["target"]);
        }

        [TestMethod]
        public void GetKeywordMap_Flash_MassEraseIsFlagKeyword()
        {
            var map = VerbTokenizer.GetKeywordMap(typeof(FlashOptions));

            Assert.IsTrue(map.ContainsKey("masserase"));
            Assert.IsTrue(map["masserase"]);
        }

        [TestMethod]
        public void GetKeywordMap_Flash_IncludesInheritedBaseOptions()
        {
            var map = VerbTokenizer.GetKeywordMap(typeof(FlashOptions));

            Assert.IsTrue(map.ContainsKey("verbosity"));
            Assert.IsTrue(map.ContainsKey("suppressnanoffversioncheck"));
        }

        [TestMethod]
        public void GetKeywordMap_List_ContainsAllMutuallyExclusiveFlags()
        {
            var map = VerbTokenizer.GetKeywordMap(typeof(ListOptions));

            foreach (var keyword in new[] { "targets", "devices", "ports", "dfu", "jtag", "jlink", "nativeswd" })
            {
                Assert.IsTrue(map.ContainsKey(keyword), $"Expected keyword '{keyword}' to be present.");
                Assert.IsTrue(map[keyword], $"Expected keyword '{keyword}' to be a flag.");
            }
        }

        #endregion

        // ======================================================================
        // End-to-end: normalized tokens parse correctly with CommandLineParser
        // ======================================================================

        #region End-to-end parsing

        private static ParserResult<object> ParseNormalized(params string[] args)
        {
            string[] normalized = VerbTokenizer.Normalize(args);

            return new Parser(config => config.HelpWriter = null)
                .ParseArguments<FlashOptions, DeployOptions, ListOptions, DetailsOptions, IdentifyOptions, DriversOptions, CacheOptions>(normalized);
        }

        [TestMethod]
        public void EndToEnd_FlashWithBareWords_ParsesCorrectly()
        {
            var result = ParseNormalized("flash", "target", "ST_STM32F769I_DISCOVERY", "interface", "jtag", "masserase");

            var options = (FlashOptions)result.Value;
            Assert.AreEqual("ST_STM32F769I_DISCOVERY", options.TargetName);
            Assert.AreEqual(FlashInterface.Jtag, options.Interface);
            Assert.IsTrue(options.MassErase);
        }

        [TestMethod]
        public void EndToEnd_FlashBaudWithBareWords_ParsesCorrectly()
        {
            var result = ParseNormalized("flash", "target", "ESP_WROVER_KIT", "serialport", "COM7", "baud", "921600");

            var options = (FlashOptions)result.Value;
            Assert.AreEqual("COM7", options.SerialPort);
            Assert.AreEqual(921600, options.BaudRate);
        }

        [TestMethod]
        public void EndToEnd_ListWithBareWords_ParsesCorrectly()
        {
            var result = ParseNormalized("list", "jtag", "platform", "stm32");

            var options = (ListOptions)result.Value;
            Assert.IsTrue(options.Jtag);
            Assert.AreEqual(SupportedPlatform.stm32, options.Platform);
        }

        [TestMethod]
        public void EndToEnd_CacheDownloadWithBareWords_ParsesCorrectly()
        {
            var result = ParseNormalized("cache", "download", "archivepath", "./fw-cache", "platform", "esp32");

            var options = (CacheOptions)result.Value;
            Assert.IsTrue(options.Download);
            Assert.AreEqual("./fw-cache", options.FwArchivePath);
            Assert.AreEqual(SupportedPlatform.esp32, options.Platform);
        }

        [TestMethod]
        public void EndToEnd_FlashBackupFlash_ParsesPathAsValue()
        {
            var result = ParseNormalized("flash", "target", "ESP_WROVER_KIT", "backupflash", "./backups/esp32.bin", "masserase");

            var options = (FlashOptions)result.Value;
            Assert.AreEqual("./backups/esp32.bin", options.BackupFlash);
            Assert.IsTrue(options.MassErase);
        }

        [TestMethod]
        public void EndToEnd_FlashNoBackupFlashKeyword_BackupFlashStaysNull()
        {
            var result = ParseNormalized("flash", "target", "ESP_WROVER_KIT");

            var options = (FlashOptions)result.Value;
            Assert.IsNull(options.BackupFlash);
        }

        #endregion
    }
}
