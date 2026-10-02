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
    /// Unit tests for <see cref="McubootImageManager.SignImage"/>,
    /// <see cref="McubootImageManager.GenerateSigningKey"/>,
    /// <see cref="McubootImageManager.ExtractPublicKey"/>,
    /// <see cref="McubootImageManager.FormatImgtoolVersion"/>, and
    /// <see cref="McubootImageManager.FindImgtool"/> (§3.1–3.6).
    /// </summary>
    [TestClass]
    public class McubootImageManagerSigningTests
    {
        // Non-existent path used to exercise the "imgtool fails to start" branch.
        private const string NonExistentImgtool = "nonexistent-imgtool-xyz-12345";

        private static McubootImageManager CreateManager(string imgtoolPath = NonExistentImgtool)
            => new McubootImageManager(
                signingKeyPath: "test-key.pem",
                slotSize: 0x80000,
                imgtoolPath: imgtoolPath);

        // -----------------------------------------------------------------------
        // FormatImgtoolVersion — pure string conversion, no imgtool needed
        // -----------------------------------------------------------------------

        [TestMethod]
        public void FormatImgtoolVersion_TypicalVersion_ReturnsPlusForBuild()
        {
            string result = McubootImageManager.FormatImgtoolVersion("1.12.0.45");
            Assert.AreEqual("1.12.0+45", result);
        }

        [TestMethod]
        public void FormatImgtoolVersion_ZeroBuild_ReturnsPlusSeparator()
        {
            string result = McubootImageManager.FormatImgtoolVersion("2.0.0.0");
            Assert.AreEqual("2.0.0+0", result);
        }

        [TestMethod]
        public void FormatImgtoolVersion_LargeNumbers_ConvertedCorrectly()
        {
            string result = McubootImageManager.FormatImgtoolVersion("255.255.65535.4294967295");
            Assert.AreEqual("255.255.65535+4294967295", result);
        }

        [TestMethod]
        public void FormatImgtoolVersion_SingleDigitParts_ReturnsPlusSeparator()
        {
            string result = McubootImageManager.FormatImgtoolVersion("1.2.3.4");
            Assert.AreEqual("1.2.3+4", result);
        }

        [TestMethod]
        public void FormatImgtoolVersion_ZeroVersion_ReturnsPlusSeparator()
        {
            string result = McubootImageManager.FormatImgtoolVersion("0.0.0.0");
            Assert.AreEqual("0.0.0+0", result);
        }

        // -----------------------------------------------------------------------
        // SignImage — argument validation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void SignImage_NullInputPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CreateManager().SignImage(null, "output.bin", "1.0.0.0"));
        }

        [TestMethod]
        public void SignImage_NullOutputPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CreateManager().SignImage("input.bin", null, "1.0.0.0"));
        }

        [TestMethod]
        public void SignImage_NullVersion_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CreateManager().SignImage("input.bin", "output.bin", null));
        }

        [TestMethod]
        public void SignImage_ImgtoolNotStartable_ReturnsE10002()
        {
            // When the imgtool process cannot be started, RunImgtool returns exit -1,
            // which SignImage maps to E10002 (image signing failed).
            ExitCodes result = CreateManager(NonExistentImgtool)
                .SignImage("input.bin", "output.bin", "1.0.0.0");

            Assert.AreEqual(ExitCodes.E10002, result);
        }

        // -----------------------------------------------------------------------
        // GenerateSigningKey — argument validation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void GenerateSigningKey_NullOutputPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CreateManager().GenerateSigningKey(null));
        }

        [TestMethod]
        public void GenerateSigningKey_ImgtoolNotStartable_ReturnsE10004()
        {
            ExitCodes result = CreateManager(NonExistentImgtool)
                .GenerateSigningKey("my-key.pem");

            Assert.AreEqual(ExitCodes.E10004, result);
        }

        // -----------------------------------------------------------------------
        // ExtractPublicKey — argument validation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void ExtractPublicKey_NullSigningKeyPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CreateManager().ExtractPublicKey(null, "root-pub-key.c"));
        }

        [TestMethod]
        public void ExtractPublicKey_NullOutputPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CreateManager().ExtractPublicKey("my-key.pem", null));
        }

        [TestMethod]
        public void ExtractPublicKey_ImgtoolNotStartable_ReturnsE10004()
        {
            ExitCodes result = CreateManager(NonExistentImgtool)
                .ExtractPublicKey("my-key.pem", "root-pub-key.c");

            Assert.AreEqual(ExitCodes.E10004, result);
        }

        // -----------------------------------------------------------------------
        // McubootImageManager constructor — signing key null check
        // -----------------------------------------------------------------------

        [TestMethod]
        public void Constructor_NullSigningKeyPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new McubootImageManager(
                signingKeyPath: null,
                slotSize: 0x80000));
        }

        // -----------------------------------------------------------------------
        // FindImgtool — discovery logic
        // -----------------------------------------------------------------------

        [TestMethod]
        public void FindImgtool_ReturnsNullOrNonEmptyString()
        {
            // FindImgtool() either locates imgtool and returns a non-empty string,
            // or returns null when it is not installed. Both outcomes are valid.
            string result = McubootImageManager.FindImgtool();
            Assert.IsTrue(result is null || result.Length > 0,
                "FindImgtool must return null or a non-empty locator string");
        }

        [TestMethod]
        public void FindImgtool_WhenNull_RequireImgtoolThrowsMcubootImageException()
        {
            // Build a manager where imgtoolPath starts as null (auto-detect),
            // then immediately try to sign — if FindImgtool() returns null on this
            // machine, RequireImgtool() must throw McubootImageException.
            // If FindImgtool() actually finds imgtool, the test is skipped.
            if (McubootImageManager.FindImgtool() is not null)
            {
                Assert.Inconclusive("imgtool is installed on this machine; cannot test the not-found path.");
                return;
            }

            var manager = new McubootImageManager(
                signingKeyPath: "test-key.pem",
                slotSize: 0x80000,
                imgtoolPath: null); // triggers auto-detect, which will return null

            Assert.Throws<McubootImageException>(
                () => manager.SignImage("in.bin", "out.bin", "1.0.0.0"),
                "When imgtool is not found, SignImage must throw McubootImageException");
        }

        // -----------------------------------------------------------------------
        // SignImage — exit-code propagation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void SignImage_ImgtoolExitsNonZero_ReturnsE10002()
        {
            // Even if the process starts but exits with a non-zero code, SignImage
            // must return E10002. We rely on the NonExistentImgtool path causing
            // process-start failure (exit -1), which is the same branch.
            ExitCodes result = CreateManager(NonExistentImgtool)
                .SignImage("input.bin", "output.bin", "1.2.3.4");

            Assert.AreEqual(ExitCodes.E10002, result,
                "Any non-zero imgtool exit code must map to E10002");
        }

        // -----------------------------------------------------------------------
        // GenerateSigningKey — exit-code propagation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void GenerateSigningKey_ImgtoolExitsNonZero_ReturnsE10004()
        {
            ExitCodes result = CreateManager(NonExistentImgtool)
                .GenerateSigningKey("my-key.pem");

            Assert.AreEqual(ExitCodes.E10004, result,
                "Any non-zero imgtool exit code must map to E10004");
        }

        // -----------------------------------------------------------------------
        // ExtractPublicKey — exit-code propagation
        // -----------------------------------------------------------------------

        [TestMethod]
        public void ExtractPublicKey_ImgtoolExitsNonZero_ReturnsE10004()
        {
            ExitCodes result = CreateManager(NonExistentImgtool)
                .ExtractPublicKey("my-key.pem", "root-pub-key.c");

            Assert.AreEqual(ExitCodes.E10004, result,
                "Any non-zero imgtool exit code must map to E10004");
        }
    }
}
