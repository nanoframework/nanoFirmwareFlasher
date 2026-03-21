// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Implements the PICOBOOT USB protocol for direct communication with
    /// Raspberry Pi Pico (RP2040/RP2350) devices in BOOTSEL mode.
    /// </summary>
    /// <remarks>
    /// When a Pico is in BOOTSEL mode, it exposes both a USB mass storage interface
    /// (used by <see cref="PicoUf2Utility"/>) and a PICOBOOT bulk interface. This class
    /// communicates via the PICOBOOT interface for direct flash read/write, verification,
    /// mass erase, and force-reboot capabilities.
    /// </remarks>
    public class PicoBootDevice : IDisposable
    {
        #region Constants

        // Raspberry Pi USB vendor/product IDs
        private const int VendorIdRaspberryPi = 0x2E8A;
        private const int ProductIdRp2040UsbBoot = 0x0003;
        private const int ProductIdRp2350UsbBoot = 0x000F;
        private const int ProductIdStdioUsb = 0x0009;
        private const int ProductIdStdioUsbRp2350 = 0x000A;

        // PICOBOOT protocol constants
        private const uint PicobootMagic = 0x431FD10B;
        private const int CommandSize = 32;
        private const int StatusSize = 16;
        private const int PageSize = 256;
        private const int FlashSectorSize = 4096;
        private const uint DefaultFlashBase = 0x10000000;

        // PICOBOOT command IDs
        private const byte CmdExclusiveAccess = 0x01;
        private const byte CmdReboot = 0x02;
        private const byte CmdFlashErase = 0x03;
        private const byte CmdRead = 0x84;
        private const byte CmdWrite = 0x05;
        private const byte CmdExitXip = 0x06;
        private const byte CmdEnterCmdXip = 0x07;
        private const byte CmdExec = 0x08;
        private const byte CmdVectorizeFlash = 0x09;
        private const byte CmdReboot2 = 0x0A;
        private const byte CmdGetInfo = 0x8B;
        private const byte CmdOtpRead = 0x8C;

        /// <summary>GET_INFO type flag: system info (chip, ROM version, flash).</summary>
        public const uint InfoTypeSys = 0x01;

        /// <summary>GET_INFO type flag: partition table info.</summary>
        public const uint InfoTypePartitions = 0x02;

        /// <summary>GET_INFO type flag: UF2 download status.</summary>
        public const uint InfoTypeUf2Status = 0x04;

        // PICOBOOT interface number.
        // On RP2040/RP2350 in BOOTSEL mode, the USB device exposes:
        //   Interface 0: Mass Storage (UF2)
        //   Interface 1: PICOBOOT vendor-specific bulk interface
        // The picotool source (picoboot_connection_cxx.cpp) claims interface 1.
        // When running with stdio USB, the PICOBOOT interface may shift to #2.
        // Default to 1 for BOOTSEL mode; override if needed.
        private const int PicobootInterfaceNumber = 1;

        // Transfer timeout in milliseconds
        private const int TransferTimeoutMs = 5000;

        #endregion

        #region Fields

        private UsbDevice _usbDevice;
        private UsbEndpointWriter _writer;
        private UsbEndpointReader _reader;
        private uint _tokenCounter;
        private bool _disposed;
        private readonly string _chipType;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the chip type of the connected device ("RP2040" or "RP2350").
        /// </summary>
        public string ChipType => _chipType;

        /// <summary>
        /// Gets whether this device is connected and ready.
        /// </summary>
        public bool IsConnected => _usbDevice != null && _usbDevice.IsOpen;

        #endregion

        #region Constructor / Dispose

        /// <summary>
        /// Opens a PICOBOOT connection to a Pico device.
        /// </summary>
        /// <param name="usbDevice">The USB device to connect to.</param>
        /// <param name="chipType">The chip type ("RP2040" or "RP2350").</param>
        private PicoBootDevice(UsbDevice usbDevice, string chipType)
        {
            _usbDevice = usbDevice;
            _chipType = chipType;
            _tokenCounter = 1;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the USB device.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                try { _reader?.Dispose(); } catch (Exception) { }
                try { _writer?.Dispose(); } catch (Exception) { }

                _reader = null;
                _writer = null;

                CloseDevice();
            }

            _disposed = true;
        }

        #endregion

        #region Static Factory / Discovery

        /// <summary>
        /// Finds all Pico devices in BOOTSEL mode connected via USB.
        /// </summary>
        /// <returns>List of device info for each detected PICOBOOT device.</returns>
        public static List<PicoBootDeviceInfo> FindDevices()
        {
            var devices = new List<PicoBootDeviceInfo>();

            UsbRegDeviceList allDevices = UsbDevice.AllDevices;

            foreach (UsbRegistry regDevice in allDevices)
            {
                if (regDevice.Vid != VendorIdRaspberryPi)
                {
                    continue;
                }

                string chipType = null;

                if (regDevice.Pid == ProductIdRp2040UsbBoot)
                {
                    chipType = "RP2040";
                }
                else if (regDevice.Pid == ProductIdRp2350UsbBoot)
                {
                    chipType = "RP2350";
                }

                if (chipType != null)
                {
                    devices.Add(new PicoBootDeviceInfo(
                        chipType,
                        regDevice.Vid,
                        regDevice.Pid,
                        regDevice.DevicePath));
                }
            }

            return devices;
        }

        /// <summary>
        /// Finds Pico devices running with USB stdio (nanoFramework running) that can be force-rebooted.
        /// </summary>
        /// <returns>List of device info for force-rebootable devices.</returns>
        public static List<PicoBootDeviceInfo> FindRunningDevices()
        {
            var devices = new List<PicoBootDeviceInfo>();

            UsbRegDeviceList allDevices = UsbDevice.AllDevices;

            foreach (UsbRegistry regDevice in allDevices)
            {
                if (regDevice.Vid != VendorIdRaspberryPi)
                {
                    continue;
                }

                string chipType = null;

                if (regDevice.Pid == ProductIdStdioUsb)
                {
                    chipType = "RP2040";
                }
                else if (regDevice.Pid == ProductIdStdioUsbRp2350)
                {
                    chipType = "RP2350";
                }

                if (chipType != null)
                {
                    devices.Add(new PicoBootDeviceInfo(
                        chipType,
                        regDevice.Vid,
                        regDevice.Pid,
                        regDevice.DevicePath));
                }
            }

            return devices;
        }

        /// <summary>
        /// Opens and connects to the first available Pico device in BOOTSEL mode.
        /// </summary>
        /// <returns>A connected <see cref="PicoBootDevice"/>, or <c>null</c> if none found.</returns>
        public static PicoBootDevice OpenFirst()
        {
            var devices = FindDevices();

            if (devices.Count == 0)
            {
                return null;
            }

            return Open(devices[0]);
        }

        /// <summary>
        /// Opens a specific Pico device.
        /// </summary>
        /// <param name="deviceInfo">Device info from <see cref="FindDevices"/>.</param>
        /// <returns>A connected <see cref="PicoBootDevice"/>, or <c>null</c> if connection failed.</returns>
        public static PicoBootDevice Open(PicoBootDeviceInfo deviceInfo)
        {
            UsbDeviceFinder finder = new UsbDeviceFinder(deviceInfo.Vid, deviceInfo.Pid);
            UsbDevice usbDevice = UsbDevice.OpenUsbDevice(finder);

            if (usbDevice == null)
            {
                return null;
            }

            var device = new PicoBootDevice(usbDevice, deviceInfo.ChipType);

            if (!device.InitializeEndpoints())
            {
                device.Dispose();
                return null;
            }

            return device;
        }

        #endregion

        #region Flash Operations

        /// <summary>
        /// Erases a region of flash memory. Address and length must be 4096-byte aligned.
        /// </summary>
        /// <param name="address">Start address in flash (must be 4096-byte aligned).</param>
        /// <param name="length">Number of bytes to erase (must be 4096-byte aligned).</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes FlashErase(uint address, uint length)
        {
            if (address % FlashSectorSize != 0 || length % FlashSectorSize != 0)
            {
                return ExitCodes.E3003;
            }

            byte[] args = new byte[16];
            WriteUInt32(args, 0, address);
            WriteUInt32(args, 4, length);

            return SendCommand(CmdFlashErase, 0, args);
        }

        /// <summary>
        /// Writes data to flash memory. Data is written in 256-byte pages.
        /// Flash must be erased before writing.
        /// </summary>
        /// <param name="address">Start address in flash.</param>
        /// <param name="data">Data to write.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes FlashWrite(uint address, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return ExitCodes.OK;
            }

            // write in page-sized chunks
            int offset = 0;

            while (offset < data.Length)
            {
                int chunkSize = Math.Min(PageSize, data.Length - offset);
                byte[] chunk = new byte[chunkSize];
                Array.Copy(data, offset, chunk, 0, chunkSize);

                byte[] args = new byte[16];
                WriteUInt32(args, 0, address + (uint)offset);
                WriteUInt32(args, 4, (uint)chunkSize);

                ExitCodes result = SendCommandWithData(CmdWrite, (uint)chunkSize, args, chunk);

                if (result != ExitCodes.OK)
                {
                    return result;
                }

                offset += chunkSize;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Reads data from flash or RAM.
        /// </summary>
        /// <param name="address">Start address to read from.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>The data read, or <c>null</c> on failure.</returns>
        public byte[] FlashRead(uint address, uint length)
        {
            byte[] args = new byte[16];
            WriteUInt32(args, 0, address);
            WriteUInt32(args, 4, length);

            byte[] result = SendCommandWithRead(CmdRead, length, args);

            return result;
        }

        /// <summary>
        /// Updates firmware by erasing the required flash region and writing new data.
        /// </summary>
        /// <param name="firmwareBin">Raw firmware binary data.</param>
        /// <param name="baseAddress">Flash base address (default: 0x10000000).</param>
        /// <param name="verbosity">Verbosity level for progress output.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes UpdateFirmware(byte[] firmwareBin, uint baseAddress = DefaultFlashBase, VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            if (firmwareBin == null || firmwareBin.Length == 0)
            {
                return ExitCodes.E3003;
            }

            // 1. Acquire exclusive access
            ExitCodes result = ExclusiveAccess(true);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            // 2. Exit XIP mode
            result = ExitXip();

            if (result != ExitCodes.OK)
            {
                ExclusiveAccess(false);
                return result;
            }

            // 3. Erase flash — round up to sector boundary
            uint eraseLength = ((uint)firmwareBin.Length + FlashSectorSize - 1) & ~((uint)FlashSectorSize - 1);

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Erasing {eraseLength / 1024}KB at 0x{baseAddress:X8}...");
            }

            result = FlashErase(baseAddress, eraseLength);

            if (result != ExitCodes.OK)
            {
                ExclusiveAccess(false);
                return result;
            }

            // 4. Write firmware in pages
            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Writing {firmwareBin.Length} bytes...");
            }

            result = FlashWrite(baseAddress, firmwareBin);

            if (result != ExitCodes.OK)
            {
                ExclusiveAccess(false);
                return result;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Flash write complete.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            // 5. Release exclusive access
            ExclusiveAccess(false);

            return ExitCodes.OK;
        }

        /// <summary>
        /// Verifies firmware by reading back flash contents and comparing.
        /// </summary>
        /// <param name="firmwareBin">Expected firmware binary data.</param>
        /// <param name="baseAddress">Flash base address (default: 0x10000000).</param>
        /// <param name="verbosity">Verbosity level for progress output.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes VerifyFirmware(byte[] firmwareBin, uint baseAddress = DefaultFlashBase, VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            if (firmwareBin == null || firmwareBin.Length == 0)
            {
                return ExitCodes.E3003;
            }

            // exit XIP for raw flash access
            ExitCodes result = ExclusiveAccess(true);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            result = ExitXip();

            if (result != ExitCodes.OK)
            {
                ExclusiveAccess(false);
                return result;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Verifying {firmwareBin.Length} bytes at 0x{baseAddress:X8}...");
            }

            byte[] readBack = FlashRead(baseAddress, (uint)firmwareBin.Length);

            ExclusiveAccess(false);

            if (readBack == null || readBack.Length != firmwareBin.Length)
            {
                return ExitCodes.E3003;
            }

            for (int i = 0; i < firmwareBin.Length; i++)
            {
                if (readBack[i] != firmwareBin[i])
                {
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"Verification failed at offset 0x{i:X8}.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    return ExitCodes.E3003;
                }
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Verification passed.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Erases all flash contents on the device.
        /// </summary>
        /// <param name="flashSizeBytes">Total flash size to erase (default: 2MB).</param>
        /// <param name="verbosity">Verbosity level for progress output.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes MassErase(uint flashSizeBytes = 2 * 1024 * 1024, VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            ExitCodes result = ExclusiveAccess(true);

            if (result != ExitCodes.OK)
            {
                return result;
            }

            result = ExitXip();

            if (result != ExitCodes.OK)
            {
                ExclusiveAccess(false);
                return result;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"Erasing all flash ({flashSizeBytes / 1024}KB)...");
            }

            result = FlashErase(DefaultFlashBase, flashSizeBytes);

            ExclusiveAccess(false);

            if (result == ExitCodes.OK && verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Mass erase complete.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            return result;
        }

        #endregion

        #region Device Control

        /// <summary>
        /// Sets exclusive access mode (locks mass storage writes).
        /// </summary>
        /// <param name="exclusive"><see langword="true"/> to acquire exclusive access, <see langword="false"/> to release.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes ExclusiveAccess(bool exclusive)
        {
            byte[] args = new byte[16];
            args[0] = (byte)(exclusive ? 1 : 0);

            return SendCommand(CmdExclusiveAccess, 0, args);
        }

        /// <summary>
        /// Reboots the device.
        /// </summary>
        /// <param name="pc">Program counter value (0 for default boot).</param>
        /// <param name="sp">Stack pointer value (0 for default).</param>
        /// <param name="delayMs">Delay in milliseconds before reboot.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes Reboot(uint pc = 0, uint sp = 0, uint delayMs = 500)
        {
            byte[] args = new byte[16];
            WriteUInt32(args, 0, pc);
            WriteUInt32(args, 4, sp);
            WriteUInt32(args, 8, delayMs);

            // reboot command — device will disconnect, so IO errors are expected
            try
            {
                SendCommand(CmdReboot, 0, args);
            }
            catch (System.IO.IOException)
            {
                // expected — device disconnects during reboot
            }
            catch (LibUsbDotNet.Main.UsbException)
            {
                // expected — USB pipe breaks when device disconnects
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Extended reboot command (RP2350 only).
        /// </summary>
        /// <param name="flags">Reboot flags.</param>
        /// <param name="delayMs">Delay before reboot.</param>
        /// <param name="param0">First parameter.</param>
        /// <param name="param1">Second parameter.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes Reboot2(uint flags, uint delayMs, uint param0, uint param1)
        {
            byte[] args = new byte[16];
            WriteUInt32(args, 0, flags);
            WriteUInt32(args, 4, delayMs);
            WriteUInt32(args, 8, param0);
            WriteUInt32(args, 12, param1);

            try
            {
                SendCommand(CmdReboot2, 0, args);
            }
            catch (System.IO.IOException)
            {
                // expected — device disconnects during reboot
            }
            catch (LibUsbDotNet.Main.UsbException)
            {
                // expected — USB pipe breaks when device disconnects
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Force-reboots a running Pico device into BOOTSEL mode.
        /// </summary>
        /// <param name="deviceInfo">Running device info from <see cref="FindRunningDevices"/>.</param>
        /// <param name="timeoutMs">Timeout to wait for device to re-enumerate in BOOTSEL mode.</param>
        /// <returns>Exit code indicating result.</returns>
        public static ExitCodes ForceBootsel(PicoBootDeviceInfo deviceInfo, int timeoutMs = 10_000)
        {
            PicoBootDevice runningDevice = Open(deviceInfo);

            if (runningDevice == null)
            {
                return ExitCodes.E3004;
            }

            using (runningDevice)
            {
                // reboot into BOOTSEL mode
                if (deviceInfo.ChipType == "RP2350")
                {
                    // RP2350: use Reboot2 with BOOTSEL flags
                    runningDevice.Reboot2(0x02, 500, 0, 0);
                }
                else
                {
                    // RP2040: standard reboot into USB boot
                    runningDevice.Reboot(0, 0, 500);
                }
            }

            // wait for device to re-enumerate in BOOTSEL mode
            int elapsed = 0;
            const int pollInterval = 500;

            while (elapsed < timeoutMs)
            {
                Thread.Sleep(pollInterval);
                elapsed += pollInterval;

                var bootselDevices = FindDevices();

                if (bootselDevices.Count > 0)
                {
                    return ExitCodes.OK;
                }
            }

            return ExitCodes.E3005;
        }

        /// <summary>
        /// Exits XIP (execute-in-place) mode for raw flash access.
        /// </summary>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes ExitXip()
        {
            return SendCommand(CmdExitXip, 0, new byte[16]);
        }

        /// <summary>
        /// Enters command XIP mode.
        /// </summary>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes EnterCmdXip()
        {
            return SendCommand(CmdEnterCmdXip, 0, new byte[16]);
        }

        /// <summary>
        /// Execute code at the specified address.
        /// </summary>
        /// <param name="address">Address to execute.</param>
        /// <returns>Exit code indicating result.</returns>
        public ExitCodes Exec(uint address)
        {
            byte[] args = new byte[16];
            WriteUInt32(args, 0, address);

            return SendCommand(CmdExec, 0, args);
        }

        /// <summary>
        /// Queries device info from the ROM bootloader (RP2350 only).
        /// </summary>
        /// <param name="infoType">Info type flags (use <see cref="InfoTypeSys"/>, <see cref="InfoTypePartitions"/>, etc.).</param>
        /// <param name="maxResponseLength">Maximum response buffer size.</param>
        /// <returns>Raw response bytes, or <c>null</c> on failure or if device is RP2040.</returns>
        public byte[] GetInfo(uint infoType, uint maxResponseLength = 256)
        {
            if (_chipType != "RP2350")
            {
                return null;
            }

            byte[] args = new byte[16];
            WriteUInt32(args, 0, infoType);

            return SendCommandWithRead(CmdGetInfo, maxResponseLength, args);
        }

        /// <summary>
        /// Reads OTP (One-Time-Programmable) memory rows (RP2350 only).
        /// </summary>
        /// <param name="startRow">First OTP row to read (0-based).</param>
        /// <param name="count">Number of rows to read.</param>
        /// <param name="ecc">If <see langword="true"/>, returns ECC-corrected 16-bit values (2 bytes/row);
        /// if <see langword="false"/>, returns raw 32-bit fuse values (4 bytes/row).</param>
        /// <returns>Raw OTP data, or <c>null</c> on failure or if device is RP2040.</returns>
        public byte[] OtpRead(ushort startRow, ushort count, bool ecc = true)
        {
            if (_chipType != "RP2350")
            {
                return null;
            }

            const ushort maxOtpRows = 8192;

            if (count == 0 || startRow >= maxOtpRows || startRow + count > maxOtpRows)
            {
                return null;
            }

            byte[] args = new byte[16];
            args[0] = (byte)(startRow & 0xFF);
            args[1] = (byte)((startRow >> 8) & 0xFF);
            args[2] = (byte)(count & 0xFF);
            args[3] = (byte)((count >> 8) & 0xFF);
            args[4] = (byte)(ecc ? 0x01 : 0x00);

            uint transferLength = ecc ? (uint)(count * 2) : (uint)(count * 4);

            return SendCommandWithRead(CmdOtpRead, transferLength, args);
        }

        /// <summary>
        /// Queries extended device information by combining GET_INFO and OTP reads (RP2350 only).
        /// For RP2040 devices, returns a minimal info object.
        /// </summary>
        /// <returns>Extended device info, never <c>null</c>.</returns>
        public PicoDeviceExtendedInfo QueryExtendedInfo()
        {
            byte[] sysInfo = null;
            byte[] partitionInfo = null;
            byte[] otpCritical = null;

            if (_chipType == "RP2350")
            {
                sysInfo = GetInfo(InfoTypeSys);
                partitionInfo = GetInfo(InfoTypePartitions);
                otpCritical = OtpRead(
                    PicoDeviceExtendedInfo.OtpRowCrit0,
                    PicoDeviceExtendedInfo.OtpCritRowCount,
                    ecc: true);
            }

            return new PicoDeviceExtendedInfo(_chipType, sysInfo, partitionInfo, otpCritical);
        }

        #endregion

        #region Private Protocol Implementation

        private bool InitializeEndpoints()
        {
            try
            {
                // for whole USB devices, claim the PICOBOOT interface
                if (_usbDevice is IUsbDevice wholeDevice)
                {
                    wholeDevice.SetConfiguration(1);
                    wholeDevice.ClaimInterface(PicobootInterfaceNumber);
                }

                // PICOBOOT uses bulk endpoints on interface 1
                // OUT endpoint (0x03) for commands and write data
                // IN endpoint (0x84) for read data and status
                _writer = _usbDevice.OpenEndpointWriter(WriteEndpointID.Ep03);
                _reader = _usbDevice.OpenEndpointReader(ReadEndpointID.Ep04);

                return _writer != null && _reader != null;
            }
            catch (UnauthorizedAccessException)
            {
                // USB driver permissions issue (common on Linux without udev rules)
                return false;
            }
            catch (System.IO.IOException)
            {
                // USB interface not available or already claimed
                return false;
            }
        }

        private void CloseDevice()
        {
            try
            {
                if (_usbDevice != null)
                {
                    if (_usbDevice is IUsbDevice wholeDevice)
                    {
                        wholeDevice.ReleaseInterface(PicobootInterfaceNumber);
                    }

                    _usbDevice.Close();
                    _usbDevice = null;
                }
            }
            catch (Exception)
            {
                // best effort cleanup
            }
        }

        private ExitCodes SendCommand(byte cmdId, uint transferLength, byte[] args)
        {
            byte[] cmdPacket = BuildCommandPacket(cmdId, transferLength, args);

            int bytesWritten;
            ErrorCode ec = _writer.Write(cmdPacket, TransferTimeoutMs, out bytesWritten);

            if (ec != ErrorCode.None || bytesWritten != CommandSize)
            {
                return ExitCodes.E3002;
            }

            // read status if no data transfer is expected
            if (transferLength == 0)
            {
                return ReadStatus();
            }

            return ExitCodes.OK;
        }

        private ExitCodes SendCommandWithData(byte cmdId, uint transferLength, byte[] args, byte[] data)
        {
            byte[] cmdPacket = BuildCommandPacket(cmdId, transferLength, args);

            int bytesWritten;
            ErrorCode ec = _writer.Write(cmdPacket, TransferTimeoutMs, out bytesWritten);

            if (ec != ErrorCode.None || bytesWritten != CommandSize)
            {
                return ExitCodes.E3002;
            }

            // write data phase
            ec = _writer.Write(data, TransferTimeoutMs, out bytesWritten);

            if (ec != ErrorCode.None || bytesWritten != data.Length)
            {
                return ExitCodes.E3002;
            }

            return ReadStatus();
        }

        private byte[] SendCommandWithRead(byte cmdId, uint transferLength, byte[] args)
        {
            byte[] cmdPacket = BuildCommandPacket(cmdId, transferLength, args);

            int bytesWritten;
            ErrorCode ec = _writer.Write(cmdPacket, TransferTimeoutMs, out bytesWritten);

            if (ec != ErrorCode.None || bytesWritten != CommandSize)
            {
                return null;
            }

            // read data phase
            byte[] buffer = new byte[transferLength];
            int bytesRead;
            ec = _reader.Read(buffer, TransferTimeoutMs, out bytesRead);

            if (ec != ErrorCode.None)
            {
                return null;
            }

            // read trailing status
            ReadStatus();

            if (bytesRead < transferLength)
            {
                byte[] trimmed = new byte[bytesRead];
                Array.Copy(buffer, trimmed, bytesRead);
                return trimmed;
            }

            return buffer;
        }

        private ExitCodes ReadStatus()
        {
            byte[] statusBuffer = new byte[StatusSize];
            int bytesRead;
            ErrorCode ec = _reader.Read(statusBuffer, TransferTimeoutMs, out bytesRead);

            if (ec != ErrorCode.None || bytesRead < 8)
            {
                return ExitCodes.E3002;
            }

            // check status code at offset 4
            uint statusCode = ReadUInt32(statusBuffer, 4);

            return statusCode == 0 ? ExitCodes.OK : ExitCodes.E3002;
        }

        private byte[] BuildCommandPacket(byte cmdId, uint transferLength, byte[] args)
        {
            byte[] packet = new byte[CommandSize];

            // magic
            WriteUInt32(packet, 0, PicobootMagic);

            // token
            uint token = _tokenCounter++;
            WriteUInt32(packet, 4, token);

            // command ID
            packet[8] = cmdId;

            // command argument size (bytes of args used)
            packet[9] = (byte)(args != null ? Math.Min(args.Length, 16) : 0);

            // reserved
            packet[10] = 0;
            packet[11] = 0;

            // transfer length
            WriteUInt32(packet, 12, transferLength);

            // args (up to 16 bytes)
            if (args != null)
            {
                int copyLen = Math.Min(args.Length, 16);
                Array.Copy(args, 0, packet, 16, copyLen);
            }

            return packet;
        }

        #endregion

        #region Utility

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }

        #endregion
    }

    /// <summary>
    /// Information about a Pico device discovered via USB enumeration.
    /// </summary>
    public class PicoBootDeviceInfo
    {
        /// <summary>Gets the chip type ("RP2040" or "RP2350").</summary>
        public string ChipType { get; }

        /// <summary>Gets the USB vendor ID.</summary>
        public int Vid { get; }

        /// <summary>Gets the USB product ID.</summary>
        public int Pid { get; }

        /// <summary>Gets the USB device path.</summary>
        public string DevicePath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PicoBootDeviceInfo"/> class.
        /// </summary>
        public PicoBootDeviceInfo(string chipType, int vid, int pid, string devicePath)
        {
            ChipType = chipType;
            Vid = vid;
            Pid = pid;
            DevicePath = devicePath;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{ChipType} (VID:0x{Vid:X4} PID:0x{Pid:X4}) at {DevicePath}";
        }
    }
}
