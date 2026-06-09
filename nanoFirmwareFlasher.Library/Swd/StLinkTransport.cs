//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// ST-LINK V2/V3 USB transport layer.
    /// Communicates with ST-LINK debug probes via USB bulk transfers.
    /// Implements <see cref="ISwdTransport"/> so it can be used with
    /// <see cref="SwdProtocol"/>, <see cref="ArmMemAp"/>, and <see cref="Stm32FlashProgrammer"/>.
    /// Cross-platform: uses WinUSB on Windows, libusb-1.0 on Linux/macOS.
    /// </summary>
    internal class StLinkTransport : ISwdTransport
    {
        #region ST-LINK command constants

        // ST-LINK command sizes
        private const int CmdSize = 16;   // Command packet is always 16 bytes
        private const int DataInSize = 64; // Default data-in packet size

        // Top-level commands
        private const byte StLinkGetVersion = 0xF1;
        private const byte StLinkDebugCommand = 0xF2;
        private const byte StLinkDfuCommand = 0xF3;
        private const byte StLinkGetCurrentMode = 0xF5;

        // Debug sub-commands
        private const byte StLinkDebugEnterSwd = 0x30;
        private const byte StLinkDebugExit = 0x21;
        private const byte StLinkDebugReadIdCodes = 0x31;
        private const byte StLinkDebugReadMemory32 = 0x36;
        private const byte StLinkDebugWriteMemory32 = 0x35;
        private const byte StLinkDebugReadMemory8 = 0x3C;
        private const byte StLinkDebugWriteMemory8 = 0x3D;
        private const byte StLinkDebugSetSwdClk = 0x43;
        private const byte StLinkDebugReadRegister = 0x33;
        private const byte StLinkDebugWriteRegister = 0x34;
        private const byte StLinkDebugRunCore = 0x09;
        private const byte StLinkDebugHaltCore = 0x02;
        private const byte StLinkDebugResetSys = 0x32;
        private const byte StLinkDebugReadCoreStatus = 0x01;

        // DAP register access (V2 API)
        private const byte StLinkDebugApiV2ReadDap = 0x45;
        private const byte StLinkDebugApiV2WriteDap = 0x46;

        // Mode values
        private const byte StLinkModeDebugSwd = 0x02;
        private const byte StLinkModeDfu = 0x00;
        private const byte StLinkModeMass = 0x01;

        // Status codes
        private const byte StLinkDebugErrOk = 0x80;

        // SWD clock speed divisor values (for STLINK_DEBUG_SET_SWD_CLK)
        // Maps to STLINK_DEBUG_APIV2_SWD_SET_FREQ
        private const ushort StLinkSwdClk4000K = 0;
        private const ushort StLinkSwdClk1800K = 1;
        private const ushort StLinkSwdClk1200K = 2;
        private const ushort StLinkSwdClk950K = 3;
        private const ushort StLinkSwdClk480K = 7;
        private const ushort StLinkSwdClk240K = 15;
        private const ushort StLinkSwdClk125K = 31;
        private const ushort StLinkSwdClk100K = 40;
        private const ushort StLinkSwdClk50K = 79;
        private const ushort StLinkSwdClk25K = 158;
        private const ushort StLinkSwdClk15K = 265;

        // ST-LINK VID and known PIDs
        private const ushort StLinkVid = 0x0483;
        private static readonly ushort[] StLinkPids = { 0x3748, 0x374B, 0x374D, 0x374E, 0x374F, 0x3752, 0x3753 };

        // USB endpoint addresses
        private const byte EpOut = 0x02; // Bulk OUT
        private const byte EpIn = 0x81;  // Bulk IN (V2)

        #endregion

        #region Platform USB abstraction

        private IStLinkUsb _usb;
        private bool _disposed;
        private ushort _stLinkVersion;
        private byte _jtag_api;

        public string ProductName { get; private set; }
        public string SerialNumber { get; private set; }
        public int PacketSize => 64;

        /// <summary>
        /// Opens a connection to an ST-LINK probe by device path.
        /// </summary>
        internal void Open(string devicePath)
        {
            _usb = StLinkUsbFactory.Create();
            _usb.Open(devicePath);

            ProductName = _usb.ProductName ?? "ST-LINK";
            SerialNumber = _usb.SerialNumber ?? string.Empty;

            // Read and parse ST-LINK version
            ReadVersion();

            // Ensure we're in debug mode
            LeaveCurrentMode();
        }

        /// <summary>
        /// Enumerates connected ST-LINK debug probes.
        /// </summary>
        internal static List<(string productName, string serialNumber, string devicePath)> Enumerate()
        {
            return StLinkUsbFactory.Enumerate();
        }

        #endregion

        #region ISwdTransport implementation

        public bool Connect()
        {
            // Enter SWD debug mode
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugEnterSwd;

            byte[] resp = SendCommand(cmd, 2);

            return resp != null && resp.Length >= 2 && resp[0] == StLinkDebugErrOk;
        }

        public void Disconnect()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugExit;

            SendCommand(cmd, 0);
        }

        public bool SetClock(uint frequencyHz)
        {
            ushort divisor = GetSwdClockDivisor(frequencyHz);

            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugSetSwdClk;
            cmd[2] = (byte)(divisor & 0xFF);
            cmd[3] = (byte)((divisor >> 8) & 0xFF);

            byte[] resp = SendCommand(cmd, 2);
            return resp != null && resp.Length >= 2 && resp[0] == StLinkDebugErrOk;
        }

        public bool TransferConfigure(byte idleCycles, ushort waitRetry, ushort matchRetry)
        {
            // ST-LINK protocol doesn't have an equivalent of DAP_TransferConfigure.
            // Retry behavior is handled at the protocol layer.
            return true;
        }

        public bool SwdConfigure(byte turnaround = 0)
        {
            // ST-LINK configures SWD parameters internally.
            return true;
        }

        public bool SwjSequence(byte bitCount, byte[] data)
        {
            // ST-LINK handles JTAG-to-SWD switching internally when entering SWD mode.
            // No explicit sequence needed.
            return true;
        }

        public uint[] ExecuteTransfer(byte dapIndex, TransferRequest[] requests)
        {
            var results = new List<uint>();

            for (int i = 0; i < requests.Length; i++)
            {
                bool isRead = (requests[i].Request & 0x02) != 0;
                bool isAp = (requests[i].Request & 0x01) != 0;
                byte regAddr = (byte)(requests[i].Request & 0x0C);

                if (isRead)
                {
                    uint value = isAp
                        ? ReadDapRegister(1, regAddr)
                        : ReadDapRegister(0, regAddr);
                    results.Add(value);
                }
                else
                {
                    if (isAp)
                    {
                        WriteDapRegister(1, regAddr, requests[i].Data);
                    }
                    else
                    {
                        WriteDapRegister(0, regAddr, requests[i].Data);
                    }
                }
            }

            return results.ToArray();
        }

        public byte SwjPins(byte pinOutput, byte pinSelect, uint waitUs)
        {
            // ST-LINK does not have a direct pin manipulation command like CMSIS-DAP.
            // For nRESET, use the dedicated reset commands.
            if ((pinSelect & 0x80) != 0) // nRESET pin
            {
                if ((pinOutput & 0x80) == 0)
                {
                    // Assert reset (pull low)
                    HardwareReset(true);
                }
                else
                {
                    // Deassert reset (release)
                    HardwareReset(false);
                    Thread.Sleep(10);
                }
            }

            return pinOutput; // Return requested state as "actual" state
        }

        #endregion

        #region ST-LINK specific operations

        /// <summary>
        /// Maximum bytes per single ST-LINK read/write memory transfer.
        /// </summary>
        private const int MaxTransferSize = 6144;

        /// <summary>
        /// Reads a 32-bit aligned block of memory.
        /// </summary>
        internal uint[] ReadMemory32(uint address, int wordCount)
        {
            uint[] result = new uint[wordCount];
            int wordsRead = 0;

            while (wordsRead < wordCount)
            {
                int remaining = wordCount - wordsRead;
                int chunkWords = Math.Min(remaining, MaxTransferSize / 4);
                int chunkBytes = chunkWords * 4;
                uint chunkAddr = address + (uint)(wordsRead * 4);

                byte[] cmd = new byte[CmdSize];
                cmd[0] = StLinkDebugCommand;
                cmd[1] = StLinkDebugReadMemory32;
                cmd[2] = (byte)(chunkAddr & 0xFF);
                cmd[3] = (byte)((chunkAddr >> 8) & 0xFF);
                cmd[4] = (byte)((chunkAddr >> 16) & 0xFF);
                cmd[5] = (byte)((chunkAddr >> 24) & 0xFF);
                cmd[6] = (byte)(chunkBytes & 0xFF);
                cmd[7] = (byte)((chunkBytes >> 8) & 0xFF);

                byte[] data = SendCommand(cmd, chunkBytes);

                if (data == null || data.Length < chunkBytes)
                {
                    throw new SwdProtocolException(
                        $"Failed to read {chunkBytes} bytes from 0x{chunkAddr:X8}.");
                }

                for (int i = 0; i < chunkWords; i++)
                {
                    int off = i * 4;
                    result[wordsRead + i] = (uint)(data[off] | (data[off + 1] << 8) |
                                                   (data[off + 2] << 16) | (data[off + 3] << 24));
                }

                wordsRead += chunkWords;
            }

            return result;
        }

        /// <summary>
        /// Writes a 32-bit aligned block of memory.
        /// </summary>
        internal void WriteMemory32(uint address, byte[] data)
        {
            int offset = 0;

            while (offset < data.Length)
            {
                int remaining = data.Length - offset;
                int chunkLen = Math.Min(remaining, MaxTransferSize);

                // Round down to 4-byte alignment
                chunkLen = (chunkLen / 4) * 4;

                if (chunkLen == 0)
                {
                    break;
                }

                uint chunkAddr = address + (uint)offset;

                byte[] cmd = new byte[CmdSize];
                cmd[0] = StLinkDebugCommand;
                cmd[1] = StLinkDebugWriteMemory32;
                cmd[2] = (byte)(chunkAddr & 0xFF);
                cmd[3] = (byte)((chunkAddr >> 8) & 0xFF);
                cmd[4] = (byte)((chunkAddr >> 16) & 0xFF);
                cmd[5] = (byte)((chunkAddr >> 24) & 0xFF);
                cmd[6] = (byte)(chunkLen & 0xFF);
                cmd[7] = (byte)((chunkLen >> 8) & 0xFF);

                byte[] chunk = new byte[chunkLen];
                Buffer.BlockCopy(data, offset, chunk, 0, chunkLen);

                SendCommandWithData(cmd, chunk);

                offset += chunkLen;
            }
        }

        /// <summary>
        /// Reads the target IDCODE via ST-LINK.
        /// </summary>
        internal uint ReadIdCodes()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugReadIdCodes;

            byte[] resp = SendCommand(cmd, 12);

            if (resp == null || resp.Length < 4)
            {
                return 0;
            }

            // Bytes 0-3: status + padding, bytes 4-7: IDCODE
            if (resp.Length >= 8)
            {
                return (uint)(resp[4] | (resp[5] << 8) | (resp[6] << 16) | (resp[7] << 24));
            }

            return 0;
        }

        /// <summary>
        /// Halts the target core.
        /// </summary>
        internal void HaltCore()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugHaltCore;

            SendCommand(cmd, 2);
        }

        /// <summary>
        /// Runs (resumes) the target core.
        /// </summary>
        internal void RunCore()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugRunCore;

            SendCommand(cmd, 2);
        }

        /// <summary>
        /// Resets the target system.
        /// </summary>
        internal void ResetSystem()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugResetSys;

            SendCommand(cmd, 2);
            Thread.Sleep(50);
        }

        /// <summary>
        /// Checks if the target core is halted.
        /// </summary>
        internal bool IsCoreHalted()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugReadCoreStatus;

            byte[] resp = SendCommand(cmd, 4);

            if (resp != null && resp.Length >= 4)
            {
                uint status = (uint)(resp[0] | (resp[1] << 8) | (resp[2] << 16) | (resp[3] << 24));
                return (status & 0x01) != 0; // DHCSR S_HALT bit
            }

            return false;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        Disconnect();
                    }
                    catch
                    {
                        // Ignore disconnect errors during dispose
                    }

                    _usb?.Dispose();
                    _usb = null;
                }

                _disposed = true;
            }
        }

        ~StLinkTransport()
        {
            Dispose(false);
        }

        #endregion

        #region Private helpers

        private void ReadVersion()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkGetVersion;

            byte[] resp = SendCommand(cmd, 6);

            if (resp == null || resp.Length < 6)
            {
                throw new SwdProtocolException("Failed to read ST-LINK version.");
            }

            // Parse version: bytes 0-1 contain the version bitfield
            // Bits [15:12]=major, [11:6]=JTAG/SWD version, [5:0]=SWIM version
            ushort versionRaw = (ushort)((resp[0] << 8) | resp[1]);
            int major = (versionRaw >> 12) & 0x0F;
            int jtag = (versionRaw >> 6) & 0x3F;
            // int swim = versionRaw & 0x3F;  // not used

            _stLinkVersion = versionRaw;

            // V2 uses API v2 if JTAG version >= 11, V3 always uses API v2
            _jtag_api = (byte)(jtag >= 11 || major >= 3 ? 2 : 1);

            if (ProductName == null || ProductName == "ST-LINK")
            {
                ProductName = $"ST-LINK/V{major}";
            }
        }

        private void LeaveCurrentMode()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkGetCurrentMode;

            byte[] resp = SendCommand(cmd, 2);

            if (resp == null || resp.Length < 2)
            {
                return;
            }

            byte mode = resp[0];

            if (mode == StLinkModeDebugSwd)
            {
                // Already in debug mode — leave first so we can re-enter cleanly
                byte[] leaveCmd = new byte[CmdSize];
                leaveCmd[0] = StLinkDebugCommand;
                leaveCmd[1] = StLinkDebugExit;
                SendCommand(leaveCmd, 0);
            }
        }

        private uint ReadDapRegister(byte port, byte addr)
        {
            if (_jtag_api < 2)
            {
                throw new SwdProtocolException(
                    "ST-LINK firmware is too old for DAP register access. " +
                    "Update your ST-LINK firmware to JTAG version >= 11.");
            }

            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugApiV2ReadDap;
            cmd[2] = port;   // 0 = DP, 1 = AP
            cmd[3] = 0;
            cmd[4] = addr;   // register address
            cmd[5] = 0;

            byte[] resp = SendCommand(cmd, 8);

            if (resp == null || resp.Length < 8)
            {
                throw new SwdProtocolException(
                    $"Failed to read DAP register (port={port}, addr=0x{addr:X2}).");
            }

            // Check status in first 2 bytes
            if (resp[0] != StLinkDebugErrOk)
            {
                throw new SwdProtocolException(
                    $"ST-LINK DAP read error: status=0x{resp[0]:X2} (port={port}, addr=0x{addr:X2}).");
            }

            return (uint)(resp[4] | (resp[5] << 8) | (resp[6] << 16) | (resp[7] << 24));
        }

        private void WriteDapRegister(byte port, byte addr, uint data)
        {
            if (_jtag_api < 2)
            {
                throw new SwdProtocolException(
                    "ST-LINK firmware is too old for DAP register access. " +
                    "Update your ST-LINK firmware to JTAG version >= 11.");
            }

            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugApiV2WriteDap;
            cmd[2] = port;
            cmd[3] = 0;
            cmd[4] = addr;
            cmd[5] = 0;
            cmd[6] = (byte)(data & 0xFF);
            cmd[7] = (byte)((data >> 8) & 0xFF);
            cmd[8] = (byte)((data >> 16) & 0xFF);
            cmd[9] = (byte)((data >> 24) & 0xFF);

            byte[] resp = SendCommand(cmd, 2);

            if (resp != null && resp.Length >= 2 && resp[0] != StLinkDebugErrOk)
            {
                throw new SwdProtocolException(
                    $"ST-LINK DAP write error: status=0x{resp[0]:X2} (port={port}, addr=0x{addr:X2}, data=0x{data:X8}).");
            }
        }

        private void HardwareReset(bool assert)
        {
            // Use SwjPins to toggle nRESET (not natively supported by ST-LINK directly via this method)
            // Instead, use system reset command
            if (assert)
            {
                // Hold core in halt and reset
                HaltCore();
            }
            else
            {
                ResetSystem();
            }
        }

        private ushort GetSwdClockDivisor(uint frequencyHz)
        {
            // Map desired frequency to the closest ST-LINK clock divisor
            if (frequencyHz >= 4000000)
            {
                return StLinkSwdClk4000K;
            }

            if (frequencyHz >= 1800000)
            {
                return StLinkSwdClk1800K;
            }

            if (frequencyHz >= 1200000)
            {
                return StLinkSwdClk1200K;
            }

            if (frequencyHz >= 950000)
            {
                return StLinkSwdClk950K;
            }

            if (frequencyHz >= 480000)
            {
                return StLinkSwdClk480K;
            }

            if (frequencyHz >= 240000)
            {
                return StLinkSwdClk240K;
            }

            if (frequencyHz >= 125000)
            {
                return StLinkSwdClk125K;
            }

            if (frequencyHz >= 100000)
            {
                return StLinkSwdClk100K;
            }

            if (frequencyHz >= 50000)
            {
                return StLinkSwdClk50K;
            }

            if (frequencyHz >= 25000)
            {
                return StLinkSwdClk25K;
            }

            return StLinkSwdClk15K;
        }

        private byte[] SendCommand(byte[] cmd, int responseLength)
        {
            _usb.WriteBulk(EpOut, cmd, cmd.Length);

            if (responseLength <= 0)
            {
                return null;
            }

            byte[] response = new byte[responseLength];
            int bytesRead = _usb.ReadBulk(EpIn, response, responseLength);

            if (bytesRead < responseLength)
            {
                // Partial read — resize
                byte[] partial = new byte[bytesRead];
                Buffer.BlockCopy(response, 0, partial, 0, bytesRead);
                return partial;
            }

            return response;
        }

        private void SendCommandWithData(byte[] cmd, byte[] data)
        {
            _usb.WriteBulk(EpOut, cmd, cmd.Length);
            _usb.WriteBulk(EpOut, data, data.Length);
        }

        #endregion
    }

    #region ST-LINK USB abstraction

    /// <summary>
    /// Low-level USB bulk transfer interface for ST-LINK probes.
    /// </summary>
    internal interface IStLinkUsb : IDisposable
    {
        void Open(string devicePath);
        void WriteBulk(byte endpoint, byte[] data, int length);
        int ReadBulk(byte endpoint, byte[] buffer, int length);
        string ProductName { get; }
        string SerialNumber { get; }
    }

    /// <summary>
    /// Factory for creating ST-LINK USB instances using LibUsbDotNet.
    /// </summary>
    internal static class StLinkUsbFactory
    {
        internal static IStLinkUsb Create()
        {
            return new LibUsbDotNetStLinkUsb();
        }

        internal static List<(string productName, string serialNumber, string devicePath)> Enumerate()
        {
            return LibUsbDotNetStLinkUsb.EnumerateDevices();
        }
    }

    /// <summary>
    /// Cross-platform ST-LINK USB implementation using LibUsbDotNet (bulk transfers).
    /// Replaces the per-platform WindowsStLinkUsb and LibUsbStLinkUsb implementations.
    /// </summary>
    internal class LibUsbDotNetStLinkUsb : IStLinkUsb
    {
        private const int StLinkVid = 0x0483;
        private static readonly int[] KnownPids = { 0x3748, 0x374B, 0x374D, 0x374E, 0x374F, 0x3752, 0x3753 };

        private const int BulkTransferTimeout = 5000;

        private LibUsbDotNet.UsbDevice _device;
        private LibUsbDotNet.UsbEndpointWriter _writer;
        private LibUsbDotNet.UsbEndpointReader _reader;
        private bool _disposed;

        public string ProductName { get; private set; }
        public string SerialNumber { get; private set; }

        public void Open(string devicePath)
        {
            // Try each known ST-LINK PID
            for (int i = 0; i < KnownPids.Length && _device == null; i++)
            {
                var finder = new LibUsbDotNet.Main.UsbDeviceFinder(StLinkVid, KnownPids[i]);
                _device = LibUsbDotNet.UsbDevice.OpenUsbDevice(finder);
            }

            if (_device == null)
            {
                throw new SwdProtocolException(
                    "No ST-LINK device found. Make sure the probe is connected. " +
                    "On Windows, install the WinUSB driver via Zadig (https://zadig.akeo.ie). " +
                    "On Linux, add a udev rule: echo 'SUBSYSTEM==\"usb\", ATTR{idVendor}==\"0483\", MODE=\"0666\"' " +
                    "| sudo tee /etc/udev/rules.d/70-st-link.rules && sudo udevadm control --reload-rules. " +
                    "On macOS, no additional drivers are needed.");
            }

            // If this is a "whole" USB device (libusb), we need to claim the interface
            LibUsbDotNet.IUsbDevice wholeDevice = _device as LibUsbDotNet.IUsbDevice;

            if (wholeDevice != null)
            {
                wholeDevice.SetAutoDetachKernelDriver(true);
                wholeDevice.SetConfiguration(1);
                wholeDevice.ClaimInterface(0);
            }

            // Read product name and serial number
            try
            {
                if (_device.Info != null && !string.IsNullOrEmpty(_device.Info.ProductString))
                {
                    ProductName = _device.Info.ProductString;
                }
                else
                {
                    ProductName = "ST-LINK";
                }
            }
            catch
            {
                ProductName = "ST-LINK";
            }

            try
            {
                if (_device.Info != null && !string.IsNullOrEmpty(_device.Info.SerialString))
                {
                    SerialNumber = _device.Info.SerialString;
                }
                else
                {
                    SerialNumber = string.Empty;
                }
            }
            catch
            {
                SerialNumber = string.Empty;
            }

            // Open bulk endpoints: OUT=0x02, IN=0x81
            _writer = _device.OpenEndpointWriter(LibUsbDotNet.Main.WriteEndpointID.Ep02);
            _reader = _device.OpenEndpointReader(LibUsbDotNet.Main.ReadEndpointID.Ep01);
        }

        public void WriteBulk(byte endpoint, byte[] data, int length)
        {
            var error = _writer.Write(data, 0, length, BulkTransferTimeout, out _);

            if (error != LibUsbDotNet.Main.ErrorCode.None)
            {
                throw new SwdProtocolException(
                    $"ST-LINK USB bulk write failed. Error: {error}");
            }
        }

        public int ReadBulk(byte endpoint, byte[] buffer, int length)
        {
            var error = _reader.Read(buffer, 0, length, BulkTransferTimeout, out int bytesRead);

            if (error != LibUsbDotNet.Main.ErrorCode.None)
            {
                throw new SwdProtocolException(
                    $"ST-LINK USB bulk read failed. Error: {error}");
            }

            return bytesRead;
        }

        internal static List<(string productName, string serialNumber, string devicePath)> EnumerateDevices()
        {
            var devices = new List<(string productName, string serialNumber, string devicePath)>();

            LibUsbDotNet.Main.UsbRegDeviceList allDevices = LibUsbDotNet.UsbDevice.AllDevices;

            foreach (LibUsbDotNet.Main.UsbRegistry reg in allDevices)
            {
                if (reg.Vid != StLinkVid || !IsKnownPid((ushort)reg.Pid))
                {
                    continue;
                }

                string devicePath = reg.DevicePath ?? reg.SymbolicName ?? string.Empty;
                string serial = string.Empty;
                string productName = "ST-LINK";

                try
                {
                    LibUsbDotNet.UsbDevice dev = reg.Device;

                    if (dev != null)
                    {
                        if (dev.Info != null)
                        {
                            if (!string.IsNullOrEmpty(dev.Info.SerialString))
                            {
                                serial = dev.Info.SerialString;
                            }

                            if (!string.IsNullOrEmpty(dev.Info.ProductString))
                            {
                                productName = dev.Info.ProductString;
                            }
                        }

                        dev.Close();
                    }
                }
                catch
                {
                    // If we can't open it, use the device path
                }

                if (string.IsNullOrEmpty(serial))
                {
                    serial = devicePath;
                }

                devices.Add((productName, serial, devicePath));
            }

            return devices;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_device != null)
                {
                    LibUsbDotNet.IUsbDevice wholeDevice = _device as LibUsbDotNet.IUsbDevice;

                    if (wholeDevice != null)
                    {
                        try
                        {
                            wholeDevice.ReleaseInterface(0);
                        }
                        catch
                        {
                            // Ignore release errors during dispose
                        }
                    }

                    _device.Close();
                    _device = null;
                }

                _disposed = true;
            }
        }

        private static bool IsKnownPid(ushort pid)
        {
            for (int i = 0; i < KnownPids.Length; i++)
            {
                if (KnownPids[i] == pid)
                {
                    return true;
                }
            }

            return false;
        }
    }

    #endregion
}
