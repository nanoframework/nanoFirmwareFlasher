//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// Windows HID device implementation using hid.dll, setupapi.dll, and kernel32.dll P/Invoke.
    /// </summary>
    internal class WindowsHidDevice : IHidDevice
    {
        #region P/Invoke declarations

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HidAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

        [DllImport("hid.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetProductString(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetSerialNumberString(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid, string enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize,
            out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(IntPtr handle, byte[] buffer, int numberOfBytesToWrite,
            out int numberOfBytesWritten, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(IntPtr handle, byte[] buffer, int numberOfBytesToRead,
            out int numberOfBytesRead, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int DIGCF_PRESENT = 0x02;
        private const int DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct HidAttributes
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        #endregion

        private IntPtr _deviceHandle = INVALID_HANDLE_VALUE;
        private bool _disposed;
        private int _reportSize = 65; // default: 1 byte report ID + 64 bytes data

        public string ProductName { get; private set; }

        public string SerialNumber { get; private set; }

        public int ReportSize => _reportSize;

        public void Open(string devicePath)
        {
            _deviceHandle = CreateFile(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (_deviceHandle == INVALID_HANDLE_VALUE)
            {
                int error = Marshal.GetLastWin32Error();
                throw new SwdProtocolException(
                    $"Failed to open HID device. Win32 error: {error}");
            }

            // Get HID report size
            if (HidD_GetPreparsedData(_deviceHandle, out IntPtr preparsedData))
            {
                try
                {
                    if (HidP_GetCaps(preparsedData, out HidpCaps caps) == 0x00110000) // HIDP_STATUS_SUCCESS
                    {
                        _reportSize = caps.OutputReportByteLength;
                    }
                }
                finally
                {
                    HidD_FreePreparsedData(preparsedData);
                }
            }

            ProductName = GetHidString(_deviceHandle, false) ?? "CMSIS-DAP";
            SerialNumber = GetHidString(_deviceHandle, true) ?? string.Empty;
        }

        public bool Write(byte[] report)
        {
            return WriteFile(_deviceHandle, report, report.Length, out _, IntPtr.Zero);
        }

        public int Read(byte[] buffer)
        {
            if (ReadFile(_deviceHandle, buffer, buffer.Length, out int bytesRead, IntPtr.Zero))
            {
                return bytesRead;
            }

            return 0;
        }

        /// <summary>
        /// Enumerates all CMSIS-DAP HID devices on Windows.
        /// </summary>
        internal static List<(string productName, string serialNumber, string devicePath)> EnumerateDevices()
        {
            var devices = new List<(string productName, string serialNumber, string devicePath)>();

            HidD_GetHidGuid(out Guid hidGuid);

            IntPtr deviceInfoSet = SetupDiGetClassDevs(
                ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == INVALID_HANDLE_VALUE)
            {
                return devices;
            }

            try
            {
                var interfaceData = new SpDeviceInterfaceData();
                interfaceData.cbSize = Marshal.SizeOf(interfaceData);

                int index = 0;

                while (SetupDiEnumDeviceInterfaces(
                    deviceInfoSet, IntPtr.Zero, ref hidGuid, index++, ref interfaceData))
                {
                    string devicePath = GetDevicePath(deviceInfoSet, ref interfaceData);

                    if (string.IsNullOrEmpty(devicePath))
                    {
                        continue;
                    }

                    IntPtr handle = CreateFile(
                        devicePath,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                    if (handle == INVALID_HANDLE_VALUE)
                    {
                        continue;
                    }

                    try
                    {
                        string product = GetHidString(handle, false);

                        if (product != null && product.Contains("CMSIS-DAP"))
                        {
                            string serial = GetHidString(handle, true);
                            devices.Add((product, serial ?? string.Empty, devicePath));
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
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
                if (_deviceHandle != INVALID_HANDLE_VALUE)
                {
                    CloseHandle(_deviceHandle);
                    _deviceHandle = INVALID_HANDLE_VALUE;
                }

                _disposed = true;
            }
        }

        ~WindowsHidDevice()
        {
            Dispose(false);
        }

        #region Private helpers

        private static string GetDevicePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData)
        {
            SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out int requiredSize, IntPtr.Zero);

            if (requiredSize <= 0)
            {
                return null;
            }

            IntPtr detailPtr = Marshal.AllocHGlobal(requiredSize);

            try
            {
                int cbSize = IntPtr.Size == 8 ? 8 : 6;
                Marshal.WriteInt32(detailPtr, cbSize);

                if (!SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet, ref interfaceData, detailPtr, requiredSize, out _, IntPtr.Zero))
                {
                    return null;
                }

                return Marshal.PtrToStringAuto(detailPtr + 4);
            }
            finally
            {
                Marshal.FreeHGlobal(detailPtr);
            }
        }

        private static string GetHidString(IntPtr deviceHandle, bool serial)
        {
            byte[] buffer = new byte[512];
            bool result;

            if (serial)
            {
                result = HidD_GetSerialNumberString(deviceHandle, buffer, buffer.Length);
            }
            else
            {
                result = HidD_GetProductString(deviceHandle, buffer, buffer.Length);
            }

            if (!result)
            {
                return null;
            }

            string str = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
            return string.IsNullOrEmpty(str) ? null : str;
        }

        #endregion
    }
}
