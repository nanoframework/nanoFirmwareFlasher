// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Protocol-level tests ported from the Zephyr mcumgr_client test suite
    /// (tests/subsys/mgmt/mcumgr/mcumgr_client/src/main.c and its stubs).
    ///
    /// The Zephyr tests drive the C implementation through stub transports and verify
    /// both the outgoing CBOR payloads and the decoded responses.  These tests mirror
    /// that approach for the C# SMP client by:
    ///   • verifying CBOR request payloads built by the static encoder helpers, and
    ///   • verifying response decoding against CBOR payloads synthesised the same way
    ///     the Zephyr stubs (img_gr_stub.c, os_gr_stub.c) do.
    ///
    /// Test constants mirror the Zephyr stub values:
    ///   TEST_IMAGE_NUM  = 1
    ///   TEST_IMAGE_SIZE = 2048
    ///   TEST_SLOT_NUMBER = 2
    /// </summary>
    [TestClass]
    public class McumgrClientProtocolTests
    {
        // Mirrors Zephyr stub constants
        private const int TestImageNum  = 1;
        private const int TestImageSize = 2048;

        // -----------------------------------------------------------------------
        // img_upload_init_verify equivalents
        //
        // Zephyr's img_upload_init_verify() decodes the first upload chunk and
        // asserts: data present, off==0, image==TEST_IMAGE_NUM, len==TEST_IMAGE_SIZE,
        // sha (optional) matches the registered hash.
        // -----------------------------------------------------------------------

        [TestMethod]
        public void UploadFirstChunk_ImageField_MatchesSlotNumber()
        {
            // mirrors: zassert_equal(image, TEST_IMAGE_NUM, ...)
            byte[] data = new byte[64];
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset: 0, totalLen: TestImageSize, slot: TestImageNum, isFirst: true);

            uint image = ReadUIntField(cbor, "image");
            Assert.AreEqual((uint)TestImageNum, image, "image field must equal the slot number passed to EncodeUploadChunk");
        }

        [TestMethod]
        public void UploadFirstChunk_OffsetField_IsZero()
        {
            // mirrors: zassert offset == test_offset (== 0 at start)
            byte[] data = new byte[64];
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset: 0, totalLen: TestImageSize, slot: TestImageNum, isFirst: true);

            uint off = ReadUIntField(cbor, "off");
            Assert.AreEqual(0u, off, "off field must be 0 for the first chunk");
        }

        [TestMethod]
        public void UploadFirstChunk_LenField_EqualsTotalImageSize()
        {
            // mirrors: if (length != TEST_IMAGE_SIZE) → error
            byte[] data = new byte[64];
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset: 0, totalLen: TestImageSize, slot: TestImageNum, isFirst: true);

            uint len = ReadUIntField(cbor, "len");
            Assert.AreEqual((uint)TestImageSize, len, "len field must equal the total image size");
        }

        [TestMethod]
        public void UploadFirstChunk_DataField_ContainsChunkBytes()
        {
            // mirrors: zassert data.len != 0
            byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset: 0, totalLen: TestImageSize, slot: TestImageNum, isFirst: true);

            byte[] decoded = ReadByteStringField(cbor, "data");
            CollectionAssert.AreEqual(data, decoded, "data field must contain the original chunk bytes");
        }

        [TestMethod]
        public void UploadFirstChunk_WithSha_ShaFieldMatchesImageHash()
        {
            // mirrors: if (sha.len && memcmp(sha.value, image_hash_ptr, 32)) → error
            // The Zephyr stub sets image_hash_ptr[i] = i for i in 0..31.
            byte[] imageHash = new byte[32];
            for (int i = 0; i < 32; i++) imageHash[i] = (byte)i;

            byte[] data = new byte[64];
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset: 0, totalLen: TestImageSize, slot: TestImageNum, isFirst: true, sha: imageHash);

            byte[] shaField = ReadByteStringField(cbor, "sha");
            Assert.IsNotNull(shaField, "first chunk with SHA must encode the 'sha' field");
            CollectionAssert.AreEqual(imageHash, shaField, "sha field must contain the image hash byte-for-byte");
        }

        [TestMethod]
        public void UploadFirstChunk_WithSha_Sha256OfRealImageData_RoundTrips()
        {
            // Mirrors the test scenario where SHA256 is computed over the full image
            // and passed in the first chunk - the device verifies it matches.
            byte[] imageData = new byte[TestImageSize];
            for (int i = 0; i < imageData.Length; i++) imageData[i] = (byte)(i & 0xFF);

            byte[] sha;
            using (SHA256 h = SHA256.Create())
                sha = h.ComputeHash(imageData);

            byte[] chunk = new byte[64];
            Buffer.BlockCopy(imageData, 0, chunk, 0, chunk.Length);

            byte[] cbor = McumgrClient.EncodeUploadChunk(chunk, offset: 0, totalLen: imageData.Length, slot: TestImageNum, isFirst: true, sha: sha);

            byte[] shaField = ReadByteStringField(cbor, "sha");
            Assert.IsNotNull(shaField, "'sha' field must be present");
            CollectionAssert.AreEqual(sha, shaField, "sha field must carry the SHA-256 of the full image");
        }

        [TestMethod]
        public void UploadFirstChunk_WithoutSha_NoShaField()
        {
            // mirrors test_img_upload "without hash" branch:
            // img_mgmt_client_upload_init(&img_client, TEST_IMAGE_SIZE, TEST_IMAGE_NUM, NULL)
            byte[] data = new byte[64];
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset: 0, totalLen: TestImageSize, slot: TestImageNum, isFirst: true, sha: null);

            bool hasSha = HasField(cbor, "sha");
            Assert.IsFalse(hasSha, "first chunk without SHA must not encode a 'sha' field");
        }

        [TestMethod]
        public void UploadSubsequentChunk_OffsetAdvances_CorrectlyEncoded()
        {
            // mirrors: test_offset += data.len; img_upload_response(test_offset, EOK)
            // then next chunk arrives with off == test_offset
            byte[] data = new byte[] { 0x10, 0x20, 0x30, 0x40 };
            int offset = 1024; // simulates second chunk
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset: offset, totalLen: 0, slot: 0, isFirst: false);

            uint off = ReadUIntField(cbor, "off");
            Assert.AreEqual((uint)offset, off, "subsequent chunk must encode the correct byte offset");
        }

        [TestMethod]
        public void UploadSubsequentChunk_HasImageAndOffAndData_NotLenOrSha()
        {
            // newtmgr reference: ImageNum (image) has no omitempty, so it is sent on every
            // chunk. Continuation chunks must carry image + off + data, but NOT len or sha.
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[8], offset: 512, totalLen: 0, slot: 1, isFirst: false);

            Assert.IsFalse(HasField(cbor, "len"), "subsequent chunk must not have 'len' field");
            Assert.IsFalse(HasField(cbor, "sha"), "subsequent chunk must not have 'sha' field");
            Assert.IsTrue(HasField(cbor, "image"), "subsequent chunk must have 'image' field (no omitempty in reference)");
            Assert.IsTrue(HasField(cbor, "data"),  "subsequent chunk must have 'data' field");
            Assert.IsTrue(HasField(cbor, "off"),   "subsequent chunk must have 'off' field");
        }

        // -----------------------------------------------------------------------
        // img_upload_response / upload response parsing equivalents
        //
        // Zephyr's stub builds { "off": offset } or { "rc": status, "off": offset }
        // and the client decodes these to advance or stop the upload.
        // -----------------------------------------------------------------------

        [TestMethod]
        public void UploadResponse_RcOk_NoOffField_DecodeRcReturnsOk()
        {
            // mirrors: img_upload_response(offset, MGMT_ERR_EOK) with just { "off": offset }
            byte[] payload = EncodeSingleIntMap("off", 1024);
            SmpReturnCode rc = McumgrClient.DecodeRc(payload);
            Assert.AreEqual(SmpReturnCode.Ok, rc, "upload response without 'rc' field must decode as Ok");
        }

        [TestMethod]
        public void UploadResponse_RcEinval_DecodeRcReturnsInvalidValue()
        {
            // mirrors: img_upload_response(0, MGMT_ERR_EINVAL)
            // zassert_equal(MGMT_ERR_EINVAL, rc, ...)
            byte[] payload = EncodeUploadResponseWithError((long)SmpReturnCode.InvalidValue, offset: 0);
            SmpReturnCode rc = McumgrClient.DecodeRc(payload);
            Assert.AreEqual(SmpReturnCode.InvalidValue, rc, "rc field present with EINVAL must decode as InvalidValue");
        }

        [TestMethod]
        public void UploadResponse_ValidOffset_ParsedCorrectly()
        {
            // mirrors: zassert_equal(1024, response.image_upload_offset, ...)
            byte[] payload = EncodeSingleIntMap("off", 1024);
            bool found = McumgrClient.TryDecodeIntField(payload, "off", out int off);
            Assert.IsTrue(found, "off field must be present in the upload response");
            Assert.AreEqual(1024, off, "decoded offset must match the value in the response");
        }

        [TestMethod]
        public void UploadResponse_FinalChunk_OffsetEqualsTotalImageSize()
        {
            // mirrors: zassert_equal(TEST_IMAGE_SIZE, response.image_upload_offset, ...)
            byte[] payload = EncodeSingleIntMap("off", TestImageSize);
            McumgrClient.TryDecodeIntField(payload, "off", out int off);
            Assert.AreEqual(TestImageSize, off, "final response offset must equal total image size");
        }

        [TestMethod]
        public void UploadResponse_OffsetZeroAfterNonZero_DetectableAsRestartSignal()
        {
            // mirrors: the device restart-signal check:
            // if (hasOff && nextOff == 0 && offset > 0) → restart upload from 0
            byte[] payload = EncodeSingleIntMap("off", 0);
            bool found = McumgrClient.TryDecodeIntField(payload, "off", out int off);
            Assert.IsTrue(found, "TryDecodeIntField must return true for off=0 so the caller can detect the restart signal");
            Assert.AreEqual(0, off);
        }

        // -----------------------------------------------------------------------
        // img_read_response / DecodeImageList equivalents
        //
        // Zephyr's img_read_response() encodes the full image struct including all
        // boolean flags (bootable, pending, confirmed, active, permanent).
        // These tests use the same CBOR structure as img_gr_stub_data_init sets up.
        // -----------------------------------------------------------------------

        [TestMethod]
        public void ImageStateRead_OneImage_ParsesAllFlags()
        {
            // mirrors test_image_state_read: img_read_response(1)
            // zassert_equal(1, res_buf.image_list_length, ...)
            byte[] hash = new byte[32];
            for (int i = 0; i < 32; i++) hash[i] = (byte)(i + 32); // image_dummy_info[0].hash

            byte[] payload = EncodeImgReadResponse(new[]
            {
                new McumgrImageInfo
                {
                    Image = 0, Slot = 0, Version = "1.1.0",
                    Hash = hash,
                    Bootable  = true,
                    Pending   = false,
                    Confirmed = true,
                    Active    = true,
                    Permanent = false,
                },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.AreEqual(1, images.Count, "one image in response must produce list of length 1");
            Assert.IsTrue(images[0].Bootable,   "bootable flag must be decoded");
            Assert.IsFalse(images[0].Pending,   "pending flag must be decoded");
            Assert.IsTrue(images[0].Confirmed,  "confirmed flag must be decoded");
            Assert.IsTrue(images[0].Active,     "active flag must be decoded");
            Assert.IsFalse(images[0].Permanent, "permanent flag must be decoded");
        }

        [TestMethod]
        public void ImageStateRead_TwoImages_BothParsedWithCorrectSlots()
        {
            // mirrors test_image_state_read: img_read_response(2)
            // zassert_equal(2, res_buf.image_list_length, ...)
            byte[] hash0 = new byte[32];
            byte[] hash1 = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                hash0[i] = (byte)(i + 32); // image_dummy_info[0].hash[i] = i + 32
                hash1[i] = (byte)(i + 64); // image_dummy_info[1].hash[i] = i + 64
            }

            byte[] payload = EncodeImgReadResponse(new[]
            {
                new McumgrImageInfo
                {
                    Image = 0, Slot = 0, Version = "1.1.0",
                    Hash = hash0,
                    Bootable = true, Pending = false, Confirmed = true, Active = true, Permanent = false,
                },
                new McumgrImageInfo
                {
                    Image = 1, Slot = 1, Version = "1.1.1",
                    Hash = hash1,
                    Bootable = true, Pending = false, Confirmed = false, Active = false, Permanent = false,
                },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.AreEqual(2, images.Count, "two images in response must produce list of length 2");
            Assert.AreEqual(0, images[0].Slot);
            Assert.AreEqual(1, images[1].Slot);
            CollectionAssert.AreEqual(hash0, images[0].Hash, "primary slot hash must match");
            CollectionAssert.AreEqual(hash1, images[1].Hash, "secondary slot hash must match");
        }

        [TestMethod]
        public void ImageStateRead_SecondarySlot_InitiallyNotPending()
        {
            // mirrors test_image_state_set: zassert_equal(false, image_info[1].flags.pending, ...)
            byte[] payload = EncodeImgReadResponse(new[]
            {
                new McumgrImageInfo { Image = 0, Slot = 0, Version = "1.1.0", Hash = new byte[32], Bootable = true, Pending = false, Confirmed = true,  Active = true,  Permanent = false },
                new McumgrImageInfo { Image = 1, Slot = 1, Version = "1.1.1", Hash = new byte[32], Bootable = true, Pending = false, Confirmed = false, Active = false, Permanent = false },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.IsFalse(images[1].Pending, "secondary slot must not have pending flag set before state write");
        }

        [TestMethod]
        public void ImageStateRead_AfterSetPending_SecondarySlotIsPending()
        {
            // mirrors test_image_state_set: after img_mgmt_client_state_write with hash,
            // zassert_equal(true, image_info[1].flags.pending, ...)
            byte[] payload = EncodeImgReadResponse(new[]
            {
                new McumgrImageInfo { Image = 0, Slot = 0, Version = "1.1.0", Hash = new byte[32], Bootable = true, Pending = false, Confirmed = true,  Active = true,  Permanent = false },
                new McumgrImageInfo { Image = 1, Slot = 1, Version = "1.1.1", Hash = new byte[32], Bootable = true, Pending = true,  Confirmed = false, Active = false, Permanent = false },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.IsTrue(images[1].Pending, "after state write, secondary slot pending flag must be true");
        }

        [TestMethod]
        public void ImageStateRead_AfterConfirm_PrimarySlotIsConfirmed()
        {
            // mirrors test_image_state_set: after state_write with confirm=true,
            // zassert_equal(true, image_info[0].flags.confirmed, ...)
            byte[] payload = EncodeImgReadResponse(new[]
            {
                new McumgrImageInfo { Image = 0, Slot = 0, Version = "1.1.0", Hash = new byte[32], Bootable = true, Pending = false, Confirmed = true, Active = true, Permanent = false },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.IsTrue(images[0].Confirmed, "primary slot confirmed flag must be true after confirm state write");
        }

        [TestMethod]
        public void ImageStateRead_PermanentFlag_DetectedInSecondarySlot()
        {
            // mirrors Zephyr: image_dummy_info[1].flags.permanent = true (set in img_state_write_verify)
            byte[] payload = EncodeImgReadResponse(new[]
            {
                new McumgrImageInfo { Image = 1, Slot = 1, Version = "1.1.1", Hash = new byte[32], Bootable = true, Pending = false, Confirmed = false, Active = false, Permanent = true },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.IsTrue(images[0].Permanent, "permanent flag set by confirm+hash state write must be decoded");
        }

        [TestMethod]
        public void ImageStateRead_VersionString_MatchesZephyrFormat()
        {
            // mirrors img_gr_stub_data_init: snprintf(version, "1.1.%u", i)
            byte[] payload = EncodeImgReadResponse(new[]
            {
                new McumgrImageInfo { Image = 0, Slot = 0, Version = "1.1.0", Hash = new byte[32] },
                new McumgrImageInfo { Image = 0, Slot = 1, Version = "1.1.1", Hash = new byte[32] },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.AreEqual("1.1.0", images[0].Version);
            Assert.AreEqual("1.1.1", images[1].Version);
        }

        // -----------------------------------------------------------------------
        // img_erase_response / DecodeRc equivalents
        //
        // Zephyr's img_erase_response() encodes { "rc": status }.
        // -----------------------------------------------------------------------

        [TestMethod]
        public void EraseResponse_RcEok_DecodesAsOk()
        {
            // mirrors test_img_erase: img_erase_response(MGMT_ERR_EOK);
            // zassert_equal(MGMT_ERR_EOK, rc, ...)
            byte[] payload = EncodeSingleIntMap("rc", 0);
            Assert.AreEqual(SmpReturnCode.Ok, McumgrClient.DecodeRc(payload));
        }

        [TestMethod]
        public void EraseResponse_RcEinval_DecodesAsInvalidValue()
        {
            // mirrors test_img_erase: img_erase_response(MGMT_ERR_EINVAL);
            // zassert_equal(MGMT_ERR_EINVAL, rc, ...)
            byte[] payload = EncodeSingleIntMap("rc", (long)SmpReturnCode.InvalidValue);
            Assert.AreEqual(SmpReturnCode.InvalidValue, McumgrClient.DecodeRc(payload));
        }

        // -----------------------------------------------------------------------
        // os_echo_verify / OS echo protocol equivalents
        //
        // Zephyr's os_echo_verify() decodes { "d": echo_string } from the request
        // and os_echo_response() encodes { "r": echo_string } in the response.
        // -----------------------------------------------------------------------

        [TestMethod]
        public void OsEchoRequest_DField_ContainsEchoString()
        {
            // mirrors os_echo_verify: zcbor_map_decode "d" and compare to os_echo_test
            const string echoText = "TestString"; // same as Zephyr's os_echo_test
            byte[] cbor = McumgrClient.EncodeMap1("d", echoText);

            string decoded = McumgrClient.DecodeStringField(cbor, "d");
            Assert.AreEqual(echoText, decoded, "'d' field in echo request must contain the echo text");
        }

        [TestMethod]
        public void OsEchoResponse_RField_ParsedCorrectly()
        {
            // mirrors os_echo_response: { "r": echo_data } → client reads it back
            const string echoText = "TestString";
            byte[] response = McumgrClient.EncodeMap1("r", echoText);

            string result = McumgrClient.DecodeStringField(response, "r");
            Assert.AreEqual(echoText, result, "'r' field in echo response must contain the echoed text");
        }

        [TestMethod]
        public void OsEchoResponse_EmptyPayload_NullReturned()
        {
            // mirrors timeout scenario: no response → null
            string result = McumgrClient.DecodeStringField(Array.Empty<byte>(), "r");
            Assert.IsNull(result, "empty echo response must return null");
        }

        [TestMethod]
        public void OsEchoRequest_VariousStrings_RoundTrip()
        {
            // Additional coverage: different echo strings encode/decode correctly
            foreach (string text in new[] { "", "a", "Hello, World!", "nano\x00Framework" })
            {
                byte[] cbor = McumgrClient.EncodeMap1("d", text);
                string decoded = McumgrClient.DecodeStringField(cbor, "d");
                Assert.AreEqual(text, decoded, $"echo text '{text}' must round-trip through 'd' field");
            }
        }

        [TestMethod]
        public void OsEcho_RequestAndResponse_FullRoundTrip()
        {
            // Simulates the full echo flow: encode request → device echoes → decode response.
            // The device copies the "d" value into "r" in its response (os_echo_response).
            const string echoText = "TestString";

            byte[] request = McumgrClient.EncodeMap1("d", echoText);
            string sent = McumgrClient.DecodeStringField(request, "d");

            // device response: { "r": sent }
            byte[] response = McumgrClient.EncodeMap1("r", sent);
            string echoed = McumgrClient.DecodeStringField(response, "r");

            Assert.AreEqual(echoText, echoed, "echoed 'r' value must match the originally sent 'd' value");
        }

        [TestMethod]
        public void OsEcho_RequestEncodesOnlyDField()
        {
            // The echo request must contain exactly the "d" key and nothing else.
            byte[] cbor = McumgrClient.EncodeMap1("d", "hello");

            Assert.IsTrue(HasField(cbor,  "d"), "echo request must have 'd' field");
            Assert.IsFalse(HasField(cbor, "r"), "echo request must not have 'r' field");
        }

        [TestMethod]
        public void OsEchoResponse_MissingRField_ReturnsNull()
        {
            // A response that has an unrelated field must not throw and must return null.
            byte[] response = McumgrClient.EncodeMap1("rc", "0");
            string result = McumgrClient.DecodeStringField(response, "r");
            Assert.IsNull(result, "response without 'r' field must return null");
        }

        [TestMethod]
        public void OsParamsRequest_IsEmptyMap()
        {
            // The MCUmgr parameters request carries no fields.
            byte[] cbor = McumgrClient.EncodeEmptyMap();

            Assert.IsFalse(HasField(cbor, "buf_size"), "parameters request must be an empty map");
            Assert.IsFalse(HasField(cbor, "buf_count"), "parameters request must be an empty map");
        }

        [TestMethod]
        public void OsParamsResponse_BufSizeAndCount_Decoded()
        {
            // mirrors os_mgmt_params response: { "buf_size": 2475, "buf_count": 4 }
            byte[] payload = EncodeParametersResponse(bufSize: 2475, bufCount: 4);

            McumgrParameters parameters = McumgrClient.DecodeParameters(payload);

            Assert.AreEqual(2475, parameters.BufSize, "buf_size field must be decoded");
            Assert.AreEqual(4, parameters.BufCount, "buf_count field must be decoded");
        }

        [TestMethod]
        public void OsParamsResponse_EmptyPayload_ReturnsZeroes()
        {
            // unsupported / no-response scenario must not throw and must leave BufSize == 0
            McumgrParameters parameters = McumgrClient.DecodeParameters(Array.Empty<byte>());

            Assert.AreEqual(0, parameters.BufSize, "empty parameters response must decode buf_size as 0");
            Assert.AreEqual(0, parameters.BufCount, "empty parameters response must decode buf_count as 0");
        }

        [TestMethod]
        public void OsParamsResponse_MissingFields_LeavesDefaults()
        {
            // a response carrying an unrelated field must not throw and must leave defaults
            byte[] payload = EncodeSingleIntMap("rc", 0);

            McumgrParameters parameters = McumgrClient.DecodeParameters(payload);

            Assert.AreEqual(0, parameters.BufSize);
            Assert.AreEqual(0, parameters.BufCount);
        }

        [TestMethod]
        public void OsParamsResponse_BufSizeAndCount_IsSupported()
        {
            byte[] payload = EncodeParametersResponse(bufSize: 512, bufCount: 1);

            McumgrParameters parameters = McumgrClient.DecodeParameters(payload);

            Assert.IsTrue(parameters.Supported, "a parameters reply without an error code means the command is supported");
        }

        [TestMethod]
        public void DeviceInfoResponse_FromMcubootSerialRecovery_Decoded()
        {
            // mirrors nf-interpreter MCUboot/common/mcuboot_serial_extensions.c
            var w = new CborWriter();
            w.WriteStartMap(3);
            w.WriteTextString("target");
            w.WriteTextString("ORGPAL_PALTHREE");
            w.WriteTextString("mcuboot_ver");
            w.WriteTextString("2.5.0-rc1");
            w.WriteTextString("nanomcuboot_ver");
            w.WriteTextString("1.0.0.0");
            w.WriteEndMap();

            McumgrDeviceInfo info = McumgrClient.DecodeDeviceInfo(w.Encode());

            Assert.AreEqual("ORGPAL_PALTHREE", info.TargetName);
            Assert.AreEqual("2.5.0-rc1", info.McubootVersion);
            Assert.AreEqual("1.0.0.0", info.NanoMcubootVersion);
            Assert.IsNull(info.ClrVersion, "the bootloader doesn't report a CLR version");
        }

        [TestMethod]
        public void DeviceInfoResponse_RcNotSupported_LeavesDefaults()
        {
            // older bootloaders reply { "rc": 8 } to the nanoFramework group
            byte[] payload = EncodeSingleIntMap("rc", (int)SmpReturnCode.NotSupported);

            McumgrDeviceInfo info = McumgrClient.DecodeDeviceInfo(payload);

            Assert.IsNull(info.TargetName);
            Assert.IsNull(info.McubootVersion);
            Assert.IsNull(info.NanoMcubootVersion);
        }

        [TestMethod]
        public void OsParamsResponse_RcNotSupported_IsNotSupported()
        {
            // MCUboot serial recovery built without MCUBOOT_BOOT_MGMT_MCUMGR_PARAMS replies { "rc": 8 }
            byte[] payload = EncodeSingleIntMap("rc", (int)SmpReturnCode.NotSupported);

            McumgrParameters parameters = McumgrClient.DecodeParameters(payload);

            Assert.IsFalse(parameters.Supported, "rc=ENOTSUP must flag the command as unsupported");
            Assert.AreEqual(0, parameters.BufSize);
            Assert.AreEqual(0, parameters.BufCount);
        }

        [TestMethod]
        public void CalculateChunkSize_TypicalBuffer_FitsWithinDeviceBuffer()
        {
            // For a 512-byte buffer the chunk plus all SMP/CBOR/framing overhead must fit.
            int chunk = McumgrClient.CalculateChunkSize(512);

            // 512 - 8 (header) - 4 (length+CRC) - 33 (first-chunk CBOR overhead) = 467 → 464 aligned
            Assert.AreEqual(464, chunk, "chunk size must be the aligned remainder of the device buffer");
            Assert.IsTrue(chunk + 8 + 4 + 33 <= 512, "encoded first chunk must fit within the device buffer");
        }

        [TestMethod]
        public void CalculateChunkSize_IsFlashWriteAligned()
        {
            // Chunk size must always be a multiple of the 4-byte flash-write alignment.
            foreach (int bufSize in new[] { 256, 384, 512, 1024, 2475 })
            {
                int chunk = McumgrClient.CalculateChunkSize(bufSize);
                Assert.AreEqual(0, chunk % 4, $"chunk size for buf_size {bufSize} must be 4-byte aligned");
            }
        }

        [TestMethod]
        public void CalculateChunkSize_LargerBuffer_ProducesLargerChunk()
        {
            // A bigger device buffer must allow a bigger chunk (so we adapt to the server).
            int small = McumgrClient.CalculateChunkSize(256);
            int large = McumgrClient.CalculateChunkSize(1024);

            Assert.IsTrue(large > small, "a larger device buffer must yield a larger chunk size");
        }

        [TestMethod]
        public void CalculateChunkSize_ImplausiblySmallBuffer_FallsBackToAlignment()
        {
            // A buffer too small to hold any overhead must not produce a negative/zero chunk.
            int chunk = McumgrClient.CalculateChunkSize(8);

            Assert.AreEqual(4, chunk, "an implausibly small buffer must fall back to a single aligned unit");
        }

        // -----------------------------------------------------------------------
        // os_reset_response equivalents
        //
        // Zephyr's os_reset_response() sets nb->len = 0 (empty payload).
        // The C# client ignores any timeout after sending a reset.
        // -----------------------------------------------------------------------

        [TestMethod]
        public void OsResetResponse_EmptyPayload_DecodesAsOk()
        {
            // mirrors os_reset_response: nb->len = 0 → DecodeRc(empty) must be Ok
            SmpReturnCode rc = McumgrClient.DecodeRc(Array.Empty<byte>());
            Assert.AreEqual(SmpReturnCode.Ok, rc, "empty reset response must decode as Ok");
        }

        [TestMethod]
        public void OsResetResponse_NullPayload_DecodesAsOk()
        {
            // mirrors timeout/no-response scenario
            SmpReturnCode rc = McumgrClient.DecodeRc(null);
            Assert.AreEqual(SmpReturnCode.Ok, rc, "null reset response must decode as Ok");
        }

        // -----------------------------------------------------------------------
        // SMP v2 error format (group-specific errors)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void UploadResponse_SmpV2ErrFormat_ExtractsRcFromErrMap()
        {
            // mirrors img_fail_response patterns but using SMP v2 err map
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("err");
            w.WriteStartMap(2);
            w.WriteTextString("group"); w.WriteInt64(1);
            w.WriteTextString("rc");    w.WriteInt64((long)SmpReturnCode.InvalidValue);
            w.WriteEndMap();
            w.WriteEndMap();

            Assert.AreEqual(SmpReturnCode.InvalidValue, McumgrClient.DecodeRc(w.Encode()));
        }

        // -----------------------------------------------------------------------
        // Helpers - mirror the Zephyr stub encoding functions
        // -----------------------------------------------------------------------

        /// <summary>
        /// Encodes an image list response matching the format produced by
        /// Zephyr's img_read_response() in img_gr_stub.c.
        /// </summary>
        private static byte[] EncodeImgReadResponse(McumgrImageInfo[] images)
        {
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("images");
            w.WriteStartArray(images.Length);

            foreach (McumgrImageInfo img in images)
            {
                w.WriteStartMap(9);
                w.WriteTextString("image");     w.WriteInt64(img.Image);
                w.WriteTextString("slot");      w.WriteInt64(img.Slot);
                w.WriteTextString("version");   w.WriteTextString(img.Version ?? "0.0.0");
                w.WriteTextString("hash");      w.WriteByteString(img.Hash ?? Array.Empty<byte>());
                w.WriteTextString("bootable");  w.WriteBoolean(img.Bootable);
                w.WriteTextString("pending");   w.WriteBoolean(img.Pending);
                w.WriteTextString("confirmed"); w.WriteBoolean(img.Confirmed);
                w.WriteTextString("active");    w.WriteBoolean(img.Active);
                w.WriteTextString("permanent"); w.WriteBoolean(img.Permanent);
                w.WriteEndMap();
            }

            w.WriteEndArray();
            w.WriteEndMap();
            return w.Encode();
        }

        /// <summary>
        /// Encodes an upload error response: { "rc": status, "off": offset }
        /// Mirrors Zephyr's img_upload_response() when status != 0.
        /// </summary>
        private static byte[] EncodeUploadResponseWithError(long status, long offset)
        {
            var w = new CborWriter();
            w.WriteStartMap(2);
            w.WriteTextString("rc");  w.WriteInt64(status);
            w.WriteTextString("off"); w.WriteInt64(offset);
            w.WriteEndMap();
            return w.Encode();
        }

        /// <summary>
        /// Encodes an MCUmgr parameters response: { "buf_size": bufSize, "buf_count": bufCount }
        /// matching the format produced by Zephyr's os_mgmt MCUmgr Parameters handler.
        /// </summary>
        private static byte[] EncodeParametersResponse(long bufSize, long bufCount)
        {
            var w = new CborWriter();
            w.WriteStartMap(2);
            w.WriteTextString("buf_size");  w.WriteInt64(bufSize);
            w.WriteTextString("buf_count"); w.WriteInt64(bufCount);
            w.WriteEndMap();
            return w.Encode();
        }

        private static byte[] EncodeSingleIntMap(string key, long value)
        {
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString(key);
            w.WriteInt64(value);
            w.WriteEndMap();
            return w.Encode();
        }

        private static uint ReadUIntField(byte[] cbor, string field)
        {
            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                if (key == field) return r.ReadUInt32();
                r.SkipValue();
            }
            throw new InvalidOperationException($"Field '{field}' not found in CBOR map.");
        }

        private static byte[] ReadByteStringField(byte[] cbor, string field)
        {
            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                if (key == field) return r.ReadByteString();
                r.SkipValue();
            }
            return null;
        }

        private static bool HasField(byte[] cbor, string field)
        {
            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                if (key == field) return true;
                r.SkipValue();
            }
            return false;
        }
    }
}
