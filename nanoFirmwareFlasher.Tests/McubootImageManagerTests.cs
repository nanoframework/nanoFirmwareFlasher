// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Unit tests for <see cref="McubootImageManager.ValidateImage"/> (§12.6 and §12.7).
    ///
    /// Each test writes a synthetic MCUboot header to a temp file, calls ValidateImage,
    /// and verifies the returned <see cref="McubootImageInfo"/>.
    ///
    /// MCUboot image header layout (32 bytes, all multi-byte fields LE):
    ///   [0-3]   magic        (uint32)  - 0x96f3b83d for valid image
    ///   [4-7]   load_addr    (uint32)
    ///   [8-9]   hdr_size     (uint16)
    ///   [10-11] protect_tlv  (uint16)
    ///   [12-15] img_size     (uint32)
    ///   [16-19] flags        (uint32)
    ///   [20]    ver.major    (uint8)
    ///   [21]    ver.minor    (uint8)
    ///   [22-23] ver.revision (uint16)
    ///   [24-27] ver.build    (uint32)
    ///   [28-31] padding
    /// </summary>
    [TestClass]
    public class McubootImageManagerTests
    {
        private const uint   ValidMagic  = 0x96f3b83du;
        private const uint   BadMagic    = 0xDEADBEEFu;
        private const int    SlotSize    = 0x80000; // 512 KB - typical MCUboot slot

        // ValidateImage ignores the signing key (it's only used for signing), so any non-null
        // path satisfies the constructor; "test-key.pem" need not exist on disk.
        private static McubootImageManager CreateManager(int slotSize = SlotSize)
            => new McubootImageManager(
                signingKeyPath: "test-key.pem",
                slotSize: slotSize,
                imgtoolPath: "imgtool"); // skip auto-detect in ctor

        [TestMethod]
        public void ValidateImage_ValidHeader_IsValidTrue()
        {
            string path = WriteImageFile(
                magic: ValidMagic,
                hdrSize: 0x200,
                imgSize: 0x40000,  // 256 KB image, well within 512 KB slot
                major: 1, minor: 12, revision: 3, buildNum: 45,
                extraBytes: 0x40000);

            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.IsTrue(info.IsValid, "header with valid magic and size within slot must be valid");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ValidHeader_CorrectMagicReturned()
        {
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: 0x100,
                                         major: 0, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.AreEqual(ValidMagic, info.HeaderMagic);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ValidHeader_CorrectVersionReturned()
        {
            // Version 1.12.3.45 → major=1, minor=12, revision=3, buildNum=45
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: 0x1000,
                                         major: 1, minor: 12, revision: 3, buildNum: 45);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.AreEqual("1.12.3.45", info.Version);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ValidHeader_CorrectHeaderSizeReturned()
        {
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: 0x1000,
                                         major: 0, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.AreEqual(0x200u, info.HeaderSize);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ValidHeader_CorrectImageSizeReturned()
        {
            uint imgSize = 0x30000;
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: imgSize,
                                         major: 0, minor: 0, revision: 0, buildNum: 0,
                                         extraBytes: (int)imgSize);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.AreEqual(imgSize, info.ImageSize);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_VersionAllZeros_ReturnsZeroVersionString()
        {
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: 0x100,
                                         major: 0, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.AreEqual("0.0.0.0", info.Version);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_VersionMaxValues_ParsedCorrectly()
        {
            // major=255, minor=255, revision=65535, buildNum=4294967295
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: 0x100,
                                         major: 255, minor: 255, revision: 65535, buildNum: uint.MaxValue);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.AreEqual("255.255.65535.4294967295", info.Version);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_InvalidMagic_IsValidFalse()
        {
            string path = WriteImageFile(magic: BadMagic, hdrSize: 0x200, imgSize: 0x1000,
                                         major: 1, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.IsFalse(info.IsValid, "wrong magic byte must produce IsValid=false");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_InvalidMagic_MagicFieldStillPopulated()
        {
            string path = WriteImageFile(magic: BadMagic, hdrSize: 0x200, imgSize: 0x100,
                                         major: 0, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.AreEqual(BadMagic, info.HeaderMagic, "HeaderMagic should reflect the bytes on disk");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ZeroMagic_IsValidFalse()
        {
            string path = WriteImageFile(magic: 0, hdrSize: 0, imgSize: 0,
                                         major: 0, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.IsFalse(info.IsValid);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_FileTooShort_IsValidFalse()
        {
            // Write only 16 bytes - not enough for a complete header
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[16]);
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.IsFalse(info.IsValid, "file shorter than 32-byte header must be invalid");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_EmptyFile_IsValidFalse()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, Array.Empty<byte>());
                McubootImageInfo info = CreateManager().ValidateImage(path);
                Assert.IsFalse(info.IsValid);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ImageExceedsSlot_IsValidFalse()
        {
            // hdr_size=0x200, img_size=SlotSize → total = SlotSize + 0x200 > SlotSize
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: (uint)SlotSize,
                                         major: 1, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager(SlotSize).ValidateImage(path);
                Assert.IsFalse(info.IsValid, "hdr_size + img_size > slot_size must produce IsValid=false");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_SizeSumOverflowsUInt_IsValidFalse()
        {
            // hdr_size + img_size wraps around to 0x1FF as a uint, which would fit any slot
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: uint.MaxValue,
                                         major: 1, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager(SlotSize).ValidateImage(path);
                Assert.IsFalse(info.IsValid, "an image size overflowing hdr_size + img_size must produce IsValid=false");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ImageExactlyFitsSlot_IsValidTrue()
        {
            // hdr_size=0x200, img_size = slot - hdr_size → total exactly equals slot_size
            uint hdrSize = 0x200;
            uint imgSize = (uint)SlotSize - hdrSize;

            string path = WriteImageFile(magic: ValidMagic, hdrSize: (ushort)hdrSize, imgSize: imgSize,
                                         major: 2, minor: 0, revision: 0, buildNum: 0,
                                         extraBytes: (int)imgSize);
            try
            {
                McubootImageInfo info = CreateManager(SlotSize).ValidateImage(path);
                Assert.IsTrue(info.IsValid, "image that exactly fills the slot must be valid");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_ImageOneByteOverSlot_IsValidFalse()
        {
            uint hdrSize = 0x200;
            uint imgSize = (uint)SlotSize - hdrSize + 1; // one byte too many

            string path = WriteImageFile(magic: ValidMagic, hdrSize: (ushort)hdrSize, imgSize: imgSize,
                                         major: 0, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager(SlotSize).ValidateImage(path);
                Assert.IsFalse(info.IsValid, "image one byte over slot must be invalid");
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_SmallSlot_LargeImage_IsValidFalse()
        {
            // tiny 4 KB slot, but image claims 8 KB
            int smallSlot = 0x1000;
            string path = WriteImageFile(magic: ValidMagic, hdrSize: 0x200, imgSize: 0x2000,
                                         major: 1, minor: 0, revision: 0, buildNum: 0);
            try
            {
                McubootImageInfo info = CreateManager(smallSlot).ValidateImage(path);
                Assert.IsFalse(info.IsValid);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void ValidateImage_NullPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CreateManager().ValidateImage(null));
        }

        /// <summary>
        /// Writes a synthetic MCUboot image binary to a temp file and returns its path.
        /// <paramref name="extraBytes"/> pads the file so img_size bytes of "image" exist
        /// after the 32-byte header (helps test file reading; not required for header parse).
        /// </summary>
        private static string WriteImageFile(
            uint magic, ushort hdrSize, uint imgSize,
            byte major, byte minor, ushort revision, uint buildNum,
            int extraBytes = 0)
        {
            string path = Path.GetTempFileName();
            byte[] header = new byte[32];

            // magic at 0
            Buffer.BlockCopy(BitConverter.GetBytes(magic), 0, header, 0, 4);
            // hdr_size at 8
            Buffer.BlockCopy(BitConverter.GetBytes(hdrSize), 0, header, 8, 2);
            // img_size at 12
            Buffer.BlockCopy(BitConverter.GetBytes(imgSize), 0, header, 12, 4);
            // version
            header[20] = major;
            header[21] = minor;
            Buffer.BlockCopy(BitConverter.GetBytes(revision), 0, header, 22, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(buildNum), 0, header, 24, 4);

            using var fs = File.Create(path);
            fs.Write(header, 0, header.Length);
            if (extraBytes > 0)
            {
                byte[] pad = new byte[extraBytes];
                fs.Write(pad, 0, pad.Length);
            }

            return path;
        }

        #region TryGetImageHash

        [TestMethod]
        public void TryGetImageHash_UnprotectedTlvArea_ReturnsSha256()
        {
            byte[] expected = new byte[32];
            for (int i = 0; i < 32; i++) expected[i] = (byte)(i + 1);

            byte[] image = BuildSignedImage(
                payloadSize: 64,
                unprotectedTlvs: [(TlvSha256, expected), (TlvKeyHash, new byte[32])]);

            Assert.IsTrue(McubootImageManager.TryGetImageHash(image, out byte[] hash));
            CollectionAssert.AreEqual(expected, hash);
        }

        [TestMethod]
        public void TryGetImageHash_SkipsProtectedTlvArea()
        {
            byte[] expected = new byte[32];
            for (int i = 0; i < 32; i++) expected[i] = (byte)(0xF0 - i);

            // a protected area precedes the unprotected one; the hash lives in the latter
            byte[] image = BuildSignedImage(
                payloadSize: 32,
                unprotectedTlvs: [(TlvSha256, expected)],
                protectedTlvs: [(TlvDependency, new byte[16])]);

            Assert.IsTrue(McubootImageManager.TryGetImageHash(image, out byte[] hash));
            CollectionAssert.AreEqual(expected, hash);
        }

        [TestMethod]
        public void TryGetImageHash_NoSha256Tlv_ReturnsFalse()
        {
            byte[] image = BuildSignedImage(
                payloadSize: 16,
                unprotectedTlvs: [(TlvKeyHash, new byte[32])]);

            Assert.IsFalse(McubootImageManager.TryGetImageHash(image, out byte[] hash));
            Assert.IsNull(hash);
        }

        [TestMethod]
        public void TryGetImageHash_UnsignedImage_ReturnsFalse()
        {
            // a raw binary has no MCUboot header, so the vector table is at offset 0
            byte[] raw = new byte[256];
            raw[0] = 0x30; raw[1] = 0x04; raw[2] = 0x00; raw[3] = 0x20;

            Assert.IsFalse(McubootImageManager.TryGetImageHash(raw, out byte[] hash));
            Assert.IsNull(hash);
        }

        [TestMethod]
        public void TryGetImageHash_TruncatedTlvArea_ReturnsFalseWithoutThrowing()
        {
            byte[] image = BuildSignedImage(
                payloadSize: 16,
                unprotectedTlvs: [(TlvSha256, new byte[32])]);

            // lop off the tail so the declared TLV area runs past the end of the buffer
            byte[] truncated = new byte[image.Length - 8];
            Array.Copy(image, truncated, truncated.Length);

            Assert.IsFalse(McubootImageManager.TryGetImageHash(truncated, out byte[] hash));
            Assert.IsNull(hash);
        }

        [TestMethod]
        public void TryGetImageHash_NullOrShortInput_ReturnsFalse()
        {
            Assert.IsFalse(McubootImageManager.TryGetImageHash(null, out byte[] h1));
            Assert.IsNull(h1);

            Assert.IsFalse(McubootImageManager.TryGetImageHash(new byte[4], out byte[] h2));
            Assert.IsNull(h2);
        }

        private const ushort TlvSha256 = 0x10;
        private const ushort TlvKeyHash = 0x01;
        private const ushort TlvDependency = 0x40;
        private const ushort TlvInfoMagic = 0x6907;
        private const ushort TlvInfoMagicProtected = 0x6908;
        private const ushort ImageHeaderSize = 0x200;

        /// <summary>
        /// Builds a synthetic signed image: 0x200-byte header, payload, then an optional
        /// protected TLV area followed by the unprotected one, mirroring imgtool's output.
        /// </summary>
        private static byte[] BuildSignedImage(
            int payloadSize,
            (ushort Type, byte[] Value)[] unprotectedTlvs,
            (ushort Type, byte[] Value)[] protectedTlvs = null)
        {
            byte[] protectedArea = protectedTlvs is null
                ? []
                : BuildTlvArea(TlvInfoMagicProtected, protectedTlvs);

            byte[] unprotectedArea = BuildTlvArea(TlvInfoMagic, unprotectedTlvs);

            byte[] image = new byte[ImageHeaderSize + payloadSize + protectedArea.Length + unprotectedArea.Length];

            Buffer.BlockCopy(BitConverter.GetBytes(ValidMagic), 0, image, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ImageHeaderSize), 0, image, 8, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)protectedArea.Length), 0, image, 10, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)payloadSize), 0, image, 12, 4);

            int offset = ImageHeaderSize + payloadSize;
            Buffer.BlockCopy(protectedArea, 0, image, offset, protectedArea.Length);
            offset += protectedArea.Length;
            Buffer.BlockCopy(unprotectedArea, 0, image, offset, unprotectedArea.Length);

            return image;
        }

        private static byte[] BuildTlvArea(ushort infoMagic, (ushort Type, byte[] Value)[] tlvs)
        {
            int total = 4;
            foreach ((ushort _, byte[] value) in tlvs)
            {
                total += 4 + value.Length;
            }

            byte[] area = new byte[total];
            Buffer.BlockCopy(BitConverter.GetBytes(infoMagic), 0, area, 0, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)total), 0, area, 2, 2);

            int offset = 4;
            foreach ((ushort type, byte[] value) in tlvs)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(type), 0, area, offset, 2);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)value.Length), 0, area, offset + 2, 2);
                Buffer.BlockCopy(value, 0, area, offset + 4, value.Length);
                offset += 4 + value.Length;
            }

            return area;
        }

        #endregion
    }
}
