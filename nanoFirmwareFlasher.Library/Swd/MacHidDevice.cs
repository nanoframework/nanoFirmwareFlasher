//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// macOS HID device implementation using IOKit HID Manager (IOKit.framework).
    /// </summary>
    internal class MacHidDevice : IHidDevice
    {
        #region IOKit / CoreFoundation P/Invoke

        private const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        // IOHIDDevice report types
        private const int kIOHIDReportTypeOutput = 1;
        private const int kIOHIDReportTypeInput = 0;

        // IOReturn success
        private const int kIOReturnSuccess = 0;

        // kIOHIDOptionsTypeSeizeDevice
        private const int kIOHIDOptionsTypeNone = 0;

        // CFString encoding
        private const uint kCFStringEncodingUTF8 = 0x08000100;

        // IOKit HID property keys
        private static readonly IntPtr kIOHIDProductKey = CFStringCreateWithCString("Product");
        private static readonly IntPtr kIOHIDSerialNumberKey = CFStringCreateWithCString("SerialNumber");
        private static readonly IntPtr kIOHIDMaxOutputReportSizeKey = CFStringCreateWithCString("MaxOutputReportSize");
        private static readonly IntPtr kIOHIDMaxInputReportSizeKey = CFStringCreateWithCString("MaxInputReportSize");
        private static readonly IntPtr kIOHIDTransportKey = CFStringCreateWithCString("Transport");

        // --- IOKit HID Manager ---
        [DllImport(IOKitLib)]
        private static extern IntPtr IOHIDManagerCreate(IntPtr allocator, int options);

        [DllImport(IOKitLib)]
        private static extern void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matchingDict);

        [DllImport(IOKitLib)]
        private static extern IntPtr IOHIDManagerCopyDevices(IntPtr manager);

        [DllImport(IOKitLib)]
        private static extern int IOHIDManagerOpen(IntPtr manager, int options);

        [DllImport(IOKitLib)]
        private static extern int IOHIDManagerClose(IntPtr manager, int options);

        // --- IOKit HID Device ---
        [DllImport(IOKitLib)]
        private static extern int IOHIDDeviceOpen(IntPtr device, int options);

        [DllImport(IOKitLib)]
        private static extern int IOHIDDeviceClose(IntPtr device, int options);

        [DllImport(IOKitLib)]
        private static extern int IOHIDDeviceSetReport(
            IntPtr device, int reportType, int reportID, byte[] report, IntPtr reportLength);

        [DllImport(IOKitLib)]
        private static extern int IOHIDDeviceGetReport(
            IntPtr device, int reportType, int reportID, byte[] report, ref IntPtr reportLength);

        [DllImport(IOKitLib)]
        private static extern IntPtr IOHIDDeviceGetProperty(IntPtr device, IntPtr key);

        // --- CoreFoundation ---
        [DllImport(CoreFoundationLib)]
        private static extern IntPtr CFSetGetCount(IntPtr theSet);

        [DllImport(CoreFoundationLib)]
        private static extern void CFSetGetValues(IntPtr theSet, IntPtr[] values);

        [DllImport(CoreFoundationLib)]
        private static extern void CFRelease(IntPtr cf);

        [DllImport(CoreFoundationLib)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

        [DllImport(CoreFoundationLib)]
        private static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, int bufferSize, uint encoding);

        [DllImport(CoreFoundationLib)]
        private static extern uint CFGetTypeID(IntPtr cf);

        [DllImport(CoreFoundationLib)]
        private static extern uint CFStringGetTypeID();

        [DllImport(CoreFoundationLib)]
        private static extern uint CFNumberGetTypeID();

        [DllImport(CoreFoundationLib)]
        private static extern bool CFNumberGetValue(IntPtr number, int theType, out int value);

        // CFNumberType: kCFNumberIntType = 9
        private const int kCFNumberIntType = 9;

        private static IntPtr CFStringCreateWithCString(string str)
        {
            return CFStringCreateWithCString(IntPtr.Zero, str, kCFStringEncodingUTF8);
        }

        #endregion

        private IntPtr _device = IntPtr.Zero;
        private bool _disposed;
        private int _reportSize = 65; // default: 1 byte report ID + 64 bytes data

        public string ProductName { get; private set; }

        public string SerialNumber { get; private set; }

        public int ReportSize => _reportSize;

        public void Open(string devicePath)
        {
            // On macOS, devicePath is an index into our enumeration results.
            // The actual IOHIDDevice reference is obtained via enumeration.
            // For Open(string path), we re-enumerate and find the matching device.
            var allDevices = EnumerateRawDevices();

            IntPtr targetDevice = IntPtr.Zero;

            foreach (var (product, serial, path, dev) in allDevices)
            {
                if (path == devicePath)
                {
                    targetDevice = dev;
                    ProductName = product;
                    SerialNumber = serial;
                    break;
                }
            }

            if (targetDevice == IntPtr.Zero)
            {
                throw new SwdProtocolException(
                    $"CMSIS-DAP device not found at path: {devicePath}");
            }

            int result = IOHIDDeviceOpen(targetDevice, kIOHIDOptionsTypeNone);

            if (result != kIOReturnSuccess)
            {
                throw new SwdProtocolException(
                    $"Failed to open IOKit HID device. IOReturn: 0x{result:X8}");
            }

            _device = targetDevice;

            // Get report size from device properties
            int outSize = GetIntProperty(_device, kIOHIDMaxOutputReportSizeKey);

            if (outSize > 0)
            {
                _reportSize = outSize + 1; // add 1 for report ID byte
            }
        }

        public bool Write(byte[] report)
        {
            // IOKit SetReport expects data without the report ID prefix for report ID 0
            // Send from index 1 (skip the report ID byte)
            int dataLength = report.Length - 1;
            byte[] data = new byte[dataLength];
            Buffer.BlockCopy(report, 1, data, 0, dataLength);

            int result = IOHIDDeviceSetReport(
                _device, kIOHIDReportTypeOutput, 0,
                data, new IntPtr(dataLength));

            return result == kIOReturnSuccess;
        }

        public int Read(byte[] buffer)
        {
            // IOKit GetReport expects buffer without report ID prefix
            int dataLength = buffer.Length - 1;
            byte[] data = new byte[dataLength];
            IntPtr length = new IntPtr(dataLength);

            int result = IOHIDDeviceGetReport(
                _device, kIOHIDReportTypeInput, 0,
                data, ref length);

            if (result != kIOReturnSuccess)
            {
                return 0;
            }

            // Copy data into buffer at offset 1 (report ID at index 0 is 0x00)
            buffer[0] = 0x00;
            int bytesRead = (int)length.ToInt64();
            Buffer.BlockCopy(data, 0, buffer, 1, Math.Min(bytesRead, buffer.Length - 1));
            return bytesRead + 1;
        }

        /// <summary>
        /// Enumerates all CMSIS-DAP HID devices on macOS.
        /// </summary>
        internal static List<(string productName, string serialNumber, string devicePath)> EnumerateDevices()
        {
            var result = new List<(string productName, string serialNumber, string devicePath)>();

            foreach (var (product, serial, path, _) in EnumerateRawDevices())
            {
                if (product != null && product.Contains("CMSIS-DAP"))
                {
                    result.Add((product, serial ?? string.Empty, path));
                }
            }

            return result;
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
                if (_device != IntPtr.Zero)
                {
                    IOHIDDeviceClose(_device, kIOHIDOptionsTypeNone);
                    _device = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        ~MacHidDevice()
        {
            Dispose(false);
        }

        #region Private helpers

        /// <summary>
        /// Enumerates all HID devices and returns raw device references.
        /// </summary>
        private static List<(string product, string serial, string path, IntPtr device)> EnumerateRawDevices()
        {
            var devices = new List<(string, string, string, IntPtr)>();

            IntPtr manager = IOHIDManagerCreate(IntPtr.Zero, kIOHIDOptionsTypeNone);

            if (manager == IntPtr.Zero)
            {
                return devices;
            }

            try
            {
                // Match all HID devices (null matching dictionary)
                IOHIDManagerSetDeviceMatching(manager, IntPtr.Zero);

                int openResult = IOHIDManagerOpen(manager, kIOHIDOptionsTypeNone);

                if (openResult != kIOReturnSuccess)
                {
                    return devices;
                }

                IntPtr deviceSet = IOHIDManagerCopyDevices(manager);

                if (deviceSet == IntPtr.Zero)
                {
                    IOHIDManagerClose(manager, kIOHIDOptionsTypeNone);
                    return devices;
                }

                try
                {
                    int count = (int)CFSetGetCount(deviceSet).ToInt64();

                    if (count == 0)
                    {
                        return devices;
                    }

                    IntPtr[] deviceRefs = new IntPtr[count];
                    CFSetGetValues(deviceSet, deviceRefs);

                    for (int i = 0; i < count; i++)
                    {
                        IntPtr dev = deviceRefs[i];

                        if (dev == IntPtr.Zero)
                        {
                            continue;
                        }

                        string product = GetStringProperty(dev, kIOHIDProductKey);
                        string serial = GetStringProperty(dev, kIOHIDSerialNumberKey);
                        string transport = GetStringProperty(dev, kIOHIDTransportKey);

                        // Generate a unique path: transport + index
                        string path = $"iokit:{transport ?? "USB"}:{i}";

                        devices.Add((product, serial, path, dev));
                    }
                }
                finally
                {
                    CFRelease(deviceSet);
                }
            }
            catch
            {
                // IOKit initialization failed — no HID support
            }

            return devices;
        }

        private static string GetStringProperty(IntPtr device, IntPtr key)
        {
            IntPtr value = IOHIDDeviceGetProperty(device, key);

            if (value == IntPtr.Zero)
            {
                return null;
            }

            if (CFGetTypeID(value) != CFStringGetTypeID())
            {
                return null;
            }

            byte[] buffer = new byte[256];

            if (CFStringGetCString(value, buffer, buffer.Length, kCFStringEncodingUTF8))
            {
                return Encoding.UTF8.GetString(buffer).TrimEnd('\0');
            }

            return null;
        }

        private static int GetIntProperty(IntPtr device, IntPtr key)
        {
            IntPtr value = IOHIDDeviceGetProperty(device, key);

            if (value == IntPtr.Zero)
            {
                return 0;
            }

            if (CFGetTypeID(value) != CFNumberGetTypeID())
            {
                return 0;
            }

            if (CFNumberGetValue(value, kCFNumberIntType, out int result))
            {
                return result;
            }

            return 0;
        }

        #endregion
    }
}
