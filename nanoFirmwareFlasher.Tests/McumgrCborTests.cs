// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Unit tests for McumgrClient CBOR encoding/decoding helpers.
    /// Test patterns are drawn from the Zephyr zcbor_bulk test suite
    /// (tests/subsys/mgmt/mcumgr/zcbor_bulk/src/main.c) and adapted to
    /// the C# SMP client's graceful-degradation policy.
    /// </summary>
    [TestClass]
    public class McumgrCborTests
    {
        [TestMethod]
        public void EncodeUploadChunk_FirstChunk_ContainsAllFourFields()
        {
            byte[] data     = new byte[] { 0x01, 0x02, 0x03 };
            int    offset   = 0;
            int    totalLen = 1024;
            int    slot     = 0;

            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset, totalLen, slot, isFirst: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            var fields = new Dictionary<string, object>();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                switch (key)
                {
                    case "data":  fields["data"]  = r.ReadByteString(); break;
                    case "off":   fields["off"]   = (int)r.ReadUInt32(); break;
                    case "len":   fields["len"]   = (int)r.ReadUInt32(); break;
                    case "image": fields["image"] = (int)r.ReadUInt32(); break;
                    default:      r.SkipValue(); break;
                }
            }
            r.ReadEndMap();

            Assert.IsTrue(fields.ContainsKey("data"),  "first chunk must have 'data' field");
            Assert.IsTrue(fields.ContainsKey("off"),   "first chunk must have 'off' field");
            Assert.IsTrue(fields.ContainsKey("len"),   "first chunk must have 'len' field");
            Assert.IsTrue(fields.ContainsKey("image"), "first chunk must have 'image' field");

            CollectionAssert.AreEqual(data, (byte[])fields["data"]);
            Assert.AreEqual(offset,   (int)fields["off"]);
            Assert.AreEqual(totalLen, (int)fields["len"]);
            Assert.AreEqual(slot,     (int)fields["image"]);
        }

        [TestMethod]
        public void EncodeUploadChunk_SubsequentChunk_ContainsImageOffAndData()
        {
            // newtmgr reference: ImageNum has no omitempty → sent on every chunk.
            byte[] data   = new byte[] { 0x10, 0x20, 0x30 };
            int    offset = 128;
            int    slot   = 1;

            byte[] cbor = McumgrClient.EncodeUploadChunk(data, offset, 0, slot, isFirst: false);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            var fields = new Dictionary<string, object>();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                switch (key)
                {
                    case "data":  fields["data"]  = r.ReadByteString(); break;
                    case "off":   fields["off"]   = (int)r.ReadUInt32(); break;
                    case "image": fields["image"] = (int)r.ReadUInt32(); break;
                    default:      r.SkipValue(); break;
                }
            }
            r.ReadEndMap();

            Assert.IsTrue(fields.ContainsKey("data"),  "subsequent chunk must have 'data' field");
            Assert.IsTrue(fields.ContainsKey("off"),   "subsequent chunk must have 'off' field");
            Assert.IsTrue(fields.ContainsKey("image"), "subsequent chunk must have 'image' field (no omitempty in reference)");
            Assert.AreEqual(3, fields.Count, "subsequent chunk must have exactly 3 fields");

            CollectionAssert.AreEqual(data, (byte[])fields["data"]);
            Assert.AreEqual(offset, (int)fields["off"]);
            Assert.AreEqual(slot,   (int)fields["image"]);
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunk_HasExactlyFourFields()
        {
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0xFF }, 0, 256, 1, isFirst: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            int? count = r.ReadStartMap();
            Assert.AreEqual(4, count, "first chunk without SHA must declare 4 fields");
        }

        [TestMethod]
        public void EncodeUploadChunk_SubsequentChunk_HasExactlyThreeFields()
        {
            // After the fix: subsequent chunks carry image + off + data (3 fields).
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0xAB }, 64, 0, 0, isFirst: false);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            int? count = r.ReadStartMap();
            Assert.AreEqual(3, count, "subsequent chunk must declare exactly 3 fields: image, off, data");
        }

        [TestMethod]
        public void EncodeUploadChunk_SlotOne_ImageFieldIsOne()
        {
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0x00 }, 0, 512, slot: 1, isFirst: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            uint imageVal = 0;
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                if (key == "image") imageVal = r.ReadUInt32();
                else                r.SkipValue();
            }
            Assert.AreEqual(1u, imageVal, "slot 1 must encode image=1");
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunkWithSha_HasFiveFields()
        {
            byte[] sha = new byte[32];
            for (int i = 0; i < 32; i++) sha[i] = (byte)i;

            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0xFF }, 0, 256, 0, isFirst: true, sha: sha);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            int? count = r.ReadStartMap();
            Assert.AreEqual(5, count, "first chunk with SHA must declare 5 fields");
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunkWithSha_ShaFieldIsByteString()
        {
            byte[] sha = new byte[32];
            for (int i = 0; i < 32; i++) sha[i] = (byte)(i * 5 + 1);

            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0x01 }, 0, 100, 0, isFirst: true, sha: sha);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            byte[]? decoded = null;
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                if (key == "sha") decoded = r.ReadByteString();
                else              r.SkipValue();
            }

            Assert.IsNotNull(decoded, "'sha' field must be present");
            CollectionAssert.AreEqual(sha, decoded, "'sha' must round-trip as byte string");
        }

        [TestMethod]
        public void EncodeUploadChunk_NumericFields_EncodedAsUnsignedCbor()
        {
            // ReadUInt32 throws on CBOR major type 1 (negative), so success proves unsigned encoding
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0xAB }, offset: 128, totalLen: 1024, slot: 1, isFirst: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            uint? off = null, len = null, image = null;
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                switch (key)
                {
                    case "off":   off   = r.ReadUInt32(); break;
                    case "len":   len   = r.ReadUInt32(); break;
                    case "image": image = r.ReadUInt32(); break;
                    default:      r.SkipValue();          break;
                }
            }

            Assert.AreEqual(128u,  off,   "'off' must be encoded as uint");
            Assert.AreEqual(1024u, len,   "'len' must be encoded as uint");
            Assert.AreEqual(1u,    image, "'image' must be encoded as uint");
        }

        [TestMethod]
        public void DecodeRc_OkResponse_ReturnsZero()
        {
            byte[] payload = EncodeSingleIntMap("rc", 0);
            SmpReturnCode rc = McumgrClient.DecodeRc(payload);
            Assert.AreEqual(SmpReturnCode.Ok, rc);
        }

        [TestMethod]
        public void DecodeRc_NonZeroErrorCode_ReturnsCorrectValue()
        {
            byte[] payload = EncodeSingleIntMap("rc", (long)SmpReturnCode.InvalidValue);
            SmpReturnCode rc = McumgrClient.DecodeRc(payload);
            Assert.AreEqual(SmpReturnCode.InvalidValue, rc);
        }

        [TestMethod]
        public void DecodeRc_EmptyPayload_ReturnsOk()
        {
            SmpReturnCode rc = McumgrClient.DecodeRc(Array.Empty<byte>());
            Assert.AreEqual(SmpReturnCode.Ok, rc, "empty payload treated as success");
        }

        [TestMethod]
        public void DecodeRc_NullPayload_ReturnsOk()
        {
            SmpReturnCode rc = McumgrClient.DecodeRc(null);
            Assert.AreEqual(SmpReturnCode.Ok, rc, "null payload treated as success");
        }

        [TestMethod]
        public void DecodeRc_PayloadWithOtherFields_ExtractsRcCorrectly()
        {
            // { "off": 128, "rc": 3 }
            var w = new CborWriter();
            w.WriteStartMap(2);
            w.WriteTextString("off"); w.WriteInt64(128);
            w.WriteTextString("rc");  w.WriteInt64(3);
            w.WriteEndMap();

            SmpReturnCode rc = McumgrClient.DecodeRc(w.Encode());
            Assert.AreEqual(SmpReturnCode.InvalidValue, rc);
        }

        [TestMethod]
        public void DecodeRc_SmpV2ErrFormat_ReturnsErrGroupRc()
        {
            // Mirrors Zephyr test_map_in_map_correct: nested map { "err": { "group": 1, "rc": 6 } }
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("err");
            w.WriteStartMap(2);
            w.WriteTextString("group"); w.WriteInt64(1);
            w.WriteTextString("rc");    w.WriteInt64((int)SmpReturnCode.BadState);
            w.WriteEndMap();
            w.WriteEndMap();

            SmpReturnCode rc = McumgrClient.DecodeRc(w.Encode());
            Assert.AreEqual(SmpReturnCode.BadState, rc);
        }

        [TestMethod]
        public void DecodeIntField_OffField_ReturnsCorrectOffset()
        {
            // Typical upload response: { "rc": 0, "off": 256 }
            var w = new CborWriter();
            w.WriteStartMap(2);
            w.WriteTextString("rc");  w.WriteInt64(0);
            w.WriteTextString("off"); w.WriteInt64(256);
            w.WriteEndMap();

            int off = McumgrClient.DecodeIntField(w.Encode(), "off");
            Assert.AreEqual(256, off);
        }

        [TestMethod]
        public void DecodeIntField_FieldAbsent_ReturnsZero()
        {
            byte[] payload = EncodeSingleIntMap("rc", 0);
            int off = McumgrClient.DecodeIntField(payload, "off");
            Assert.AreEqual(0, off, "missing field returns 0");
        }

        [TestMethod]
        public void TryDecodeIntField_FieldPresent_ReturnsTrueAndValue()
        {
            byte[] payload = EncodeSingleIntMap("off", 512);
            bool found = McumgrClient.TryDecodeIntField(payload, "off", out int value);
            Assert.IsTrue(found);
            Assert.AreEqual(512, value);
        }

        [TestMethod]
        public void TryDecodeIntField_FieldAbsent_ReturnsFalse()
        {
            byte[] payload = EncodeSingleIntMap("rc", 0);
            bool found = McumgrClient.TryDecodeIntField(payload, "off", out int value);
            Assert.IsFalse(found);
            Assert.AreEqual(0, value);
        }

        [TestMethod]
        public void TryDecodeIntField_FieldPresentWithZeroValue_ReturnsTrueDistinguishingFromAbsent()
        {
            // Zero value must return true - the device restart-signal (off=0) must be detectable
            byte[] payload = EncodeSingleIntMap("off", 0);
            bool found = McumgrClient.TryDecodeIntField(payload, "off", out int value);
            Assert.IsTrue(found,  "field with value 0 must return true, not be treated as absent");
            Assert.AreEqual(0, value);
        }

        [TestMethod]
        public void TryDecodeIntField_NullPayload_ReturnsFalse()
        {
            Assert.IsFalse(McumgrClient.TryDecodeIntField(null, "off", out _));
        }

        [TestMethod]
        public void TryDecodeIntField_EmptyPayload_ReturnsFalse()
        {
            Assert.IsFalse(McumgrClient.TryDecodeIntField(Array.Empty<byte>(), "off", out _));
        }

        [TestMethod]
        public void DecodeImageList_TwoImages_ParsesAllFields()
        {
            byte[] hash0 = new byte[32];
            byte[] hash1 = new byte[32];
            for (int i = 0; i < 32; i++) hash1[i] = (byte)i;

            byte[] payload = EncodeImageListPayload(new[]
            {
                new McumgrImageInfo { Image = 0, Slot = 0, Version = "1.0.0.0", Hash = hash0, Active = true,  Confirmed = true,  Pending = false, Bootable = true },
                new McumgrImageInfo { Image = 0, Slot = 1, Version = "1.1.0.0", Hash = hash1, Active = false, Confirmed = false, Pending = true,  Bootable = true },
            });

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(payload);

            Assert.AreEqual(2, images.Count);

            Assert.AreEqual(0,         images[0].Image);
            Assert.AreEqual(0,         images[0].Slot);
            Assert.AreEqual("1.0.0.0", images[0].Version);
            CollectionAssert.AreEqual(hash0, images[0].Hash);
            Assert.IsTrue(images[0].Active);
            Assert.IsTrue(images[0].Confirmed);
            Assert.IsFalse(images[0].Pending);

            Assert.AreEqual(1,         images[1].Slot);
            Assert.AreEqual("1.1.0.0", images[1].Version);
            CollectionAssert.AreEqual(hash1, images[1].Hash);
            Assert.IsFalse(images[1].Active);
            Assert.IsTrue(images[1].Pending);
        }

        [TestMethod]
        public void DecodeImageList_EmptyPayload_ReturnsEmptyList()
        {
            Assert.AreEqual(0, McumgrClient.DecodeImageList(Array.Empty<byte>()).Count);
        }

        [TestMethod]
        public void DecodeImageList_NullPayload_ReturnsEmptyList()
        {
            Assert.AreEqual(0, McumgrClient.DecodeImageList(null).Count);
        }

        [TestMethod]
        public void DecodeImageList_FieldsInAnyOrder_ParsesCorrectly()
        {
            // Mirrors Zephyr test_correct_out_of_order: field order in the CBOR map must not matter
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("images");
            w.WriteStartArray(1);
            w.WriteStartMap(4);
            w.WriteTextString("confirmed"); w.WriteBoolean(true);     // order differs from decoder
            w.WriteTextString("version");   w.WriteTextString("2.0.0.0");
            w.WriteTextString("active");    w.WriteBoolean(true);
            w.WriteTextString("slot");      w.WriteInt64(0);
            w.WriteEndMap();
            w.WriteEndArray();
            w.WriteEndMap();

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(w.Encode());

            Assert.AreEqual(1, images.Count);
            Assert.AreEqual("2.0.0.0", images[0].Version);
            Assert.IsTrue(images[0].Active);
            Assert.IsTrue(images[0].Confirmed);
            Assert.AreEqual(0, images[0].Slot);
        }

        [TestMethod]
        public void DecodeImageList_UnknownFieldsPresent_SkippedAndKnownFieldsParsed()
        {
            // Unknown keys must be silently skipped (SkipValue) without breaking the rest
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("images");
            w.WriteStartArray(1);
            w.WriteStartMap(4);
            w.WriteTextString("slot");      w.WriteInt64(1);
            w.WriteTextString("unknown_a"); w.WriteTextString("ignored");
            w.WriteTextString("version");   w.WriteTextString("3.1.0.0");
            w.WriteTextString("unknown_b"); w.WriteInt64(99);
            w.WriteEndMap();
            w.WriteEndArray();
            w.WriteEndMap();

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(w.Encode());

            Assert.AreEqual(1, images.Count);
            Assert.AreEqual(1, images[0].Slot);
            Assert.AreEqual("3.1.0.0", images[0].Version);
        }

        [TestMethod]
        public void DecodeImageList_PermanentField_ParsedCorrectly()
        {
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("images");
            w.WriteStartArray(1);
            w.WriteStartMap(3);
            w.WriteTextString("slot");      w.WriteInt64(0);
            w.WriteTextString("version");   w.WriteTextString("1.0.0.0");
            w.WriteTextString("permanent"); w.WriteBoolean(true);
            w.WriteEndMap();
            w.WriteEndArray();
            w.WriteEndMap();

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(w.Encode());

            Assert.AreEqual(1, images.Count);
            Assert.IsTrue(images[0].Permanent);
        }

        [TestMethod]
        public void DecodeImageList_WrongCborTypeForField_GracefulDegradation()
        {
            // Mirrors Zephyr test_bad_type_encoded: Zephyr returns an error; our policy is
            // to catch the exception and return whatever was successfully decoded before failure.
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("images");
            w.WriteStartArray(1);
            w.WriteStartMap(2);
            w.WriteTextString("slot");    w.WriteTextString("not-an-int");  // wrong type for uint
            w.WriteTextString("version"); w.WriteTextString("1.0.0.0");
            w.WriteEndMap();
            w.WriteEndArray();
            w.WriteEndMap();

            // Must not throw
            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(w.Encode());
            Assert.IsTrue(images.Count >= 0);
        }

        [TestMethod]
        public void DecodeImageList_PayloadIsArrayNotMap_ReturnsEmptyList()
        {
            // Mirrors Zephyr test_not_map: Zephyr returns -EBADMSG; our policy returns empty list
            var w = new CborWriter();
            w.WriteStartArray(2);
            w.WriteTextString("hello");
            w.WriteTextString("world");
            w.WriteEndArray();

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(w.Encode());
            Assert.AreEqual(0, images.Count, "non-map root must return empty list");
        }

        [TestMethod]
        public void DecodeImageList_DuplicateField_LastValueWins()
        {
            // Mirrors Zephyr test_duplicate. Zephyr treats duplicates as an error;
            // our decoder overwrites, so the last occurrence wins.
            var w = new CborWriter(CborConformanceMode.Lax);
            w.WriteStartMap(1);
            w.WriteTextString("images");
            w.WriteStartArray(1);
            w.WriteStartMap(2);
            w.WriteTextString("slot"); w.WriteInt64(0);
            w.WriteTextString("slot"); w.WriteInt64(1);  // duplicate key
            w.WriteEndMap();
            w.WriteEndArray();
            w.WriteEndMap();

            List<McumgrImageInfo> images = McumgrClient.DecodeImageList(w.Encode());

            Assert.AreEqual(1, images.Count);
            Assert.AreEqual(1, images[0].Slot, "last value of duplicate key must win");
        }

        [TestMethod]
        public void DecodeStringField_ReturnsCorrectString()
        {
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("r");
            w.WriteTextString("nanoFramework");
            w.WriteEndMap();

            string result = McumgrClient.DecodeStringField(w.Encode(), "r");
            Assert.AreEqual("nanoFramework", result);
        }

        [TestMethod]
        public void DecodeStringField_MissingField_ReturnsNull()
        {
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("other"); w.WriteTextString("value");
            w.WriteEndMap();

            string result = McumgrClient.DecodeStringField(w.Encode(), "r");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void EncodeMap1_StringKeyValue_RoundTrips()
        {
            byte[] cbor = McumgrClient.EncodeMap1("d", "hello");

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            string key = r.ReadTextString();
            string val = r.ReadTextString();
            r.ReadEndMap();

            Assert.AreEqual("d", key);
            Assert.AreEqual("hello", val);
        }

        [TestMethod]
        public void EncodeEmptyMap_ProducesOneByteCborEmptyMap()
        {
            byte[] cbor = McumgrClient.EncodeEmptyMap();
            Assert.AreEqual(1, cbor.Length);
            Assert.AreEqual(0xA0, cbor[0]);  // CBOR empty map = 0xA0
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunk_FirstFieldIsImage()
        {
            // newtmgr field order: ImageNum (image), Off, Len, DataSha, Data.
            // Our encoder must place "image" first to match the reference.
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0x01 }, 0, 512, slot: 1, isFirst: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            string firstKey = r.ReadTextString();
            Assert.AreEqual("image", firstKey, "first field in upload chunk must be 'image' (matches newtmgr field order)");
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunk_SecondFieldIsOff()
        {
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0x01 }, 0, 512, slot: 0, isFirst: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            r.ReadTextString(); r.SkipValue(); // skip image
            string secondKey = r.ReadTextString();
            Assert.AreEqual("off", secondKey, "second field must be 'off'");
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunk_ThirdFieldIsLen()
        {
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0x01 }, 0, 512, slot: 0, isFirst: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            r.ReadTextString(); r.SkipValue(); // image
            r.ReadTextString(); r.SkipValue(); // off
            string thirdKey = r.ReadTextString();
            Assert.AreEqual("len", thirdKey, "third field must be 'len'");
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunkWithSha_FourthFieldIsSha()
        {
            byte[] sha = new byte[32];
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0x01 }, 0, 512, slot: 0, isFirst: true, sha: sha);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            r.ReadTextString(); r.SkipValue(); // image
            r.ReadTextString(); r.SkipValue(); // off
            r.ReadTextString(); r.SkipValue(); // len
            string fourthKey = r.ReadTextString();
            Assert.AreEqual("sha", fourthKey, "fourth field must be 'sha' when SHA is present");
        }

        [TestMethod]
        public void EncodeUploadChunk_FirstChunkWithSha_LastFieldIsData()
        {
            byte[] sha = new byte[32];
            byte[] data = new byte[] { 0xAB, 0xCD };
            byte[] cbor = McumgrClient.EncodeUploadChunk(data, 0, 512, slot: 0, isFirst: true, sha: sha);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            r.ReadTextString(); r.SkipValue(); // image
            r.ReadTextString(); r.SkipValue(); // off
            r.ReadTextString(); r.SkipValue(); // len
            r.ReadTextString(); r.SkipValue(); // sha
            string lastKey = r.ReadTextString();
            Assert.AreEqual("data", lastKey, "last field in first chunk with SHA must be 'data'");
        }

        [TestMethod]
        public void EncodeUploadChunk_SubsequentChunk_FirstFieldIsImage()
        {
            // Even on continuation chunks, "image" must come first.
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0xDE }, 48, 0, slot: 1, isFirst: false);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            string firstKey = r.ReadTextString();
            Assert.AreEqual("image", firstKey, "first field in subsequent chunk must be 'image'");
        }

        [TestMethod]
        public void EncodeUploadChunk_SubsequentChunk_ImageSlotMatchesSlotArg()
        {
            // Verifies image slot value is carried correctly on continuation chunks.
            int slot = 1;
            byte[] cbor = McumgrClient.EncodeUploadChunk(new byte[] { 0x00 }, 96, 0, slot, isFirst: false);

            uint imageVal = 0;
            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                if (key == "image") imageVal = r.ReadUInt32();
                else                r.SkipValue();
            }

            Assert.AreEqual((uint)slot, imageVal, "image field on subsequent chunk must carry the correct slot number");
        }

        [TestMethod]
        public void EncodeImageState_WithHash_HasHashAndConfirmFields()
        {
            byte[] hash = new byte[32];
            for (int i = 0; i < 32; i++) hash[i] = (byte)(i * 7 + 3);

            byte[] cbor = McumgrClient.EncodeImageState(hash, confirm: false);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            int? count = r.ReadStartMap();
            Assert.AreEqual(2, count, "image state write with a hash must declare 2 fields");

            var fields = new Dictionary<string, object>();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                switch (key)
                {
                    case "hash":    fields["hash"]    = r.ReadByteString(); break;
                    case "confirm": fields["confirm"] = r.ReadBoolean(); break;
                    default:        r.SkipValue(); break;
                }
            }
            r.ReadEndMap();

            Assert.IsTrue(fields.ContainsKey("hash"), "must have 'hash' field");
            Assert.IsTrue(fields.ContainsKey("confirm"), "must have 'confirm' field");

            CollectionAssert.AreEqual(hash, (byte[])fields["hash"]);
            Assert.AreEqual(false, (bool)fields["confirm"]);
        }

        [TestMethod]
        public void EncodeImageState_Confirm_SetsConfirmTrue()
        {
            byte[] cbor = McumgrClient.EncodeImageState(new byte[32], confirm: true);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            r.ReadStartMap();
            bool? confirm = null;
            while (r.PeekState() != CborReaderState.EndMap)
            {
                string key = r.ReadTextString();
                if (key == "confirm") confirm = r.ReadBoolean();
                else r.SkipValue();
            }
            r.ReadEndMap();

            Assert.AreEqual(true, confirm);
        }

        [TestMethod]
        public void EncodeImageState_WithoutHash_OmitsHashField()
        {
            // single-image devices accept a hash-less request and fall back to image 0
            byte[] cbor = McumgrClient.EncodeImageState(null, confirm: false);

            var r = new CborReader(cbor, CborConformanceMode.Lax);
            int? count = r.ReadStartMap();
            Assert.AreEqual(1, count, "image state write without a hash must declare 1 field");

            Assert.AreEqual("confirm", r.ReadTextString());
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

        private static byte[] EncodeImageListPayload(McumgrImageInfo[] images)
        {
            var w = new CborWriter();
            w.WriteStartMap(1);
            w.WriteTextString("images");
            w.WriteStartArray(images.Length);
            foreach (McumgrImageInfo img in images)
            {
                w.WriteStartMap(8);
                w.WriteTextString("image");     w.WriteInt64(img.Image);
                w.WriteTextString("slot");      w.WriteInt64(img.Slot);
                w.WriteTextString("version");   w.WriteTextString(img.Version);
                w.WriteTextString("hash");      w.WriteByteString(img.Hash ?? Array.Empty<byte>());
                w.WriteTextString("active");    w.WriteBoolean(img.Active);
                w.WriteTextString("confirmed"); w.WriteBoolean(img.Confirmed);
                w.WriteTextString("pending");   w.WriteBoolean(img.Pending);
                w.WriteTextString("bootable");  w.WriteBoolean(img.Bootable);
                w.WriteEndMap();
            }
            w.WriteEndArray();
            w.WriteEndMap();
            return w.Encode();
        }
    }
}
