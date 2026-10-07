// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Integration tests that exercise the full keygen → sign → validate pipeline
    /// against a real <c>imgtool</c> install (§3.3, §3.5, §15.1–15.3).
    ///
    /// These reproduce the parameters used by mcuboot's own <c>test_sign_verify</c>
    /// (scripts/tests/test_keys.py): a 1 KB zero image, ECDSA P-256 key,
    /// align 16, version 1.0.0, header-size 0x400, slot-size 0x10000.
    ///
    /// When imgtool is not installed (<see cref="McubootImageManager.FindImgtool"/>
    /// returns null), every test is marked Inconclusive so the suite stays green on
    /// machines without the Python toolchain. On CI agents with imgtool present they
    /// run for real and assert that a freshly signed image passes our header parser.
    /// </summary>
    [TestClass]
    public class McubootImageManagerImgtoolTests
    {
        // Same parameters as mcuboot scripts/tests/test_keys.py::test_sign_verify.
        private const int HeaderSize = 0x400;
        private const int SlotSize = 0x10000;   // 64 KB
        private const int WriteAlign = 16;
        private const int ImageSizeBytes = 1024;
        private const string Version = "1.0.0.0";

        private string _workDir;

        [TestInitialize]
        public void Setup()
        {
            if (McubootImageManager.FindImgtool() is null)
            {
                Assert.Inconclusive(
                    "imgtool not installed (pip install imgtool). Skipping real signing tests.");
            }

            _workDir = Path.Combine(Path.GetTempPath(), "nanoff_imgtool_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_workDir is not null && Directory.Exists(_workDir))
            {
                try { Directory.Delete(_workDir, recursive: true); } catch { /* best effort */ }
            }
        }

        [TestMethod]
        public void GenerateSigningKey_RealImgtool_ProducesNonEmptyPemKey()
        {
            string keyPath = Path.Combine(_workDir, "key.pem");

            var manager = new McubootImageManager(keyPath, SlotSize, HeaderSize, WriteAlign);
            ExitCodes result = manager.GenerateSigningKey(keyPath);

            Assert.AreEqual(ExitCodes.OK, result);
            Assert.IsTrue(File.Exists(keyPath), "key file must be created");
            Assert.IsTrue(new FileInfo(keyPath).Length > 0, "key file must be non-empty");
        }

        [TestMethod]
        public void ExtractPublicKey_RealImgtool_WritesCSourceArray()
        {
            string keyPath = Path.Combine(_workDir, "key.pem");
            string pubCPath = Path.Combine(_workDir, "root-pub-key.c");

            var manager = new McubootImageManager(keyPath, SlotSize, HeaderSize, WriteAlign);
            Assert.AreEqual(ExitCodes.OK, manager.GenerateSigningKey(keyPath));

            ExitCodes result = manager.ExtractPublicKey(keyPath, pubCPath);

            Assert.AreEqual(ExitCodes.OK, result);
            Assert.IsTrue(File.Exists(pubCPath), "C source file must be created");

            string content = File.ReadAllText(pubCPath);
            // imgtool getpub --lang c emits a C byte array for the public key.
            StringAssert.Contains(content, "unsigned char", "expected a C byte array declaration");
            StringAssert.Contains(content, "pub_key", "expected the public key symbol");
        }

        [TestMethod]
        public void SignImage_RealImgtool_ProducesImageValidatingAgainstOurParser()
        {
            string keyPath = Path.Combine(_workDir, "key.pem");
            string unsignedPath = Path.Combine(_workDir, "image.bin");
            string signedPath = Path.Combine(_workDir, "image.signed.bin");

            // 1 KB all-zero payload, exactly like mcuboot's test_sign_verify.
            File.WriteAllBytes(unsignedPath, new byte[ImageSizeBytes]);

            var manager = new McubootImageManager(keyPath, SlotSize, HeaderSize, WriteAlign);
            Assert.AreEqual(ExitCodes.OK, manager.GenerateSigningKey(keyPath));

            // 2. Sign with the real imgtool.
            ExitCodes signResult = manager.SignImage(unsignedPath, signedPath, Version);
            Assert.AreEqual(ExitCodes.OK, signResult, "imgtool sign must succeed");
            Assert.IsTrue(File.Exists(signedPath), "signed image must be produced");

            // 3. Our own ValidateImage parser must accept the real signed image.
            McubootImageInfo info = manager.ValidateImage(signedPath);

            Assert.IsTrue(info.IsValid, "real imgtool-signed image must pass our header parser");
            Assert.AreEqual(0x96f3b83du, info.HeaderMagic, "magic must match the MCUboot constant");
            Assert.AreEqual((uint)HeaderSize, info.HeaderSize, "header size must round-trip");
            Assert.AreEqual("1.0.0.0", info.Version, "version must round-trip");
            Assert.IsTrue(info.ImageSize >= ImageSizeBytes,
                "reported image size must cover the original payload");
        }

        [TestMethod]
        public void SignImage_RealImgtool_HeaderSizePlusImageFitsSlot()
        {
            string keyPath = Path.Combine(_workDir, "key.pem");
            string unsignedPath = Path.Combine(_workDir, "image.bin");
            string signedPath = Path.Combine(_workDir, "image.signed.bin");

            File.WriteAllBytes(unsignedPath, new byte[ImageSizeBytes]);

            var manager = new McubootImageManager(keyPath, SlotSize, HeaderSize, WriteAlign);
            Assert.AreEqual(ExitCodes.OK, manager.GenerateSigningKey(keyPath));
            Assert.AreEqual(ExitCodes.OK, manager.SignImage(unsignedPath, signedPath, Version));

            McubootImageInfo info = manager.ValidateImage(signedPath);
            Assert.IsTrue(info.HeaderSize + info.ImageSize <= (uint)SlotSize,
                "header + image must fit within the configured slot size");
        }
    }
}
