//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// Linux HID device implementation using /dev/hidraw and sysfs enumeration.
    /// Requires read/write access to /dev/hidraw* (configure via udev rules).
    /// </summary>
    internal class LinuxHidDevice : IHidDevice
    {
        #region libc P/Invoke

        private const string Libc = "libc";

        // Open flags
        private const int O_RDWR = 2;
        private const int O_NONBLOCK = 0x800;

        // ioctl request codes for hidraw
        // HIDIOCGRAWNAME(len) = _IOC(_IOC_READ, 'H', 0x04, len)
        // For len=256: direction=2 (READ), type='H'=0x48, nr=0x04, size=256=0x100
        // = (2 << 30) | (0x100 << 16) | (0x48 << 8) | 0x04 = 0x81004804
        // But Linux ioctl encoding on different architectures can vary.
        // We use a fixed-size version: HIDIOCGRAWNAME(256) 
        private static readonly uint HIDIOCGRAWNAME_256 = IoctionRead(0x48, 0x04, 256);

        // poll constants
        private const short POLLIN = 0x0001;

        [DllImport(Libc, SetLastError = true)]
        private static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

        [DllImport(Libc, SetLastError = true)]
        private static extern int close(int fd);

        [DllImport(Libc, SetLastError = true)]
        private static extern IntPtr write(int fd, byte[] buf, IntPtr count);

        [DllImport(Libc, SetLastError = true)]
        private static extern IntPtr read(int fd, byte[] buf, IntPtr count);

        [DllImport(Libc, SetLastError = true)]
        private static extern int ioctl(int fd, uint request, byte[] data);

        [DllImport(Libc, SetLastError = true)]
        private static extern int poll(ref PollFd fds, uint nfds, int timeout);

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd
        {
            public int fd;
            public short events;
            public short revents;
        }

        private static uint IoctionRead(uint type, uint nr, uint size)
        {
            // _IOC(_IOC_READ, type, nr, size) = (2 << 30) | (size << 16) | (type << 8) | nr
            return (2u << 30) | (size << 16) | (type << 8) | nr;
        }

        #endregion

        private int _fd = -1;
        private bool _disposed;
        private int _reportSize = 65; // default: 1 byte report ID + 64 bytes data

        public string ProductName { get; private set; }

        public string SerialNumber { get; private set; }

        public int ReportSize => _reportSize;

        public void Open(string devicePath)
        {
            _fd = open(devicePath, O_RDWR);

            if (_fd < 0)
            {
                int errno = Marshal.GetLastWin32Error();
                throw new SwdProtocolException(
                    $"Failed to open HID device at {devicePath}. errno: {errno}. " +
                    "Add a udev rule for CMSIS-DAP probe access: " +
                    "echo 'KERNEL==\"hidraw*\", SUBSYSTEM==\"hidraw\", MODE=\"0666\"' | sudo tee /etc/udev/rules.d/70-cmsis-dap.rules && sudo udevadm control --reload-rules. " +
                    "Alternatively, run with sudo.");
            }

            // Get product name via ioctl
            byte[] nameBuf = new byte[256];
            int nameLen = ioctl(_fd, HIDIOCGRAWNAME_256, nameBuf);

            if (nameLen > 0)
            {
                ProductName = Encoding.UTF8.GetString(nameBuf, 0, nameLen).TrimEnd('\0');
            }
            else
            {
                ProductName = "CMSIS-DAP";
            }

            // Try to get serial from sysfs
            SerialNumber = ReadSysfsSerial(devicePath) ?? string.Empty;

            // Report size: most CMSIS-DAP v1 probes use 64+1=65.
            // We keep the default and let protocol-level queries adjust if needed.
        }

        public bool Write(byte[] report)
        {
            IntPtr written = write(_fd, report, new IntPtr(report.Length));
            return written.ToInt64() == report.Length;
        }

        public int Read(byte[] buffer)
        {
            // Use poll to wait for data with a timeout (5 seconds)
            var pfd = new PollFd
            {
                fd = _fd,
                events = POLLIN,
                revents = 0
            };

            int pollResult = poll(ref pfd, 1, 5000);

            if (pollResult <= 0)
            {
                return 0;
            }

            IntPtr bytesRead = read(_fd, buffer, new IntPtr(buffer.Length));
            return (int)bytesRead.ToInt64();
        }

        /// <summary>
        /// Enumerates all CMSIS-DAP HID devices on Linux via /sys/class/hidraw.
        /// </summary>
        internal static List<(string productName, string serialNumber, string devicePath)> EnumerateDevices()
        {
            var devices = new List<(string productName, string serialNumber, string devicePath)>();

            const string sysClassHidraw = "/sys/class/hidraw";

            if (!Directory.Exists(sysClassHidraw))
            {
                return devices;
            }

            foreach (string hidrawDir in Directory.GetDirectories(sysClassHidraw))
            {
                string hidrawName = Path.GetFileName(hidrawDir);
                string devicePath = "/dev/" + hidrawName;

                if (!File.Exists(devicePath))
                {
                    continue;
                }

                // Read product name from uevent or by opening the device
                string productName = ReadUeventName(hidrawDir);

                if (productName == null)
                {
                    // Try opening the device to get the name via ioctl
                    productName = ReadNameViaIoctl(devicePath);
                }

                if (productName == null)
                {
                    continue;
                }

                // Filter for CMSIS-DAP
                if (!productName.Contains("CMSIS-DAP"))
                {
                    continue;
                }

                string serialNumber = ReadSysfsSerial(devicePath) ?? string.Empty;
                devices.Add((productName, serialNumber, devicePath));
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
                if (_fd >= 0)
                {
                    close(_fd);
                    _fd = -1;
                }

                _disposed = true;
            }
        }

        ~LinuxHidDevice()
        {
            Dispose(false);
        }

        #region Private helpers

        /// <summary>
        /// Reads the HID device name from the sysfs uevent file.
        /// </summary>
        private static string ReadUeventName(string hidrawSysDir)
        {
            // Navigate: /sys/class/hidraw/hidrawN/device/uevent
            string ueventPath = Path.Combine(hidrawSysDir, "device", "uevent");

            if (!File.Exists(ueventPath))
            {
                return null;
            }

            try
            {
                foreach (string line in File.ReadAllLines(ueventPath))
                {
                    if (line.StartsWith("HID_NAME="))
                    {
                        return line.Substring("HID_NAME=".Length);
                    }
                }
            }
            catch
            {
                // Permission denied or other I/O error
            }

            return null;
        }

        /// <summary>
        /// Reads the device name by opening the hidraw device and using ioctl.
        /// </summary>
        private static string ReadNameViaIoctl(string devicePath)
        {
            int fd = open(devicePath, O_RDWR);

            if (fd < 0)
            {
                return null;
            }

            try
            {
                byte[] nameBuf = new byte[256];
                int nameLen = ioctl(fd, HIDIOCGRAWNAME_256, nameBuf);

                if (nameLen > 0)
                {
                    return Encoding.UTF8.GetString(nameBuf, 0, nameLen).TrimEnd('\0');
                }
            }
            finally
            {
                close(fd);
            }

            return null;
        }

        /// <summary>
        /// Reads the USB serial number from sysfs by navigating to the parent USB device.
        /// Path: /sys/class/hidraw/hidrawN → resolve symlink → navigate to USB parent → read serial.
        /// </summary>
        private static string ReadSysfsSerial(string devicePath)
        {
            string hidrawName = Path.GetFileName(devicePath);

            // Navigate sysfs tree to find the USB device serial
            // /sys/class/hidraw/hidrawN/device/../../serial
            string sysPath = "/sys/class/hidraw/" + hidrawName;

            if (!Directory.Exists(sysPath))
            {
                return null;
            }

            // Walk up to find a 'serial' file in a USB device directory
            try
            {
                string deviceDir = Path.Combine(sysPath, "device");

                if (!Directory.Exists(deviceDir))
                {
                    return null;
                }

                // Try the parent and grandparent for the USB serial file
                for (int i = 0; i < 4; i++)
                {
                    string parentDir = deviceDir;

                    for (int j = 0; j < i; j++)
                    {
                        parentDir = Path.GetDirectoryName(parentDir);

                        if (parentDir == null)
                        {
                            break;
                        }
                    }

                    if (parentDir == null)
                    {
                        break;
                    }

                    string serialPath = Path.Combine(parentDir, "serial");

                    if (File.Exists(serialPath))
                    {
                        return File.ReadAllText(serialPath).Trim();
                    }
                }
            }
            catch
            {
                // Permission denied or other I/O error
            }

            return null;
        }

        #endregion
    }
}
