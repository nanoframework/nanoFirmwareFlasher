//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// Platform abstraction for USB HID device access used by the CMSIS-DAP transport.
    /// Implementations exist for Windows (hid.dll), Linux (hidraw), and macOS (IOKit).
    /// </summary>
    internal interface IHidDevice : IDisposable
    {
        /// <summary>
        /// Opens a HID device by its platform-specific path.
        /// </summary>
        /// <param name="devicePath">Platform-specific device path (e.g., \\?\hid#... on Windows, /dev/hidrawN on Linux).</param>
        void Open(string devicePath);

        /// <summary>
        /// Writes a full HID output report (including the report ID byte at index 0).
        /// </summary>
        /// <param name="report">The output report buffer.</param>
        /// <returns>True if the write succeeded.</returns>
        bool Write(byte[] report);

        /// <summary>
        /// Reads a full HID input report (including the report ID byte at index 0).
        /// </summary>
        /// <param name="buffer">Buffer to receive the input report.</param>
        /// <returns>Number of bytes read.</returns>
        int Read(byte[] buffer);

        /// <summary>
        /// Gets the product name of the opened device.
        /// </summary>
        string ProductName { get; }

        /// <summary>
        /// Gets the serial number of the opened device.
        /// </summary>
        string SerialNumber { get; }

        /// <summary>
        /// Gets the HID output report byte length (including the report ID byte).
        /// Default is 65 (1 byte report ID + 64 bytes data) for most CMSIS-DAP v1 probes.
        /// </summary>
        int ReportSize { get; }
    }

    /// <summary>
    /// Factory for creating platform-specific <see cref="IHidDevice"/> instances
    /// and enumerating connected CMSIS-DAP HID devices.
    /// </summary>
    internal static class HidDeviceFactory
    {
        /// <summary>
        /// Creates a platform-appropriate HID device instance.
        /// </summary>
        internal static IHidDevice Create()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return new WindowsHidDevice();
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return new LinuxHidDevice();
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return new MacHidDevice();
            }

            throw new PlatformNotSupportedException(
                "HID device access is not supported on this platform.");
        }

        /// <summary>
        /// Enumerates connected CMSIS-DAP HID devices on the current platform.
        /// </summary>
        /// <returns>List of (productName, serialNumber, devicePath) tuples.</returns>
        internal static List<(string productName, string serialNumber, string devicePath)> Enumerate()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return WindowsHidDevice.EnumerateDevices();
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return LinuxHidDevice.EnumerateDevices();
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return MacHidDevice.EnumerateDevices();
            }

            return new List<(string, string, string)>();
        }
    }
}
