// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for the MCUboot/SMP surface of the verbs + words CLI: the <c>mcuboot</c> keywords on
    /// <see cref="FlashOptions"/>, <see cref="DeployOptions"/> and <see cref="ListOptions"/>, the
    /// <see cref="KeysOptions"/> verb, their mapping to <see cref="Options"/> and the dispatch to
    /// <see cref="McubootManager"/>.
    /// </summary>
    [TestClass]
    public class McubootVerbTests
    {
        /// <summary>
        /// Tokenizes and parses a bare-word command line, the same way <c>Program</c> does.
        /// </summary>
        private static object Parse(params string[] args)
        {
            ParserResult<object> result = new Parser(config => config.HelpWriter = null)
                .ParseArguments<FlashOptions, DeployOptions, ListOptions, DetailsOptions, IdentifyOptions, DriversOptions, CacheOptions, KeysOptions>(VerbTokenizer.Normalize(args));

            Assert.IsInstanceOfType(result, typeof(Parsed<object>), "command line should parse");

            return result.Value;
        }

        // ======================================================================
        // Tokenizer
        // ======================================================================

        #region Tokenizer

        [TestMethod]
        public void Normalize_KeysIsAKnownVerb()
        {
            Assert.IsTrue(VerbTokenizer.KnownVerbs.ContainsKey("keys"));
            Assert.AreEqual(typeof(KeysOptions), VerbTokenizer.KnownVerbs["keys"]);
        }

        [TestMethod]
        public void Normalize_FlashMcuboot_FlagsAndValues()
        {
            string[] result = VerbTokenizer.Normalize(new[]
            {
                "flash", "serialport", "COM31", "image", "clr.bin", "mcuboot", "secondaryslot", "signkey", "key.pem", "slotsize", "0xE8000"
            });

            CollectionAssert.AreEqual(
                new[] { "flash", "--serialport", "COM31", "--image", "clr.bin", "--mcuboot", "--secondaryslot", "--signkey", "key.pem", "--slotsize", "0xE8000" },
                result);
        }

        [TestMethod]
        public void Normalize_ListImagesMcuboot()
        {
            string[] result = VerbTokenizer.Normalize(new[] { "list", "images", "mcuboot", "serialport", "COM31" });

            CollectionAssert.AreEqual(new[] { "list", "--images", "--mcuboot", "--serialport", "COM31" }, result);
        }

        [TestMethod]
        public void Normalize_KeysGetPub()
        {
            string[] result = VerbTokenizer.Normalize(new[] { "keys", "getpub", "pub.c", "signkey", "key.pem" });

            CollectionAssert.AreEqual(new[] { "keys", "--getpub", "pub.c", "--signkey", "key.pem" }, result);
        }

        #endregion

        // ======================================================================
        // flash
        // ======================================================================

        #region flash

        [TestMethod]
        public void Flash_Mcuboot_Valid()
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "image", "clr.bin", "mcuboot");

            Assert.IsTrue(o.Mcuboot);
            Assert.IsNull(FlashOptions.Validate(o));
        }

        [TestMethod]
        public void Flash_McubootWithTargetAndNoImage_Valid()
        {
            var o = (FlashOptions)Parse("flash", "target", "ESP32_S3", "serialport", "COM31", "mcuboot", "signkey", "key.pem");

            Assert.IsNull(FlashOptions.Validate(o));
        }

        [TestMethod]
        public void Flash_McubootWithPlatformAndImage_SkipsAddressRule()
        {
            var o = (FlashOptions)Parse("flash", "platform", "esp32", "serialport", "COM31", "image", "clr.bin", "mcuboot");

            Assert.IsNull(FlashOptions.Validate(o));
        }

        [TestMethod]
        public void Flash_McubootWithoutSerialPort_Invalid()
        {
            var o = (FlashOptions)Parse("flash", "image", "clr.bin", "mcuboot");

            StringAssert.Contains(FlashOptions.Validate(o), "serialport");
        }

        [TestMethod]
        public void Flash_McubootWithoutImageOrTarget_Invalid()
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "mcuboot");

            StringAssert.Contains(FlashOptions.Validate(o), "requires image");
        }

        [TestMethod]
        public void Flash_McubootWithTwoImages_Invalid()
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "image", "a.bin", "b.bin", "mcuboot");

            Assert.IsNotNull(FlashOptions.Validate(o));
        }

        [TestMethod]
        [DataRow("jtag")]
        [DataRow("dfu")]
        [DataRow("masserase")]
        [DataRow("hex")]
        public void Flash_McubootWithIncompatibleFlag_Invalid(string flag)
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "image", "clr.bin", "mcuboot", flag);

            StringAssert.Contains(FlashOptions.Validate(o), "can't be combined");
        }

        [TestMethod]
        public void Flash_McubootWithAddress_Invalid()
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "image", "clr.bin", "mcuboot", "address", "0x08000000");

            StringAssert.Contains(FlashOptions.Validate(o), "can't be combined");
        }

        [TestMethod]
        [DataRow("signkey", "key.pem")]
        [DataRow("slotsize", "0x100000")]
        [DataRow("headersize", "512")]
        [DataRow("writealign", "4")]
        public void Flash_McubootKeywordWithoutMcuboot_Invalid(string keyword, string value)
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "image", "clr.bin", keyword, value);

            StringAssert.Contains(FlashOptions.Validate(o), "only be used with mcuboot");
        }

        [TestMethod]
        public void Flash_SecondarySlotWithoutMcuboot_Invalid()
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "image", "clr.bin", "secondaryslot");

            StringAssert.Contains(FlashOptions.Validate(o), "only be used with mcuboot");
        }

        [TestMethod]
        public void Flash_SlotSizeWithoutSignKey_Invalid()
        {
            var o = (FlashOptions)Parse("flash", "serialport", "COM31", "image", "clr.bin", "mcuboot", "slotsize", "0x100000");

            StringAssert.Contains(FlashOptions.Validate(o), "signkey");
        }

        [TestMethod]
        [DataRow("0xZZ")]
        [DataRow("-4")]
        [DataRow("0")]
        [DataRow("lots")]
        public void Flash_InvalidSlotSize_Invalid(string value)
        {
            var o = new FlashOptions { SerialPort = "COM31", Image = new[] { "clr.bin" }, Mcuboot = true, SignKey = "key.pem", SlotSize = value };

            StringAssert.Contains(FlashOptions.Validate(o), "slotsize");
        }

        [TestMethod]
        public void Flash_Mcuboot_MapsToClrFileImage0()
        {
            var o = (FlashOptions)Parse(
                "flash", "serialport", "COM31", "image", "clr.bin", "mcuboot", "secondaryslot",
                "signkey", "key.pem", "slotsize", "0xE8000", "headersize", "512", "writealign", "8");

            Options legacy = o.ToLegacyOptions();

            Assert.IsTrue(legacy.McubootTarget);
            Assert.AreEqual("clr.bin", legacy.ClrFile);
            Assert.IsNull(legacy.DeploymentImage);
            Assert.AreEqual(0, legacy.BinFile.Count);
            Assert.AreEqual(0, legacy.HexFile.Count);
            Assert.IsFalse(legacy.NanoDevice);
            Assert.IsTrue(legacy.SecondarySlot);
            Assert.AreEqual("key.pem", legacy.SigningKeyPath);
            Assert.AreEqual(0xE8000, legacy.McubootSlotSize);
            Assert.AreEqual(512, legacy.McubootHeaderSize);
            Assert.AreEqual(8, legacy.McubootWriteAlignment);
        }

        [TestMethod]
        public void Flash_McubootDefaults_LeaveSigningParametersUnset()
        {
            Options legacy = new FlashOptions { SerialPort = "COM31", Image = new[] { "clr.bin" }, Mcuboot = true }.ToLegacyOptions();

            Assert.IsNull(legacy.SigningKeyPath);
            Assert.IsNull(legacy.McubootSlotSize);
            Assert.IsNull(legacy.McubootHeaderSize);
            Assert.IsNull(legacy.McubootWriteAlignment);
            Assert.IsFalse(legacy.SecondarySlot);
        }

        [TestMethod]
        public void Flash_WithoutMcuboot_LeavesMcubootTargetFalse()
        {
            Options legacy = new FlashOptions { SerialPort = "COM31", Image = new[] { "clr.bin" } }.ToLegacyOptions();

            Assert.IsFalse(legacy.McubootTarget);
        }

        #endregion

        // ======================================================================
        // deploy
        // ======================================================================

        #region deploy

        [TestMethod]
        public void Deploy_Mcuboot_Valid()
        {
            var o = (DeployOptions)Parse("deploy", "serialport", "COM31", "image", "app.bin", "mcuboot");

            Assert.IsNull(DeployOptions.Validate(o));
        }

        [TestMethod]
        public void Deploy_McubootWithoutImage_Invalid()
        {
            var o = (DeployOptions)Parse("deploy", "serialport", "COM31", "mcuboot");

            StringAssert.Contains(DeployOptions.Validate(o), "requires image");
        }

        [TestMethod]
        public void Deploy_McubootWithoutSerialPort_Invalid()
        {
            var o = (DeployOptions)Parse("deploy", "image", "app.bin", "mcuboot");

            StringAssert.Contains(DeployOptions.Validate(o), "serialport");
        }

        [TestMethod]
        public void Deploy_McubootWithAddress_Invalid()
        {
            var o = (DeployOptions)Parse("deploy", "serialport", "COM31", "image", "app.bin", "mcuboot", "address", "0x08040000");

            StringAssert.Contains(DeployOptions.Validate(o), "can't be combined");
        }

        [TestMethod]
        public void Deploy_SignKeyWithoutMcuboot_Invalid()
        {
            var o = (DeployOptions)Parse("deploy", "serialport", "COM31", "image", "app.bin", "signkey", "key.pem");

            StringAssert.Contains(DeployOptions.Validate(o), "only be used with mcuboot");
        }

        [TestMethod]
        public void Deploy_Mcuboot_MapsToDeploymentImage1_AndSkipsWireProtocol()
        {
            var o = (DeployOptions)Parse("deploy", "serialport", "COM31", "image", "app.bin", "mcuboot", "signkey", "key.pem");

            Options legacy = o.ToLegacyOptions();

            Assert.IsTrue(legacy.McubootTarget);
            Assert.AreEqual("app.bin", legacy.DeploymentImage);
            Assert.IsNull(legacy.ClrFile);
            Assert.IsFalse(legacy.Deploy);
            Assert.IsFalse(legacy.NanoDevice);
            Assert.IsFalse(legacy.Update);
            Assert.AreEqual("key.pem", legacy.SigningKeyPath);
        }

        #endregion

        // ======================================================================
        // list images
        // ======================================================================

        #region list images

        [TestMethod]
        public void ListImages_Mcuboot_Valid()
        {
            var o = (ListOptions)Parse("list", "images", "mcuboot", "serialport", "COM31");

            Assert.IsNull(ListOptions.Validate(o));
        }

        [TestMethod]
        public void ListImages_WithoutMcuboot_Invalid()
        {
            var o = (ListOptions)Parse("list", "images", "serialport", "COM31");

            StringAssert.Contains(ListOptions.Validate(o), "requires mcuboot");
        }

        [TestMethod]
        public void ListImages_WithoutSerialPort_Invalid()
        {
            var o = (ListOptions)Parse("list", "images", "mcuboot");

            StringAssert.Contains(ListOptions.Validate(o), "serialport");
        }

        [TestMethod]
        public void ListMcuboot_WithoutImages_Invalid()
        {
            var o = (ListOptions)Parse("list", "devices", "mcuboot");

            StringAssert.Contains(ListOptions.Validate(o), "only be used with images");
        }

        [TestMethod]
        public void ListImages_WithAnotherListKeyword_Invalid()
        {
            var o = (ListOptions)Parse("list", "images", "devices", "mcuboot", "serialport", "COM31");

            StringAssert.Contains(ListOptions.Validate(o), "Only one of");
        }

        [TestMethod]
        public void ListDevices_WithSerialPort_Valid()
        {
            var o = (ListOptions)Parse("list", "devices", "serialport", "COM31");

            Assert.IsNull(ListOptions.Validate(o));
        }

        [TestMethod]
        public void ListPorts_WithSerialPort_Invalid()
        {
            var o = (ListOptions)Parse("list", "ports", "serialport", "COM31");

            StringAssert.Contains(ListOptions.Validate(o), "serialport can only be used");
        }

        [TestMethod]
        public void ListImages_MapsToListMcuImages()
        {
            Options legacy = ((ListOptions)Parse("list", "images", "mcuboot", "serialport", "COM31")).ToLegacyOptions();

            Assert.IsTrue(legacy.ListMcuImages);
            Assert.IsTrue(legacy.McubootTarget);
            Assert.AreEqual("COM31", legacy.SerialPort);
            Assert.IsFalse(legacy.ListDevices);
        }

        [TestMethod]
        public void ListDevices_MapsSerialPort_WithoutMcubootTarget()
        {
            Options legacy = ((ListOptions)Parse("list", "devices", "serialport", "COM31")).ToLegacyOptions();

            Assert.IsTrue(legacy.ListDevices);
            Assert.IsFalse(legacy.McubootTarget);
            Assert.AreEqual("COM31", legacy.SerialPort);
        }

        #endregion

        // ======================================================================
        // keys
        // ======================================================================

        #region keys

        [TestMethod]
        public void Keys_Generate_Valid()
        {
            var o = (KeysOptions)Parse("keys", "generate", "key.pem");

            Assert.AreEqual("key.pem", o.Generate);
            Assert.IsNull(KeysOptions.Validate(o));
        }

        [TestMethod]
        public void Keys_GetPub_Valid()
        {
            var o = (KeysOptions)Parse("keys", "getpub", "pub.c", "signkey", "key.pem");

            Assert.AreEqual("pub.c", o.GetPub);
            Assert.AreEqual("key.pem", o.SignKey);
            Assert.IsNull(KeysOptions.Validate(o));
        }

        [TestMethod]
        public void Keys_NothingRequested_Invalid()
        {
            var o = (KeysOptions)Parse("keys");

            StringAssert.Contains(KeysOptions.Validate(o), "requires one of generate or getpub");
        }

        [TestMethod]
        public void Keys_GenerateAndGetPub_Invalid()
        {
            var o = (KeysOptions)Parse("keys", "generate", "key.pem", "getpub", "pub.c", "signkey", "key.pem");

            StringAssert.Contains(KeysOptions.Validate(o), "Only one of");
        }

        [TestMethod]
        public void Keys_GetPubWithoutSignKey_Invalid()
        {
            var o = (KeysOptions)Parse("keys", "getpub", "pub.c");

            StringAssert.Contains(KeysOptions.Validate(o), "requires signkey");
        }

        [TestMethod]
        public void Keys_GenerateWithSignKey_Invalid()
        {
            var o = (KeysOptions)Parse("keys", "generate", "key.pem", "signkey", "other.pem");

            StringAssert.Contains(KeysOptions.Validate(o), "only be used with getpub");
        }

        #endregion

        // ======================================================================
        // size parsing
        // ======================================================================

        #region size parsing

        [TestMethod]
        [DataRow("0x100000", 0x100000)]
        [DataRow("0XE8000", 0xE8000)]
        [DataRow("512", 512)]
        [DataRow("4", 4)]
        public void TryParseMcubootSize_ValidValues(string value, int expected)
        {
            Assert.IsTrue(VerbOptionsBase.TryParseMcubootSize(value, out int? result));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        public void TryParseMcubootSize_NotSpecified_IsValidAndNull(string value)
        {
            Assert.IsTrue(VerbOptionsBase.TryParseMcubootSize(value, out int? result));
            Assert.IsNull(result);
        }

        [TestMethod]
        [DataRow("0x")]
        [DataRow("0x-1")]
        [DataRow("+4")]
        [DataRow("4.0")]
        [DataRow("0x1FFFFFFFF")]
        public void TryParseMcubootSize_InvalidValues(string value)
        {
            Assert.IsFalse(VerbOptionsBase.TryParseMcubootSize(value, out _));
        }

        #endregion
    }

    /// <summary>
    /// End-to-end dispatch of the MCUboot commands through <see cref="Program.Main"/>: with a
    /// missing image file, only <see cref="McubootManager"/> answers with E10003, so getting that
    /// code back proves the command reached it rather than the wire protocol or ESP32 paths.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // because of static variables in the programs
    public sealed class McubootDispatchTests
    {
        public TestContext TestContext { get; set; } = null!;

        private string MissingImage => Path.Combine(TestDirectoryHelper.GetTestDirectory(TestContext), "missing.bin");

        [TestMethod]
        public void Flash_Mcuboot_ReachesMcubootManager()
        {
            using var output = new OutputWriterHelper();

            int exitCode = Program.Main(["flash", "serialport", "COM_NANOFF_TEST", "image", MissingImage, "mcuboot", "suppressnanoffversioncheck"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E10003, exitCode, output.Output);
            StringAssert.Contains(output.Output, "Image file not found");
        }

        [TestMethod]
        public void Flash_McubootWithStm32Target_ReachesMcubootManager()
        {
            using var output = new OutputWriterHelper();

            int exitCode = Program.Main(["flash", "target", "ORGPAL_PALTHREE", "serialport", "COM_NANOFF_TEST", "image", MissingImage, "mcuboot", "suppressnanoffversioncheck"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E10003, exitCode, output.Output);
        }

        [TestMethod]
        public void Deploy_Mcuboot_ReachesMcubootManager()
        {
            using var output = new OutputWriterHelper();

            int exitCode = Program.Main(["deploy", "serialport", "COM_NANOFF_TEST", "image", MissingImage, "mcuboot", "suppressnanoffversioncheck"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E10003, exitCode, output.Output);
            StringAssert.Contains(output.Output, "Image file not found");
        }

        [TestMethod]
        public void Deploy_McubootWithEsp32Target_NeverProvisions()
        {
            using var output = new OutputWriterHelper();

            // a deploy must never take the ESP32 manager path, which may flash the MCUboot package
            int exitCode = Program.Main(["deploy", "target", "ESP32_S3", "serialport", "COM_NANOFF_TEST", "image", MissingImage, "mcuboot", "suppressnanoffversioncheck"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E10003, exitCode, output.Output);
        }

        [TestMethod]
        public void ListImages_WithoutMcuboot_IsRejected()
        {
            using var output = new OutputWriterHelper();

            int exitCode = Program.Main(["list", "images", "serialport", "COM_NANOFF_TEST", "suppressnanoffversioncheck"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, exitCode);
        }

        [TestMethod]
        public void Keys_GetPubWithoutSignKey_IsRejected()
        {
            using var output = new OutputWriterHelper();

            int exitCode = Program.Main(["keys", "getpub", "pub.c"])
                .GetAwaiter().GetResult();

            Assert.AreEqual((int)ExitCodes.E9000, exitCode);
        }
    }
}
