//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher.UsbDfu
{
    /// <summary>
    /// Platform abstraction for USB device access used by the DFU protocol.
    /// Implementations exist for Windows (WinUSB), Linux (libusb), and macOS (libusb).
    /// </summary>
    internal interface IUsbDevice : IDisposable
    {
        /// <summary>
        /// Opens a USB device by its platform-specific path.
        /// </summary>
        /// <param name="devicePath">The platform-specific device identifier.</param>
        void Open(string devicePath);

        /// <summary>
        /// Sends a USB control transfer from host to device (OUT).
        /// </summary>
        /// <param name="requestType">bmRequestType byte.</param>
        /// <param name="request">bRequest byte.</param>
        /// <param name="value">wValue.</param>
        /// <param name="index">wIndex.</param>
        /// <param name="data">Data to send (may be null for zero-length).</param>
        /// <param name="length">Number of bytes to send.</param>
        void ControlTransferOut(byte requestType, byte request, ushort value, ushort index, byte[] data, int length);

        /// <summary>
        /// Sends a USB control transfer from device to host (IN).
        /// </summary>
        /// <param name="requestType">bmRequestType byte.</param>
        /// <param name="request">bRequest byte.</param>
        /// <param name="value">wValue.</param>
        /// <param name="index">wIndex.</param>
        /// <param name="buffer">Buffer to receive data.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>Number of bytes actually transferred.</returns>
        int ControlTransferIn(byte requestType, byte request, ushort value, ushort index, byte[] buffer, int length);

        /// <summary>
        /// Reads the USB device descriptor (18 bytes).
        /// </summary>
        /// <param name="buffer">Buffer to receive the descriptor (at least 18 bytes).</param>
        /// <returns>Number of bytes transferred.</returns>
        int GetDeviceDescriptor(byte[] buffer);

        /// <summary>
        /// Reads a USB string descriptor.
        /// </summary>
        /// <param name="index">String descriptor index.</param>
        /// <param name="languageId">Language ID (e.g. 0x0409 for English US).</param>
        /// <param name="buffer">Buffer to receive the descriptor.</param>
        /// <returns>Number of bytes transferred.</returns>
        int GetStringDescriptor(byte index, ushort languageId, byte[] buffer);
    }

    /// <summary>
    /// Factory for creating <see cref="IUsbDevice"/> instances
    /// and enumerating connected DFU devices.
    /// Uses LibUsbDotNet for cross-platform USB access.
    /// </summary>
    internal static class UsbDeviceFactory
    {
        /// <summary>
        /// Creates a USB device instance using LibUsbDotNet.
        /// </summary>
        internal static IUsbDevice Create()
        {
            return new LibUsbDotNetDevice();
        }

        /// <summary>
        /// Enumerates connected STM32 DFU devices.
        /// </summary>
        /// <returns>List of (serialNumber, devicePath) tuples.</returns>
        internal static List<(string serial, string devicePath)> Enumerate()
        {
            return LibUsbDotNetDevice.EnumerateDevices();
        }
    }
}
