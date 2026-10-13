// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for <see cref="McubootUpdateBundle"/>: the update bundle (nanoCLR + deployment)
    /// built by <c>nanoff pack update</c>.
    /// </summary>
    [TestClass]
    public class McubootUpdateBundleTests
    {
        private const ushort HeaderSize = 0x200;

        public TestContext TestContext { get; set; }

        /// <summary>
        /// Builds a signed MCUboot image: header, payload, optional protected TLV area and an
        /// unprotected TLV area carrying the SHA-256 of header + payload + protected TLVs.
        /// </summary>
        private static byte[] MakeSignedImage(
            byte major,
            byte minor,
            ushort revision,
            uint build,
            int payloadSize,
            bool withProtectedTlv = false,
            bool withHash = true,
            byte fill = 0x5A)
        {
            ushort protectedSize = (ushort)(withProtectedTlv ? 4 + 4 + 4 : 0);

            var image = new List<byte>();
            byte[] header = new byte[HeaderSize];
            BitConverter.GetBytes(0x96f3b83du).CopyTo(header, 0);
            BitConverter.GetBytes(HeaderSize).CopyTo(header, 8);
            BitConverter.GetBytes(protectedSize).CopyTo(header, 10);
            BitConverter.GetBytes((uint)payloadSize).CopyTo(header, 12);
            header[20] = major;
            header[21] = minor;
            BitConverter.GetBytes(revision).CopyTo(header, 22);
            BitConverter.GetBytes(build).CopyTo(header, 24);
            image.AddRange(header);

            image.AddRange(Enumerable.Range(0, payloadSize).Select(i => (byte)(fill + i)));

            if (withProtectedTlv)
            {
                // info (0x6908, total 12) + one TLV (type 0x50, len 4)
                image.AddRange(BitConverter.GetBytes((ushort)0x6908));
                image.AddRange(BitConverter.GetBytes(protectedSize));
                image.AddRange(BitConverter.GetBytes((ushort)0x50));
                image.AddRange(BitConverter.GetBytes((ushort)4));
                image.AddRange(new byte[] { 1, 2, 3, 4 });
            }

            byte[] hash;

            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(image.ToArray());
            }

            // unprotected area: info + SHA-256 TLV (or another TLV when no hash is wanted)
            image.AddRange(BitConverter.GetBytes((ushort)0x6907));
            image.AddRange(BitConverter.GetBytes((ushort)(4 + 4 + 32)));
            image.AddRange(BitConverter.GetBytes((ushort)(withHash ? 0x10 : 0x22)));
            image.AddRange(BitConverter.GetBytes((ushort)32));
            image.AddRange(hash);

            return image.ToArray();
        }

        private string WriteFile(string directory, string name, byte[] content)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private ExitCodes Create(string clr, string deployment, string output, bool overwrite, out string bundlePath, out string error)
        {
            using var writer = new OutputWriterHelper();

            return McubootUpdateBundle.Create(clr, deployment, output, overwrite, VerbosityLevel.Quiet, out bundlePath, out error);
        }

        #region Bundle layout

        [TestMethod]
        public void Create_RoundTrip_ManifestAndImages()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            byte[] clr = MakeSignedImage(1, 12, 0, 123, 1000);
            byte[] dep = MakeSignedImage(1, 4, 0, 7, 300, withProtectedTlv: true);
            string clrPath = WriteFile(dir, "clr.bin", clr);
            string depPath = WriteFile(dir, "dep.bin", dep);

            ExitCodes result = Create(clrPath, depPath, dir, false, out string bundlePath, out string error);

            Assert.AreEqual(ExitCodes.OK, result, error);

            byte[] bundle = File.ReadAllBytes(bundlePath);

            Assert.AreEqual("NFUPDATE", Encoding.ASCII.GetString(bundle, 0, 8));
            Assert.AreEqual(1, BitConverter.ToUInt16(bundle, 8));
            Assert.AreEqual(0x28, BitConverter.ToUInt16(bundle, 10));
            Assert.AreEqual(2, bundle[12]);
            Assert.IsTrue(McubootUpdateBundle.TryParseBundle(bundle, out List<McubootUpdateBundle.BundleEntry> entries));
            Assert.AreEqual(2, entries.Count);

            Assert.AreEqual(0, entries[0].Image);
            Assert.AreEqual(0x28u, entries[0].Offset);
            Assert.AreEqual((uint)clr.Length, entries[0].Size);
            CollectionAssert.AreEqual(clr, bundle.Skip((int)entries[0].Offset).Take((int)entries[0].Size).ToArray());

            Assert.AreEqual(1, entries[1].Image);
            Assert.AreEqual((uint)dep.Length, entries[1].Size);
            CollectionAssert.AreEqual(dep, bundle.Skip((int)entries[1].Offset).Take((int)entries[1].Size).ToArray());

            Assert.AreEqual(entries[1].Offset + entries[1].Size, (uint)bundle.Length, "the bundle ends with the deployment");
        }

        [TestMethod]
        public void BuildBundleBytes_DeploymentAlignedAndPaddedWithFF()
        {
            byte[] dep = new byte[] { 1, 2, 3, 4, 5 };

            for (int clrLength = 1; clrLength <= 8; clrLength++)
            {
                byte[] clr = Enumerable.Repeat((byte)0xAA, clrLength).ToArray();
                byte[] bundle = McubootUpdateBundle.BuildBundleBytes(clr, dep);

                Assert.IsTrue(McubootUpdateBundle.TryParseBundle(bundle, out List<McubootUpdateBundle.BundleEntry> entries));
                Assert.AreEqual(0u, entries[1].Offset % 4, $"clr length {clrLength}");
                Assert.IsTrue(entries[1].Offset >= entries[0].Offset + entries[0].Size, "entries must not overlap");

                for (uint i = entries[0].Offset + entries[0].Size; i < entries[1].Offset; i++)
                {
                    Assert.AreEqual(0xFF, bundle[i], "padding byte");
                }
            }
        }

        [TestMethod]
        public void Create_DropsBytesAfterTheTlvArea()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            byte[] clr = MakeSignedImage(1, 0, 0, 1, 64);
            byte[] padded = clr.Concat(Enumerable.Repeat((byte)0xFF, 100)).ToArray();
            string clrPath = WriteFile(dir, "clr.bin", padded);
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 32));

            Assert.AreEqual(ExitCodes.OK, Create(clrPath, depPath, dir, false, out string bundlePath, out string error), error);

            McubootUpdateBundle.TryParseBundle(File.ReadAllBytes(bundlePath), out List<McubootUpdateBundle.BundleEntry> entries);

            Assert.AreEqual((uint)clr.Length, entries[0].Size);
        }

        #endregion

        #region Naming and output

        [TestMethod]
        public void DefaultName_TakenFromTheClrVersion()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 12, 0, 123, 64));
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(3, 4, 5, 6, 64));

            Assert.AreEqual(ExitCodes.OK, Create(clrPath, depPath, dir, false, out string bundlePath, out string error), error);

            Assert.AreEqual(Path.Combine(dir, "nanoUpdate-1.12.0.123.bin"), bundlePath);
            Assert.IsTrue(File.Exists(bundlePath));
        }

        [TestMethod]
        public void DefaultName_EdgeVersionValues()
        {
            byte[] image = MakeSignedImage(255, 255, 65535, uint.MaxValue, 16);

            Assert.AreEqual(
                "nanoUpdate-255.255.65535.4294967295.bin",
                McubootUpdateBundle.DefaultFileName(McubootUpdateBundle.GetVersion(image)));
        }

        [TestMethod]
        public void Output_DirectoryWithSeparator_IsCreatedAndUsesDefaultName()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(2, 0, 0, 9, 64));
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string outDir = Path.Combine(dir, "out") + Path.DirectorySeparatorChar;

            Assert.AreEqual(ExitCodes.OK, Create(clrPath, depPath, outDir, false, out string bundlePath, out string error), error);

            Assert.AreEqual(Path.Combine(dir, "out", "nanoUpdate-2.0.0.9.bin"), bundlePath);
        }

        [TestMethod]
        public void Output_AnyFileNameIsAccepted()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string output = Path.Combine(dir, "my-update.img");

            using var writer = new OutputWriterHelper();

            ExitCodes result = McubootUpdateBundle.Create(clrPath, depPath, output, false, VerbosityLevel.Normal, out string bundlePath, out string error);

            Assert.AreEqual(ExitCodes.OK, result, error);
            Assert.AreEqual(output, bundlePath);
            Assert.IsFalse(writer.Output.Contains("warning", StringComparison.OrdinalIgnoreCase), writer.Output);
        }

        [TestMethod]
        public void Output_Exists_RefusedWithoutOverwrite()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string output = WriteFile(dir, "bundle.bin", new byte[] { 1, 2, 3 });

            Assert.AreEqual(ExitCodes.E10026, Create(clrPath, depPath, output, false, out _, out _));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(output), "existing file must be untouched");
        }

        [TestMethod]
        public void Output_Exists_ReplacedWithOverwrite()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string output = WriteFile(dir, "bundle.bin", new byte[] { 1, 2, 3 });

            Assert.AreEqual(ExitCodes.OK, Create(clrPath, depPath, output, true, out _, out string error), error);
            Assert.IsTrue(McubootUpdateBundle.TryParseBundle(File.ReadAllBytes(output), out _));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp").Length, "no temp file left behind");
        }

        [TestMethod]
        public void Output_SameAsInput_Refused()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));

            Assert.AreEqual(ExitCodes.E9000, Create(clrPath, depPath, clrPath, true, out _, out _));
        }

        #endregion

        #region Image checks

        [TestMethod]
        public void MissingImage_E10023()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));

            Assert.AreEqual(ExitCodes.E10023, Create(Path.Combine(dir, "missing.bin"), depPath, dir, false, out _, out _));
        }

        [TestMethod]
        public void BadMagic_E10024()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            byte[] clr = MakeSignedImage(1, 0, 0, 1, 64);
            clr[0] ^= 0xFF;
            string clrPath = WriteFile(dir, "clr.bin", clr);
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));

            Assert.AreEqual(ExitCodes.E10024, Create(clrPath, depPath, dir, false, out _, out _));
        }

        [TestMethod]
        public void TruncatedImage_E10024()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            byte[] dep = MakeSignedImage(1, 0, 0, 1, 64);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string depPath = WriteFile(dir, "dep.bin", dep.Take(dep.Length - 10).ToArray());

            Assert.AreEqual(ExitCodes.E10024, Create(clrPath, depPath, dir, false, out _, out _));
        }

        [TestMethod]
        public void NoHashTlv_E10025()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 0, 0, 1, 64, withHash: false));
            string depPath = WriteFile(dir, "dep.bin", MakeSignedImage(1, 0, 0, 1, 64));

            Assert.AreEqual(ExitCodes.E10025, Create(clrPath, depPath, dir, false, out _, out _));
        }

        [TestMethod]
        public void CorruptPayload_E10025()
        {
            string dir = TestDirectoryHelper.GetTestDirectory(TestContext);
            byte[] dep = MakeSignedImage(1, 0, 0, 1, 64);
            dep[HeaderSize + 10] ^= 0x01;
            string clrPath = WriteFile(dir, "clr.bin", MakeSignedImage(1, 0, 0, 1, 64));
            string depPath = WriteFile(dir, "dep.bin", dep);

            Assert.AreEqual(ExitCodes.E10025, Create(clrPath, depPath, dir, false, out _, out _));
            Assert.AreEqual(0, Directory.GetFiles(dir, "nanoUpdate-*").Length, "nothing written on failure");
        }

        #endregion

        #region Manifest parsing

        [TestMethod]
        public void TryParseBundle_RejectsBadManifests()
        {
            byte[] good = McubootUpdateBundle.BuildBundleBytes(new byte[8], new byte[8]);

            Assert.IsTrue(McubootUpdateBundle.TryParseBundle(good, out _));

            byte[] badMarker = (byte[])good.Clone();
            badMarker[0] = (byte)'X';
            Assert.IsFalse(McubootUpdateBundle.TryParseBundle(badMarker, out _));

            byte[] duplicate = (byte[])good.Clone();
            duplicate[16 + 12] = 0;
            Assert.IsFalse(McubootUpdateBundle.TryParseBundle(duplicate, out _));

            byte[] outOfBounds = (byte[])good.Clone();
            BitConverter.GetBytes(1000u).CopyTo(outOfBounds, 16 + 12 + 8);
            Assert.IsFalse(McubootUpdateBundle.TryParseBundle(outOfBounds, out _));

            byte[] misaligned = (byte[])good.Clone();
            BitConverter.GetBytes(0x29u).CopyTo(misaligned, 16 + 4);
            Assert.IsFalse(McubootUpdateBundle.TryParseBundle(misaligned, out _));
        }

        #endregion
    }
}
