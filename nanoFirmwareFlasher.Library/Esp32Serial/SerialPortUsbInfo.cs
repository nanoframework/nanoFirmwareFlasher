// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

#if NET472
using Microsoft.Win32;
#endif

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Detects USB VID/PID for a serial port to identify the type of
    /// USB-to-serial adapter (bridge vs. native USB-JTAG/Serial).
    /// </summary>
    internal static class SerialPortUsbInfo
    {
        /// <summary>Espressif USB Vendor ID.</summary>
        internal const int EspressifVid = 0x303A;

        /// <summary>Espressif USB-JTAG/Serial peripheral Product ID (ESP32-C3, S3, C6, H2, P4).</summary>
        internal const int UsbJtagSerialPid = 0x1001;

        /// <summary>
        /// Some Espressif native USB boards expose a customer PID instead of 0x1001.
        /// Treat it as USB-Serial/JTAG-compatible for reset selection.
        /// </summary>
        internal const int UsbJtagSerialCompatiblePid = 0x4001;

        /// <summary>
        /// Check whether the given serial port is an Espressif USB-JTAG/Serial device.
        /// </summary>
        internal static bool IsUsbJtagSerial(string portName)
        {
            var (vid, pid) = GetUsbIds(portName);
            return vid == EspressifVid && IsUsbJtagSerialPid(pid);
        }

        internal static bool IsUsbJtagSerialPid(int pid)
        {
            return pid == UsbJtagSerialPid || pid == UsbJtagSerialCompatiblePid;
        }

        /// <summary>
        /// Check whether the given serial port is a native Espressif USB-OTG device for the specified chip image PID.
        /// </summary>
        internal static bool IsUsbOtg(string portName, uint chipId)
        {
            var (vid, pid) = GetUsbIds(portName);
            return vid == EspressifVid && pid == (int)chipId;
        }

        /// <summary>
        /// Get the USB VID and PID for a serial port.
        /// Returns (-1, -1) if the information cannot be determined.
        /// </summary>
        internal static (int vid, int pid) GetUsbIds(string portName)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return GetUsbIdsWindows(portName);
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return GetUsbIdsLinux(portName);
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return GetUsbIdsMacOS(portName);
                }
            }
            catch
            {
                // Silently ignore lookup failures — caller handles the (-1,-1) case
            }

            return (-1, -1);
        }

        /// <summary>
        /// Windows: Search the registry for the COM port and extract VID/PID
        /// from the USB device instance path.
        /// 
        /// Registry structure:
        ///   HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_XXXX&amp;PID_XXXX\{instance}\Device Parameters\PortName
        /// </summary>
        private static (int vid, int pid) GetUsbIdsWindows(string portName)
        {
#if NET472
            return GetUsbIdsFromRegistry(portName);
#else
            // On .NET 8+, use reflection to access Registry if available at runtime
            return GetUsbIdsWindowsReflection(portName);
#endif
        }

#if NET472
        private static (int vid, int pid) GetUsbIdsFromRegistry(string portName)
        {
            using (RegistryKey usbEnum = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\USB"))
            {
                if (usbEnum == null)
                {
                    return (-1, -1);
                }

                foreach (string vidPidKeyName in usbEnum.GetSubKeyNames())
                {
                    using (RegistryKey vidPidKey = usbEnum.OpenSubKey(vidPidKeyName))
                    {
                        if (vidPidKey == null)
                        {
                            continue;
                        }

                        foreach (string instanceId in vidPidKey.GetSubKeyNames())
                        {
                            using (RegistryKey instanceKey = vidPidKey.OpenSubKey(instanceId))
                            using (RegistryKey devParams = instanceKey?.OpenSubKey("Device Parameters"))
                            {
                                if (devParams == null)
                                {
                                    continue;
                                }

                                string port = devParams.GetValue("PortName") as string;

                                if (string.Equals(port, portName, StringComparison.OrdinalIgnoreCase))
                                {
                                    return ParseVidPidFromKeyName(vidPidKeyName);
                                }
                            }
                        }
                    }
                }
            }

            return (-1, -1);
        }
#endif

#if !NET472
        private static (int vid, int pid) GetUsbIdsWindowsReflection(string portName)
        {
            // Microsoft.Win32.Registry is available at runtime on Windows even without
            // a compile-time reference in net8.0. Use reflection to access it.
            Type registryType = Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry");

            if (registryType == null)
            {
                // Fallback: try the mscorlib version
                registryType = Type.GetType("Microsoft.Win32.Registry, mscorlib");
            }

            if (registryType == null)
            {
                return (-1, -1);
            }

            object localMachine = registryType.GetProperty("LocalMachine")?.GetValue(null);
            if (localMachine == null)
            {
                // some implementations might have it as a field instead of a property, so try that as well
                localMachine = registryType.GetField("LocalMachine")?.GetValue(null);
            }

            if (localMachine == null)
            {
                return (-1, -1);
            }

            Type registryKeyType = localMachine.GetType();
            var openSubKeyMethod = registryKeyType.GetMethod("OpenSubKey", new[] { typeof(string) });
            var getSubKeyNamesMethod = registryKeyType.GetMethod("GetSubKeyNames");
            var getValueMethod = registryKeyType.GetMethod("GetValue", new[] { typeof(string) });

            if (openSubKeyMethod == null || getSubKeyNamesMethod == null || getValueMethod == null)
            {
                return (-1, -1);
            }

            object usbEnum = openSubKeyMethod.Invoke(localMachine, new object[] { @"SYSTEM\CurrentControlSet\Enum\USB" });

            if (usbEnum == null)
            {
                return (-1, -1);
            }

            try
            {
                string[] vidPidKeys = (string[])getSubKeyNamesMethod.Invoke(usbEnum, null);

                foreach (string vidPidKeyName in vidPidKeys)
                {
                    object vidPidKey = openSubKeyMethod.Invoke(usbEnum, new object[] { vidPidKeyName });

                    if (vidPidKey == null)
                    {
                        continue;
                    }

                    try
                    {
                        string[] instanceIds = (string[])getSubKeyNamesMethod.Invoke(vidPidKey, null);

                        foreach (string instanceId in instanceIds)
                        {
                            object instanceKey = openSubKeyMethod.Invoke(vidPidKey, new object[] { instanceId });

                            if (instanceKey == null)
                            {
                                continue;
                            }

                            try
                            {
                                object devParams = openSubKeyMethod.Invoke(instanceKey, new object[] { "Device Parameters" });

                                if (devParams == null)
                                {
                                    continue;
                                }

                                try
                                {
                                    string port = getValueMethod.Invoke(devParams, new object[] { "PortName" }) as string;

                                    if (string.Equals(port, portName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return ParseVidPidFromKeyName(vidPidKeyName);
                                    }
                                }
                                finally
                                {
                                    (devParams as IDisposable)?.Dispose();
                                }
                            }
                            finally
                            {
                                (instanceKey as IDisposable)?.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        (vidPidKey as IDisposable)?.Dispose();
                    }
                }
            }
            finally
            {
                (usbEnum as IDisposable)?.Dispose();
            }

            return (-1, -1);
        }
#endif

        /// <summary>
        /// Parse VID and PID from a registry key name like "VID_303A&amp;PID_1001".
        /// </summary>
        private static (int vid, int pid) ParseVidPidFromKeyName(string keyName)
        {
            int vid = -1;
            int pid = -1;

            Match vidMatch = Regex.Match(keyName, @"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            Match pidMatch = Regex.Match(keyName, @"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);

            if (vidMatch.Success)
            {
                vid = Convert.ToInt32(vidMatch.Groups[1].Value, 16);
            }

            if (pidMatch.Success)
            {
                pid = Convert.ToInt32(pidMatch.Groups[1].Value, 16);
            }

            return (vid, pid);
        }

        /// <summary>
        /// Linux: Walk the sysfs device tree for the tty to find idVendor/idProduct.
        /// 
        /// Path: /sys/class/tty/{ttyName}/device/../../idVendor (varies by depth)
        /// </summary>
        private static (int vid, int pid) GetUsbIdsLinux(string portName)
        {
            // Extract tty name from full path (e.g., "/dev/ttyUSB0" → "ttyUSB0")
            string ttyName = Path.GetFileName(portName);
            string sysPath = $"/sys/class/tty/{ttyName}/device";

            if (!Directory.Exists(sysPath))
            {
                return (-1, -1);
            }

            // Walk up the device tree looking for USB idVendor/idProduct files.
            // Keep the original sysfs symlink in the path so the kernel resolves
            // parent traversal against the real device tree under /sys/devices.
            string current = sysPath;

            for (int i = 0; i < 6; i++)
            {
                string vendorFile = Path.Combine(current, "idVendor");
                string productFile = Path.Combine(current, "idProduct");

                if (File.Exists(vendorFile) && File.Exists(productFile))
                {
                    string vidStr = File.ReadAllText(vendorFile).Trim();
                    string pidStr = File.ReadAllText(productFile).Trim();

                    int vid = Convert.ToInt32(vidStr, 16);
                    int pid = Convert.ToInt32(pidStr, 16);

                    return (vid, pid);
                }

                // Go up one directory level while preserving the symlinked path.
                current = Path.Combine(current, "..");

                if (string.IsNullOrEmpty(current))
                {
                    break;
                }
            }

            return (-1, -1);
        }

        /// <summary>
        /// macOS: Determine USB VID/PID for a specific serial port via ioreg.
        /// Falls back to (-1, -1) if not determinable.
        /// </summary>
        private static (int vid, int pid) GetUsbIdsMacOS(string portName)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/sbin/ioreg",
                    Arguments = "-r -c IOSerialBSDClient -l",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    if (process == null)
                    {
                        return (-1, -1);
                    }

                    string output = string.Empty;
                    if (!process.WaitForExit(3000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // Ignore kill failures; fall back to unknown VID/PID.
                        }
                        return (-1, -1);
                    }

                    output = process.StandardOutput.ReadToEnd();

                    return ParseUsbIdsFromMacOsIoreg(output, portName);
                }
            }
            catch
            {
                // ioreg failed — return unknown
            }

            return (-1, -1);
        }

        internal static (int vid, int pid) ParseUsbIdsFromMacOsIoreg(string ioregOutput, string portName)
        {
            if (string.IsNullOrWhiteSpace(ioregOutput) || string.IsNullOrWhiteSpace(portName))
            {
                return (-1, -1);
            }

            string normalizedPort = NormalizePortPath(portName);

            var portRegex = new Regex(
                @"""(?:IOCalloutDevice|IODialinDevice)""\s*=\s*""(?<port>[^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var vidRegex = new Regex(
                @"""idVendor""\s*=\s*(?<value>0x[0-9A-Fa-f]+|\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var pidRegex = new Regex(
                @"""idProduct""\s*=\s*(?<value>0x[0-9A-Fa-f]+|\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            bool inBlock = false;
            bool blockHasMatchingPort = false;
            int vid = -1;
            int pid = -1;

            string[] lines = ioregOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (trimmed == "{")
                {
                    inBlock = true;
                    blockHasMatchingPort = false;
                    vid = -1;
                    pid = -1;
                    continue;
                }

                if (!inBlock)
                {
                    continue;
                }

                if (trimmed == "}")
                {
                    if (blockHasMatchingPort && vid >= 0 && pid >= 0)
                    {
                        return (vid, pid);
                    }

                    inBlock = false;
                    continue;
                }

                Match portMatch = portRegex.Match(trimmed);
                if (portMatch.Success)
                {
                    string candidatePort = NormalizePortPath(portMatch.Groups["port"].Value);
                    if (string.Equals(candidatePort, normalizedPort, StringComparison.OrdinalIgnoreCase))
                    {
                        blockHasMatchingPort = true;
                    }

                    continue;
                }

                Match vidMatch = vidRegex.Match(trimmed);
                if (vidMatch.Success)
                {
                    if (TryParseIoregNumber(vidMatch.Groups["value"].Value, out int parsedVid))
                    {
                        vid = parsedVid;
                    }

                    continue;
                }

                Match pidMatch = pidRegex.Match(trimmed);
                if (pidMatch.Success && TryParseIoregNumber(pidMatch.Groups["value"].Value, out int parsedPid))
                {
                    pid = parsedPid;
                }
            }

            return (-1, -1);
        }

        private static string NormalizePortPath(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return string.Empty;
            }

            if (portName.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
            {
                return portName;
            }

            return $"/dev/{portName}";
        }

        private static bool TryParseIoregNumber(string value, out int number)
        {
            number = -1;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number);
            }

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return true;
            }

            return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number);
        }
    }
}
