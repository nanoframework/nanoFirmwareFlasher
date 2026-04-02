//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace nanoFramework.Tools.FirmwareFlasher.UsbDfu
{
    /// <summary>
    /// Cross-platform USB device implementation using LibUsbDotNet.
    /// Works on Windows (WinUSB), Linux (libusb-1.0), and macOS (libusb-1.0).
    /// Replaces the per-platform WindowsUsbDevice and LibUsbDevice implementations.
    /// </summary>
    internal class LibUsbDotNetDevice : IUsbDevice
    {
        // STM32 DFU VID/PID
        private const int StmVid = 0x0483;
        private const int StmDfuPid = 0xDF11;

        private UsbDevice _device;
        private bool _disposed;

        public void Open(string devicePath)
        {
            // Find the STM32 DFU device
            var finder = new UsbDeviceFinder(StmVid, StmDfuPid);
            _device = UsbDevice.OpenUsbDevice(finder);

            if (_device == null)
            {
                throw new DfuOperationFailedException(
                    "No STM32 DFU device found. Make sure the device is in DFU mode. " +
                    "On Windows, install the WinUSB driver by running: nanoff --installdfudrivers, or use Zadig (https://zadig.akeo.ie). " +
                    "On Linux, install libusb: sudo apt install libusb-1.0-0 and add a udev rule for VID 0483. " +
                    "On macOS, install libusb: brew install libusb.");
            }

            // If this is a "whole" USB device (libusb), we need to claim the interface
            LibUsbDotNet.IUsbDevice wholeDevice = _device as LibUsbDotNet.IUsbDevice;

            if (wholeDevice != null)
            {
                wholeDevice.SetAutoDetachKernelDriver(true);
                wholeDevice.SetConfiguration(1);
                wholeDevice.ClaimInterface(0);
            }
        }

        public void ControlTransferOut(byte requestType, byte request, ushort value, ushort index, byte[] data, int length)
        {
            var setup = new UsbSetupPacket(requestType, request, value, index, length);

            bool success = _device.ControlTransfer(ref setup, data, length, out _);

            if (!success)
            {
                throw new DfuOperationFailedException(
                    $"USB control transfer OUT failed (request=0x{request:X2}, value=0x{value:X4}). " +
                    $"Error: {UsbDevice.LastErrorString}");
            }
        }

        public int ControlTransferIn(byte requestType, byte request, ushort value, ushort index, byte[] buffer, int length)
        {
            var setup = new UsbSetupPacket(requestType, request, value, index, length);

            bool success = _device.ControlTransfer(ref setup, buffer, length, out int transferred);

            if (!success)
            {
                throw new DfuOperationFailedException(
                    $"USB control transfer IN failed (request=0x{request:X2}, value=0x{value:X4}). " +
                    $"Error: {UsbDevice.LastErrorString}");
            }

            return transferred;
        }

        public int GetDeviceDescriptor(byte[] buffer)
        {
            // Use standard GET_DESCRIPTOR control transfer for device descriptor
            var setup = new UsbSetupPacket(
                0x80, // Device-to-host, Standard, Device
                0x06, // GET_DESCRIPTOR
                (0x01 << 8), // Device descriptor type
                0,
                Math.Min(buffer.Length, 18));

            bool success = _device.ControlTransfer(ref setup, buffer, Math.Min(buffer.Length, 18), out int transferred);

            if (!success)
            {
                throw new DfuOperationFailedException(
                    $"Failed to read USB device descriptor. Error: {UsbDevice.LastErrorString}");
            }

            return transferred;
        }

        public int GetStringDescriptor(byte index, ushort languageId, byte[] buffer)
        {
            if (index == 0)
            {
                return 0;
            }

            var setup = new UsbSetupPacket(
                0x80, // Device-to-host, Standard, Device
                0x06, // GET_DESCRIPTOR
                (0x03 << 8) | index, // String descriptor type | index
                languageId,
                buffer.Length);

            try
            {
                bool success = _device.ControlTransfer(ref setup, buffer, buffer.Length, out int transferred);
                return success && transferred > 0 ? transferred : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Enumerates STM32 DFU devices using LibUsbDotNet.
        /// </summary>
        internal static List<(string serial, string devicePath)> EnumerateDevices()
        {
            var devices = new List<(string serial, string devicePath)>();
            var finder = new UsbDeviceFinder(StmVid, StmDfuPid);

            UsbRegDeviceList allDevices = UsbDevice.AllDevices;
            UsbRegDeviceList matchingDevices = allDevices.FindAll(finder);

            foreach (UsbRegistry reg in matchingDevices)
            {
                string devicePath = reg.DevicePath ?? reg.SymbolicName ?? string.Empty;
                string serial = string.Empty;

                // Try to get serial from device info
                try
                {
                    UsbDevice dev = reg.Device;

                    if (dev != null)
                    {
                        if (dev.Info != null && !string.IsNullOrEmpty(dev.Info.SerialString))
                        {
                            serial = dev.Info.SerialString;
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

                devices.Add((serial, devicePath));
            }

            return devices;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _device != null)
                {
                    LibUsbDotNet.IUsbDevice wholeDevice2 = _device as LibUsbDotNet.IUsbDevice;

                    if (wholeDevice2 != null)
                    {
                        try
                        {
                            wholeDevice2.ReleaseInterface(0);
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

        ~LibUsbDotNetDevice()
        {
            Dispose(false);
        }
    }
}
