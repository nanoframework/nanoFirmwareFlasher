// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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

        [TestMethod]
        [DataRow(typeof(StmStLinkDevice))]
        [DataRow(typeof(StmSwdDevice))]
        public void FlashBinFiles_ProgramsRetainedExternalImageWhenFileChanges(Type deviceType)
        {
            string path = Path.GetTempFileName();
            try
            {
                byte[] original = CreateImage(1);
                byte[] replacement = CreateImage(2);
                File.WriteAllBytes(path, original);
                NorModel model = new NorModel { OnJedecRead = () => File.WriteAllBytes(path, replacement) };
                using StLinkTransport transport = CreateTransport(new NativeUsb(model));
                IStmFlashableDevice device = CreateDevice(deviceType, transport);
                using IDisposable lifetime = (IDisposable)device;
                using OutputWriterHelper output = new OutputWriterHelper();

                Assert.AreEqual(ExitCodes.OK, device.FlashBinFiles(new[] { path }, new[] { "0x90000000" }));
                CollectionAssert.AreEqual(replacement, File.ReadAllBytes(path));
                CollectionAssert.AreEqual(original, model.Flash.Take(original.Length).ToArray());
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        [DataRow(typeof(StmStLinkDevice), false)]
        [DataRow(typeof(StmStLinkDevice), true)]
        [DataRow(typeof(StmSwdDevice), false)]
        [DataRow(typeof(StmSwdDevice), true)]
        public void FlashBinFiles_RetryAfterJedecRejectionReinitializesWithoutErasing(Type deviceType, bool cleanupFails)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, CreateImage(1));
                NorModel model = new NorModel { Jedec = 0x123456, FailAbort = cleanupFails };
                NativeUsb usb = new NativeUsb(model) { FailReset = cleanupFails };
                using StLinkTransport transport = CreateTransport(usb);
                IStmFlashableDevice device = CreateDevice(deviceType, transport);
                using IDisposable lifetime = (IDisposable)device;
                using OutputWriterHelper output = new OutputWriterHelper();

                ExitCodes first = device.FlashBinFiles(new[] { path }, new[] { "0x90000000" });
                ExitCodes retry = device.FlashBinFiles(new[] { path }, new[] { "0x90000000" });

                Assert.AreEqual(0, model.EraseCommands);
                Assert.AreEqual(0, model.Erased.Count);
                Assert.AreEqual(0, model.Programmed.Count);
                Assert.AreEqual(ExitCodes.E5006, first);
                Assert.AreEqual(ExitCodes.E5006, retry);
                Assert.AreEqual(2, model.JedecReads);
                Assert.AreEqual(2, usb.ResetCommands);
                Assert.AreEqual(0U, model.Registers[0xA0001000]);
                StringAssert.Contains(output.Output, "preflight failed: Unsupported external NOR");
                if (cleanupFails)
                {
                    StringAssert.Contains(output.Output, "External-flash cleanup failed");
                    StringAssert.Contains(output.Output, "Reset after external-flash failure failed");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static StLinkTransport CreateTransport(NativeUsb usb)
        {
            StLinkTransport transport = new StLinkTransport();
            typeof(StLinkTransport).GetField("_usb", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(transport, usb);
            return transport;
        }

        private static IStmFlashableDevice CreateDevice(Type type, StLinkTransport transport)
        {
            SwdProtocol swd = new SwdProtocol(transport);
            ArmMemAp mem = new ArmMemAp(swd);
            Stm32FlashProgrammer flash = new Stm32FlashProgrammer(mem);
            flash.DetectFamily();
            object device = RuntimeHelpers.GetUninitializedObject(type);
            type.GetField("_swd", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, swd);
            type.GetField("_mem", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, mem);
            type.GetField("_flash", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, flash);
            return (IStmFlashableDevice)device;
        }

        private sealed class NativeUsb : IStLinkUsb
        {
            private readonly NorModel _model;
            private byte _command;
            private uint _address;
            private bool _data;
            internal bool FailReset;
            internal int ResetCommands;
            internal NativeUsb(NorModel model) => _model = model;
            public string ProductName => "Test";
            public string SerialNumber => "Test";
            public void Open(string devicePath) { }
            public void ResetPipes() { }
            public void ResetDevice() { }
            public void Dispose() { }

            public void WriteBulk(byte endpoint, byte[] data, int length)
            {
                if (_data)
                {
                    _data = false;
                    _model.Write(_address, BitConverter.ToUInt32(data, 0));
                    return;
                }

                _command = data[1];
                if (_command == 7 || _command == 8)
                {
                    _address = BitConverter.ToUInt32(data, 2);
                    _data = _command == 8;
                }
                else if (_command == 0x32)
                {
                    ResetCommands++;
                }
            }

            public int ReadBulk(byte endpoint, byte[] buffer, int length)
            {
                if (_command == 7)
                {
                    for (int i = 0; i < length; i += 4)
                    {
                        Array.Copy(BitConverter.GetBytes(_model.Read(_address + (uint)i)), 0, buffer, i, 4);
                    }
                }
                else
                {
                    buffer[0] = FailReset && (_command == 0x32 || _command == 0x3C) ? (byte)0 : (byte)0x80;
                }

                return length;
            }
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
            internal Action? OnJedecRead;
            internal int JedecReads;
            internal int EraseCommands;
            private uint _command;
            private int _address;
            private int _written;
            private bool _wel;

            internal Stm32QspiNorProgrammer Create() => new Stm32QspiNorProgrammer(Read, Write, ReadBytes);

            internal uint Read(uint address)
            {
                if (address == 0xE0042000)
                {
                    return 0x441;
                }

                if (address == 0xE000EDF0)
                {
                    return (1U << 17) | (1U << 25);
                }

                if (address >= 0x90000000 && address < 0x90200000)
                {
                    return BitConverter.ToUInt32(Flash, (int)(address - 0x90000000));
                }

                if (address == 0xA0001008)
                {
                    return TransferError ? 1U : (ControllerBusy ? 32U : 6U) |
                        ((_command & 0x05000000) == 0x05000000 ? 4U << 8 : 0);
                }

                if (address == 0xA0001020)
                {
                    if ((_command & 255) == 0x9F)
                    {
                        JedecReads++;
                        OnJedecRead?.Invoke();
                    }

                    return (_command & 255) == 0x9F ? Jedec : (_wel ? 2U : 0);
                }

                return Registers.TryGetValue(address, out uint value) ? value : 0;
            }

            internal void Write(uint address, uint value)
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
                    else if ((value & 255) == 0x20)
                    {
                        EraseCommands++;
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
