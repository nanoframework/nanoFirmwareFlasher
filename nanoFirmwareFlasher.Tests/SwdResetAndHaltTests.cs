// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class SwdResetAndHaltTests
    {
        [TestMethod]
        public void ResetAndHalt_UsesNativeStLinkMemoryCommands()
        {
            NativeUsb usb = new NativeUsb();
            using StLinkTransport transport = new StLinkTransport();
            typeof(StLinkTransport).GetField("_usb", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(transport, usb);
            using SwdProtocol protocol = new SwdProtocol(transport);
            protocol.ResetAndHalt();
            Assert.IsTrue(usb.ResetCaught);
            Assert.AreEqual(0x1000000U, usb.Demcr);
            Assert.IsTrue(usb.NativeReads > 0);
            Assert.IsTrue(usb.NativeWrites > 0);
        }

        [TestMethod]
        public void ResetAndHalt_CatchesResetAndRestoresUnrelatedDebugBits()
        {
            using MemoryTransport transport = new MemoryTransport();
            using SwdProtocol protocol = new SwdProtocol(transport);
            protocol.ResetAndHalt();
            Assert.IsTrue(transport.ResetCaught);
            Assert.AreEqual(0x1000000U, transport.Demcr);
            Assert.IsTrue(transport.Halted);
            protocol.SystemReset();
            Assert.IsFalse(transport.Halted);
        }

        [TestMethod]
        public void ResetAndHalt_RequiresBothResetAndHaltAndClearsCatchOnTimeout()
        {
            foreach (bool noReset in new[] { false, true })
            {
                using MemoryTransport transport = new MemoryTransport { NoReset = noReset, NoHalt = !noReset };
                using SwdProtocol protocol = new SwdProtocol(transport);
                Assert.ThrowsExactly<SwdProtocolException>(() => protocol.ResetAndHalt(1));
                Assert.AreEqual(0x1000000U, transport.Demcr);
            }
        }

        [TestMethod]
        public void ResetAndHalt_PropagatesTransportFailureAndClearsCatch()
        {
            using MemoryTransport transport = new MemoryTransport { FailResetWrite = true };
            using SwdProtocol protocol = new SwdProtocol(transport);
            SwdProtocolException error = Assert.ThrowsExactly<SwdProtocolException>(() => protocol.ResetAndHalt());
            Assert.AreEqual("Injected reset failure", error.Message);
            Assert.AreEqual(0x1000000U, transport.Demcr);
        }

        private sealed class NativeUsb : IStLinkUsb
        {
            private byte _command;
            private uint _address;
            private bool _data;
            internal uint Demcr = 0x1000000;
            internal bool ResetCaught;
            internal int NativeReads;
            internal int NativeWrites;
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
                    uint value = BitConverter.ToUInt32(data, 0);
                    if (_address == 0xE000EDFC)
                    {
                        Demcr = value;
                    }
                    else if (_address == 0xE000ED0C)
                    {
                        ResetCaught = (Demcr & 1) != 0;
                    }

                    NativeWrites++;
                    _data = false;
                    return;
                }

                _command = data[1];
                Assert.IsTrue(_command != 0x45 && _command != 0x46, "Native ST-LINK must not use DAP register access.");
                if (_command == 7 || _command == 8)
                {
                    _address = BitConverter.ToUInt32(data, 2);
                    _data = _command == 8;
                }
            }

            public int ReadBulk(byte endpoint, byte[] buffer, int length)
            {
                if (_command == 7)
                {
                    uint value = _address == 0xE000EDFC ? Demcr :
                        (1U << 17) | (ResetCaught ? 1U << 25 : 0);
                    Array.Copy(BitConverter.GetBytes(value), buffer, 4);
                    NativeReads++;
                }
                else
                {
                    buffer[0] = 0x80;
                }

                return length;
            }
        }

        private sealed class MemoryTransport : ISwdTransport
        {
            private uint _address;
            private uint _posted;
            private bool _reset;
            internal uint Demcr = 0x1000000;
            internal bool Halted;
            internal bool ResetCaught;
            internal bool NoReset;
            internal bool NoHalt;
            internal bool FailResetWrite;
            public string ProductName => "Test";
            public string SerialNumber => "Test";
            public int PacketSize => 64;
            public bool Connect() => true;
            public void Disconnect() { }
            public bool SetClock(uint frequencyHz) => true;
            public bool TransferConfigure(byte idleCycles, ushort waitRetry, ushort matchRetry) => true;
            public bool SwdConfigure(byte turnaround = 0) => true;
            public bool SwjSequence(byte bitCount, byte[] data) => true;
            public byte SwjPins(byte pinOutput, byte pinSelect, uint waitUs) => 0;
            public void Dispose() { }

            public uint[] ExecuteTransfer(byte dapIndex, TransferRequest[] requests)
            {
                List<uint> reads = new List<uint>();
                foreach (TransferRequest request in requests)
                {
                    bool ap = (request.Request & 1) != 0;
                    bool read = (request.Request & 2) != 0;
                    int register = request.Request & 12;
                    if (read)
                    {
                        if (ap && register == 12)
                        {
                            _posted = _address == 0xE000EDFC ? Demcr :
                                (Halted && !NoHalt ? 1U << 17 : 0) | (_reset && !NoReset ? 1U << 25 : 0);
                        }

                        reads.Add(_posted);
                    }
                    else if (ap && register == 4)
                    {
                        _address = request.Data;
                    }
                    else if (ap && register == 12)
                    {
                        if (_address == 0xE000EDFC)
                        {
                            Demcr = request.Data;
                        }
                        else if (_address == 0xE000EDF0)
                        {
                            Halted = (request.Data & 2) != 0;
                        }
                        else if (_address == 0xE000ED0C)
                        {
                            if (FailResetWrite)
                            {
                                throw new SwdProtocolException("Injected reset failure");
                            }

                            ResetCaught = (Demcr & 1) != 0;
                            Halted = ResetCaught;
                            _reset = true;
                        }
                    }
                }

                return reads.ToArray();
            }
        }
    }
}
