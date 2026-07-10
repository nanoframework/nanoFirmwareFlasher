//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Best-effort helper to detect whether a USB device is physically present on Windows,
    /// independently of which driver (if any) is bound to it. Used to distinguish
    /// "device not connected" from "device connected but missing a WinUSB-compatible driver",
    /// so the user can be pointed at the right remedy.
    /// </summary>
    public static class WindowsUsbScanner
    {
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint SPDRP_HARDWAREID = 0x00000001;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint CbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            IntPtr classGuid,
            string enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        /// <summary>
        /// Determines whether a USB device with the given vendor ID and any of the given product IDs
        /// is currently connected, regardless of the bound driver.
        /// </summary>
        /// <param name="vendorId">USB vendor ID.</param>
        /// <param name="productIds">One or more USB product IDs to match.</param>
        /// <returns><see langword="true"/> if a matching device is present (Windows only); otherwise <see langword="false"/>.</returns>
        public static bool IsUsbDevicePresent(ushort vendorId, ushort[] productIds)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                || productIds == null
                || productIds.Length == 0)
            {
                return false;
            }

            // Build the hardware-ID fragments to look for, e.g. "VID_0483&PID_374B".
            string[] needles = new string[productIds.Length];

            for (int i = 0; i < productIds.Length; i++)
            {
                needles[i] = $"VID_{vendorId:X4}&PID_{productIds[i]:X4}";
            }

            IntPtr deviceInfoSet = IntPtr.Zero;

            try
            {
                deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

                if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                {
                    return false;
                }

                var deviceInfo = new SP_DEVINFO_DATA();
                deviceInfo.CbSize = (uint)Marshal.SizeOf(deviceInfo);

                for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo); index++)
                {
                    string hardwareIds = GetHardwareIds(deviceInfoSet, ref deviceInfo);

                    if (string.IsNullOrEmpty(hardwareIds))
                    {
                        continue;
                    }

                    foreach (string needle in needles)
                    {
                        if (hardwareIds.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Best-effort only — any failure just means "can't tell", so report not present.
                return false;
            }
            finally
            {
                if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != new IntPtr(-1))
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }
            }

            return false;
        }

        private static string GetHardwareIds(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfo)
        {
            // First call to size the buffer.
            SetupDiGetDeviceRegistryProperty(
                deviceInfoSet,
                ref deviceInfo,
                SPDRP_HARDWAREID,
                out _,
                null,
                0,
                out uint requiredSize);

            if (requiredSize == 0
                || Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
            {
                return string.Empty;
            }

            byte[] buffer = new byte[requiredSize];

            if (!SetupDiGetDeviceRegistryProperty(
                    deviceInfoSet,
                    ref deviceInfo,
                    SPDRP_HARDWAREID,
                    out _,
                    buffer,
                    requiredSize,
                    out _))
            {
                return string.Empty;
            }

            // SPDRP_HARDWAREID is a REG_MULTI_SZ (UTF-16). Replace the embedded nulls with
            // spaces so all IDs are searchable in a single string.
            return Encoding.Unicode.GetString(buffer).Replace('\0', ' ');
        }
    }
}
