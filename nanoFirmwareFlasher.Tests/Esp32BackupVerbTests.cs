// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class Esp32BackupVerbTests
    {
        private static ParserResult<object> Parse(params string[] args) =>
            new Parser(config => config.HelpWriter = null)
                .ParseArguments(VerbTokenizer.Normalize(args), VerbTokenizer.KnownVerbs.Values.ToArray());

        private static Esp32BackupOptionsBase ParseOptions(params string[] args)
        {
            ParserResult<object> result = Parse(args);
            Assert.AreEqual(ParserResultType.Parsed, result.Tag);
            return (Esp32BackupOptionsBase)result.Value;
        }

        [TestMethod]
        [DataRow("backup", typeof(BackupOptions))]
        [DataRow("restore", typeof(RestoreOptions))]
        public void Defaults_MapToEsp32AndAllPartitions(string verb, Type optionsType)
        {
            Esp32BackupOptionsBase options = ParseOptions(verb, "serialport", "COM31", "path", "backup-dir");

            Assert.IsInstanceOfType(options, optionsType);
            Assert.AreEqual(SupportedPlatform.esp32, options.Platform);
            Assert.AreEqual("COM31", options.SerialPort);
            Assert.AreEqual("backup-dir", options.Path);
            Assert.AreEqual(1500000, options.BaudRate);
            Assert.AreEqual(VerbosityLevel.Normal, options.GetVerbosityLevel());
            Assert.IsFalse(options.WholeFlash);
            CollectionAssert.AreEqual(new[] { "all" }, options.GetPartitions().ToArray());
            Assert.IsNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void PartitionSelectors_ArePassedToBackendWithoutFilteringLabels(string verb)
        {
            Esp32BackupOptionsBase options = ParseOptions(
                verb, "platform", "esp32", "serialport", "COM31", "path", "backup-dir",
                "partition", "config", "clr", "deployment", "custom_data", "nvs", "ota_0",
                "baud", "115200", "verbosity", "diag");

            CollectionAssert.AreEqual(
                new[] { "config", "clr", "deployment", "custom_data", "nvs", "ota_0" },
                options.GetPartitions().ToArray());
            Assert.AreEqual(115200, options.BaudRate);
            Assert.AreEqual(VerbosityLevel.Diagnostic, options.GetVerbosityLevel());
            Assert.IsNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void WholeFlash_MapsToNoPartitionSelectors(string verb)
        {
            Esp32BackupOptionsBase options = ParseOptions(
                verb, "serialport", "COM31", "path", "backup-full", "wholeflash");

            Assert.IsTrue(options.WholeFlash);
            Assert.AreEqual(0, options.GetPartitions().Count());
            Assert.IsNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        public void RestoreAll_DoesNotImplicitlyAuthorizeWholeFlash()
        {
            Esp32BackupOptionsBase options = ParseOptions(
                "restore", "serialport", "COM31", "path", "backup-dir", "partition", "all");

            Assert.IsFalse(options.WholeFlash);
            CollectionAssert.AreEqual(new[] { "all" }, options.GetPartitions().ToArray());
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void DashedOptions_AlsoParse(string verb)
        {
            Esp32BackupOptionsBase options = ParseOptions(
                verb, "--platform", "esp32", "--serialport", "COM31", "--path", "backup-dir",
                "--partition", "config", "clr");

            CollectionAssert.AreEqual(new[] { "config", "clr" }, options.GetPartitions().ToArray());
            Assert.IsNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup", "path")]
        [DataRow("restore", "wholeflash")]
        [DataRow("backup", "help")]
        public void LabelsMatchingKeywords_CanUseEqualsSyntax(string verb, string label)
        {
            Esp32BackupOptionsBase options = ParseOptions(
                verb, "serialport", "COM31", "path", "backup-dir", "--partition=" + label);

            CollectionAssert.AreEqual(new[] { label }, options.GetPartitions().ToArray());
            Assert.IsFalse(options.WholeFlash);
            Assert.IsNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup", "serialport")]
        [DataRow("backup", "path")]
        [DataRow("restore", "serialport")]
        [DataRow("restore", "path")]
        public void MissingRequiredOption_IsRejectedByParser(string verb, string missing)
        {
            ParserResult<object> result = missing == "serialport"
                ? Parse(verb, "path", "backup-dir")
                : Parse(verb, "serialport", "COM31");

            Assert.AreEqual(ParserResultType.NotParsed, result.Tag);
        }

        [TestMethod]
        [DataRow("backup", "")]
        [DataRow("backup", " ")]
        [DataRow("restore", "")]
        [DataRow("restore", " ")]
        public void EmptyRequiredValues_AreRejected(string verb, string value)
        {
            Esp32BackupOptionsBase options = ParseOptions(verb, "serialport", "COM31", "path", "backup-dir");
            options.SerialPort = value;
            Assert.IsNotNull(Esp32BackupOptionsBase.Validate(options));
            options.SerialPort = "COM31";
            options.Path = value;
            Assert.IsNotNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void NonEsp32Platform_IsRejected(string verb)
        {
            Esp32BackupOptionsBase options = ParseOptions(
                verb, "platform", "stm32", "serialport", "COM31", "path", "backup-dir");

            Assert.IsNotNull(Esp32BackupOptionsBase.Validate(options));
            Assert.Throws<ArgumentException>(() => new Esp32BackupManager(options, VerbosityLevel.Quiet));
        }

        [TestMethod]
        [DataRow("backup", 0)]
        [DataRow("restore", -1)]
        public void NonPositiveBaud_IsRejected(string verb, int baud)
        {
            Esp32BackupOptionsBase options = ParseOptions(verb, "serialport", "COM31", "path", "backup-dir");
            options.BaudRate = baud;

            Assert.IsNotNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup", "config")]
        [DataRow("backup", "all")]
        [DataRow("restore", "config")]
        [DataRow("restore", "all")]
        public void WholeFlashAndExplicitPartition_AreRejected(string verb, string partition)
        {
            Esp32BackupOptionsBase options = ParseOptions(
                verb, "serialport", "COM31", "path", "backup-dir", "wholeflash", "partition", partition);

            Assert.IsNotNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup", "all")]
        [DataRow("backup", "ALL")]
        [DataRow("restore", "all")]
        [DataRow("restore", "ALL")]
        public void AllAndOtherSelectors_AreRejected(string verb, string all)
        {
            Esp32BackupOptionsBase options = ParseOptions(
                verb, "serialport", "COM31", "path", "backup-dir", "partition", all, "config");

            Assert.IsNotNull(Esp32BackupOptionsBase.Validate(options));
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void BarePartitionWithoutSelectors_IsRejected(string verb)
        {
            Assert.AreEqual(
                ParserResultType.NotParsed,
                Parse(verb, "serialport", "COM31", "path", "backup-dir", "partition").Tag);
            Assert.AreEqual(
                ParserResultType.NotParsed,
                Parse(verb, "serialport", "COM31", "path", "backup-dir", "partition", "wholeflash").Tag);
        }

        [TestMethod]
        [DataRow("backup", "")]
        [DataRow("backup", " ")]
        [DataRow("restore", "")]
        [DataRow("restore", " ")]
        public void EmptyPartitionSelector_IsRejected(string verb, string selector)
        {
            ParserResult<object> result = Parse(
                verb, "serialport", "COM31", "path", "backup-dir", "partition", selector);

            if (result.Tag == ParserResultType.Parsed)
            {
                Assert.IsNotNull(Esp32BackupOptionsBase.Validate((Esp32BackupOptionsBase)result.Value));
            }
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void TargetAndDownloadKeywords_AreNotSupported(string verb)
        {
            foreach (string[] unsupported in new[]
            {
                new[] { "--target", "ESP_WROVER_KIT" },
                new[] { "--preview" },
                new[] { "--fwversion", "1.0.0" },
                new[] { "--fromarchive" },
            })
            {
                string[] args = new[] { verb, "serialport", "COM31", "path", "backup-dir" }
                    .Concat(unsupported).ToArray();
                Assert.AreEqual(ParserResultType.NotParsed, Parse(args).Tag);
            }
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void Telemetry_RecognizesVerbAndRedactsDirectoryAndLabels(string verb)
        {
            string[] args =
            {
                verb, "serialport", "COM31", "path", @"C:\private\backup",
                "partition", "private-label", "platform", "esp32",
            };
            Esp32BackupOptionsBase options = ParseOptions(args);
            using Activity activity = new Activity("test");

            CommandTelemetry.RecordParsed(activity, options, VerbTokenizer.Normalize(args));

            Assert.AreEqual(verb, activity.GetTagItem(CommandTelemetry.VerbTag));
            Assert.AreEqual("set", activity.GetTagItem(CommandTelemetry.OptionTagPrefix + "path"));
            Assert.AreEqual("set", activity.GetTagItem(CommandTelemetry.OptionTagPrefix + "serialport"));
            Assert.AreEqual("set", activity.GetTagItem(CommandTelemetry.OptionTagPrefix + "partition"));
            Assert.AreEqual("esp32", activity.GetTagItem(CommandTelemetry.OptionTagPrefix + "platform"));
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void ProgramHelp_RegistersVerbAndExamples(string verb)
        {
            using OutputWriterHelper output = new OutputWriterHelper();

            int exitCode = Program.Main(new[] { verb, "help" }).GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.OK, exitCode);
            StringAssert.Contains(output.Output, $"nanoff {verb}");
            StringAssert.Contains(output.Output, "wholeflash");
            StringAssert.Contains(output.Output, "partition");
            StringAssert.Contains(output.Output, "manifest.json");
        }

        [TestMethod]
        [DataRow("backup")]
        [DataRow("restore")]
        public void ProgramValidation_RejectsInvalidCombinationBeforeHardware(string verb)
        {
            using OutputWriterHelper output = new OutputWriterHelper();

            int exitCode = Program.Main(new[]
            {
                verb, "serialport", "COM31", "path", "backup-dir",
                "wholeflash", "partition", "config", "suppressnanoffversioncheck",
            }).GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, exitCode);
            StringAssert.Contains(output.Output, "wholeflash and partition");
        }

        [TestMethod]
        public void FlashModifiers_KeepExistingFileMapping()
        {
            ParserResult<object> result = Parse(
                "flash", "platform", "esp32", "serialport", "COM31",
                "backup", "full.bin", "restore", "config.bin");

            Assert.AreEqual(ParserResultType.Parsed, result.Tag);
            FlashOptions options = (FlashOptions)result.Value;
            Options legacy = options.ToLegacyOptions();
            Assert.AreEqual("full.bin", legacy.BackupFile);
            Assert.AreEqual("config.bin", legacy.ConfigBackupPath);
        }

        [TestMethod]
        public void BareFlashBackupAndRestoreModifiers_KeepExistingOptionalValueBehavior()
        {
            ParserResult<object> result = Parse(
                "flash", "platform", "esp32", "serialport", "COM31", "backup", "restore");

            Assert.AreEqual(ParserResultType.Parsed, result.Tag);
            FlashOptions options = (FlashOptions)result.Value;
            StringAssert.StartsWith(options.Backup, "esp32-backup-");
            Assert.IsNull(options.ConfigBackupPath);
        }

        [TestMethod]
        public void BareSerialPortFlash_StillMapsToNanoDeviceWireProtocol()
        {
            ParserResult<object> result = Parse("flash", "serialport", "COM31", "image", "nanoCLR.bin");

            Assert.AreEqual(ParserResultType.Parsed, result.Tag);
            Options legacy = ((FlashOptions)result.Value).ToLegacyOptions();
            Assert.IsTrue(legacy.NanoDevice);
            Assert.IsNull(legacy.Platform);
        }
    }
}
