// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFirmwareFlasher.Tests.Helpers;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Stm32QspiNorProgrammerTests
    {
        internal static byte[] CreateImage(int size = 4139)
        {
            byte[] image = Enumerable.Repeat((byte)0xFF, size + 256).ToArray();
            for (int i = 256; i < image.Length; i++)
            {
                image[i] = (byte)(i * 13);
            }

            Put(image, 0, 0x4657464E);
            Put(image, 4, 1);
            Put(image, 8, (uint)size);
            Put(image, 12, Stm32WifiImage.ComputeCrc(image, 256, size));
            return image;
        }

        private static void Put(byte[] data, int offset, uint value)
        {
            for (int b = 0; b < 4; b++)
            {
                data[offset + b] = (byte)(value >> (8 * b));
            }
        }

        [TestMethod]
        public void ImageCrc_UsesNanoFrameworkUnreflectedConvention()
        {
            byte[] data = Encoding.ASCII.GetBytes("123456789");
            Assert.AreEqual(0x89A1897FU, Stm32WifiImage.ComputeCrc(data, 0, data.Length));
            Stm32WifiImage.Validate(0x90000000, CreateImage(1));
            Stm32WifiImage.Validate(0x90000000, CreateImage(0x3FF00));
        }

        [TestMethod]
        public void ImageValidation_RejectsMalformedContentAndAddress()
        {
            foreach (int offset in new[] { 0, 4, 8, 12, 16, 255, 256 })
            {
                byte[] image = CreateImage();
                image[offset] ^= 1;
                Assert.ThrowsExactly<SwdProtocolException>(() => Stm32WifiImage.Validate(0x90000000, image));
            }

            foreach (byte[] image in new[] { Array.Empty<byte>(), new byte[255], CreateImage(0), CreateImage(0x3FF01), CreateImage().Take(300).ToArray() })
            {
                Assert.ThrowsExactly<SwdProtocolException>(() => Stm32WifiImage.Validate(0x90000000, image));
            }

            Assert.ThrowsExactly<SwdProtocolException>(() => Stm32WifiImage.Validate(0x90000100, CreateImage()));
        }

        [TestMethod]
        public void Programmer_WritesHeaderLastAndPreservesUnrelatedFlashAndGpio()
        {
            NorModel model = new NorModel();
            model.Registers[0x40020000] = 0xABCD1234;
            byte[] original = (byte[])model.Flash.Clone();
            byte[] image = CreateImage();
            Stm32QspiNorProgrammer programmer = model.Create();
            programmer.Initialize(0x10006441);
            Assert.AreEqual(0xABCD1234U & ~0xF000U, model.Registers[0x40020000] & ~0xF000U);
            Assert.IsTrue(programmer.Program(image));
            CollectionAssert.AreEqual(image, model.Flash.Take(image.Length).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 4096 }, model.Erased.ToArray());
            Assert.AreEqual(0, model.Programmed.Last());
            Assert.IsTrue(model.ReadBeforeHeader);
            CollectionAssert.AreEqual(original.Skip(8192).ToArray(), model.Flash.Skip(8192).ToArray());
            programmer.Cleanup();
            Assert.AreEqual(0U, model.Registers[0xA0001000]);
        }

        [TestMethod]
        public void Programmer_SkipsOnlyWhenPayloadAlsoMatches()
        {
            NorModel model = new NorModel();
            byte[] image = CreateImage();
            Array.Copy(image, model.Flash, image.Length);
            Stm32QspiNorProgrammer programmer = model.Create();
            programmer.Initialize(0x441);
            Assert.IsFalse(programmer.Program(image));
            Assert.AreEqual(0, model.Erased.Count);
            Assert.AreEqual(0, model.Programmed.Count);
            model.Flash[300] ^= 1;
            Assert.IsTrue(programmer.Program(image));
            Assert.AreEqual(2, model.Erased.Count);
        }

        [TestMethod]
        public void Programmer_DoesNotCommitHeaderOnPayloadVerificationFailure()
        {
            NorModel model = new NorModel { CorruptPayload = true };
            Stm32QspiNorProgrammer programmer = model.Create();
            programmer.Initialize(0x441);
            Assert.ThrowsExactly<SwdProtocolException>(() => programmer.Program(CreateImage()));
            Assert.IsFalse(model.Programmed.Contains(0));
            Assert.IsTrue(model.Flash.Take(256).All(b => b == 0xFF));
            programmer.Cleanup();
        }

        [TestMethod]
        public void Programmer_RejectsAbsentUnknownChipsAndWrongMcuBeforeErase()
        {
            foreach (uint id in new uint[] { 0, 0xFFFFFF, 0x123456 })
            {
                NorModel model = new NorModel { Jedec = id };
                Stm32QspiNorProgrammer programmer = model.Create();
                Assert.ThrowsExactly<SwdProtocolException>(() => programmer.Initialize(0x441));
                Assert.AreEqual(0, model.Erased.Count);
                programmer.Cleanup();
            }

            NorModel wrongMcu = new NorModel();
            Assert.ThrowsExactly<SwdProtocolException>(() => wrongMcu.Create().Initialize(0x413));
            Assert.AreEqual(0, wrongMcu.Registers.Count);
        }

        [TestMethod]
        public void Programmer_DetectsWriteProtectionAndTransferErrors()
        {
            foreach (bool transferError in new[] { false, true })
            {
                NorModel model = new NorModel();
                Stm32QspiNorProgrammer programmer = model.Create();
                programmer.Initialize(0x441);
                model.WriteProtected = !transferError;
                model.TransferError = transferError;
                Assert.ThrowsExactly<SwdProtocolException>(() => programmer.Program(CreateImage()));
                Assert.AreEqual(0, model.Programmed.Count);
            }
        }

        [TestMethod]
        public void Programmer_BoundsControllerBusyWait()
        {
            NorModel model = new NorModel();
            Stm32QspiNorProgrammer programmer = model.Create();
            programmer.Initialize(0x441);
            model.ControllerBusy = true;
            Assert.ThrowsExactly<SwdProtocolException>(() => programmer.Program(CreateImage()));
            Assert.AreEqual(0, model.Erased.Count);
        }

        [TestMethod]
        public void BothDevices_RejectMalformedExternalBinBeforeMassErase()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[256]);
                foreach (Type type in new[] { typeof(StmStLinkDevice), typeof(StmSwdDevice) })
                {
                    // No transport/flash object: reaching internal erase would fail this test.
                    IStmFlashableDevice device = (IStmFlashableDevice)RuntimeHelpers.GetUninitializedObject(type);
                    device.DoMassErase = true;
                    using OutputWriterHelper output = new OutputWriterHelper();
                    Assert.AreEqual(ExitCodes.E5006, device.FlashBinFiles(new[] { path }, new[] { "0x90000000" }));
                    StringAssert.Contains(output.Output, "preflight failed");
                    ((IDisposable)device).Dispose();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void UnsupportedTransportWarning_IsExplicitAtQuietVerbosity()
        {
            using OutputWriterHelper output = new OutputWriterHelper();
            Stm32Operations.WarnWifiFirmwareSkipped();
            StringAssert.Contains(output.Output, "NOT PROGRAMMED");
            StringAssert.Contains(output.Output, "internal flash only");
            StringAssert.Contains(output.Output, "--nativestlink");
        }

        [TestMethod]
        public async Task PackageDiscovery_UsesSelectedArchiveAndExactTarget()
        {
            string directory = Path.Combine(Path.GetTempPath(), "nanoff-wifi-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            string? oldCache = FirmwarePackage.LocationPathBase;
            try
            {
                FirmwarePackage.LocationPathBase = Path.Combine(directory, "cache");
                foreach ((string target, string version, bool withWifi, bool expected) in new[]
                {
                    ("MXCHIP_AZ3166", "1.0.0", true, true),
                    ("MXCHIP_AZ3166", "1.0.1", false, false),
                    ("OTHER_BOARD", "1.0.0", true, false)
                })
                {
                    using (ZipArchive zip = ZipFile.Open(Path.Combine(directory, $"{target}-{version}.zip"), ZipArchiveMode.Create))
                    {
                        if (withWifi)
                        {
                            using Stream stream = zip.CreateEntry("wifi_firmware.bin").Open();
                            byte[] image = CreateImage();
                            stream.Write(image, 0, image.Length);
                        }
                    }

                    using Stm32Firmware firmware = new Stm32Firmware(target, version, false) { Verbosity = VerbosityLevel.Quiet };
                    Assert.AreEqual(ExitCodes.OK, await firmware.DownloadAndExtractAsync(directory));
                    Assert.AreEqual(expected, firmware.WifiFirmwareFile != null);
                }
            }
            finally
            {
                FirmwarePackage.LocationPathBase = oldCache;
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Programmer_PreservesReadFailureWhenAbortAlsoFailsAndStillResetsPeripheral()
        {
            using OutputWriterHelper output = new OutputWriterHelper();
            NorModel model = new NorModel();
            Stm32QspiNorProgrammer programmer = model.Create();
            programmer.Initialize(0x441);
            model.FailRead = true;
            model.FailAbort = true;
            SwdProtocolException error = Assert.ThrowsExactly<SwdProtocolException>(() => programmer.Program(CreateImage()));
            Assert.AreEqual("Injected read failure", error.Message);
            Assert.ThrowsExactly<SwdProtocolException>(() => programmer.Cleanup());
            Assert.AreEqual(0U, model.Registers[0xA0001000]);
            StringAssert.Contains(output.Output, "abort after read failure");
        }

        [TestMethod]
        public void Programmer_VerifiesCommittedHeader()
        {
            NorModel model = new NorModel { CorruptHeader = true };
            Stm32QspiNorProgrammer programmer = model.Create();
            programmer.Initialize(0x441);
            SwdProtocolException error = Assert.ThrowsExactly<SwdProtocolException>(() => programmer.Program(CreateImage(1)));
            StringAssert.Contains(error.Message, "after header commit");
            CollectionAssert.AreEqual(new[] { 256, 0 }, model.Programmed.ToArray());
            programmer.Cleanup();
        }

        private sealed class NorModel
        {
            internal readonly byte[] Flash = Enumerable.Repeat((byte)0xA5, 0x200000).ToArray();
            internal readonly Dictionary<uint, uint> Registers = new Dictionary<uint, uint>();
            internal readonly List<int> Erased = new List<int>();
            internal readonly List<int> Programmed = new List<int>();
            internal uint Jedec = 0x1520C2;
            internal bool CorruptPayload;
            internal bool ReadBeforeHeader;
            internal bool WriteProtected;
            internal bool TransferError;
            internal bool ControllerBusy;
            internal bool FailRead;
            internal bool FailAbort;
            internal bool CorruptHeader;
            private uint _command;
            private int _address;
            private int _written;
            private bool _wel;

            internal Stm32QspiNorProgrammer Create() => new Stm32QspiNorProgrammer(Read, Write, ReadBytes);

            private uint Read(uint address)
            {
                if (address == 0xA0001008)
                {
                    return TransferError ? 1U : (ControllerBusy ? 32U : 6U) |
                        ((_command & 0x05000000) == 0x05000000 ? 4U << 8 : 0);
                }

                if (address == 0xA0001020)
                {
                    return (_command & 255) == 0x9F ? Jedec : (_wel ? 2U : 0);
                }

                return Registers.TryGetValue(address, out uint value) ? value : 0;
            }

            private void Write(uint address, uint value)
            {
                if (FailAbort && address == 0xA0001000 && (value & 2) != 0)
                {
                    throw new SwdProtocolException("Injected abort failure");
                }

                Registers[address] = value;
                if (address == 0xA0001014)
                {
                    _command = value;
                    if (value == 0x106)
                    {
                        _wel = !WriteProtected;
                    }
                }
                else if (address == 0xA0001018)
                {
                    _address = (int)value;
                    _written = 0;
                    if ((_command & 255) == 0x20)
                    {
                        Assert.IsTrue(_wel);
                        Erased.Add(_address);
                        Array.Fill(Flash, (byte)0xFF, _address, 4096);
                        _wel = false;
                    }
                    else if ((_command & 255) == 2)
                    {
                        Assert.IsTrue(_wel);
                        Programmed.Add(_address);
                    }
                }
                else if (address == 0xA0001020)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Flash[_address + _written++] &= (byte)(value >> (8 * i));
                    }

                    if (_written == Registers[0xA0001010] + 1)
                    {
                        Assert.IsTrue(_written <= 256);
                        _wel = false;
                        if (CorruptPayload && _address == 256)
                        {
                            Flash[256] ^= 1;
                        }

                        if (CorruptHeader && _address == 0)
                        {
                            Flash[0] ^= 1;
                        }
                    }
                }
            }

            private byte[] ReadBytes(uint address, int length)
            {
                if (FailRead)
                {
                    throw new SwdProtocolException("Injected read failure");
                }

                Assert.AreEqual(0x0D002503U, _command);
                if (Programmed.Count != 0 && !Programmed.Contains(0))
                {
                    ReadBeforeHeader = true;
                }

                return Flash.Skip((int)(address - 0x90000000)).Take(length).ToArray();
            }
        }
    }
}
