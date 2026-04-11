// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Utility class for Raspberry Pi Pico UF2 drive detection, firmware conversion, and deployment.
    /// </summary>
    public static class PicoUf2Utility
    {
        // UF2 format magic numbers
        private const uint UF2_MAGIC_START0 = 0x0A324655; // "UF2\n"
        private const uint UF2_MAGIC_START1 = 0x9E5D5157;
        private const uint UF2_MAGIC_END = 0x0AB16F30;
        private const uint UF2_FLAG_FAMILY_ID = 0x00002000;

        // UF2 block constants
        private const int UF2_BLOCK_SIZE = 512;
        private const int UF2_DATA_SIZE = 256;

        // UF2 family IDs

        /// <summary>UF2 family ID for RP2040 chip.</summary>
        public const uint FAMILY_ID_RP2040 = 0xE48BFF56;

        /// <summary>UF2 family ID for RP2350 ARM chip.</summary>
        public const uint FAMILY_ID_RP2350_ARM = 0xE48BFF59;

        // Known volume labels for Pico devices in BOOTSEL mode
        private static readonly string[] KnownVolumeLabels = ["RPI-RP2", "RP2350"];

        /// <summary>
        /// Find a mounted UF2 drive for a Pico device in BOOTSEL mode.
        /// </summary>
        /// <returns>The drive path if found, or <c>null</c>.</returns>
        public static string FindUf2Drive()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return FindUf2DriveWindows();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return FindUf2DriveLinux();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return FindUf2DriveMacOS();
            }

            return null;
        }

        /// <summary>
        /// Find all mounted UF2 drives for Pico devices in BOOTSEL mode.
        /// </summary>
        /// <returns>List of drive paths.</returns>
        public static List<string> FindAllUf2Drives()
        {
            var drives = new List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (IsUf2Drive(drive))
                    {
                        drives.Add(drive.RootDirectory.FullName);
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                drives.AddRange(FindUf2DrivesUnix("/media", "/run/media", "/mnt"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                drives.AddRange(FindUf2DrivesUnix("/Volumes"));
            }

            return drives;
        }

        /// <summary>
        /// Detect device info by reading INFO_UF2.TXT from the mounted drive.
        /// </summary>
        /// <param name="drivePath">Path to the UF2 drive.</param>
        /// <returns>Device info, or <c>null</c> if detection failed.</returns>
        public static PicoDeviceInfo DetectDevice(string drivePath)
        {
            string infoFilePath = Path.Combine(drivePath, "INFO_UF2.TXT");

            if (!File.Exists(infoFilePath))
            {
                return null;
            }

            string[] lines = File.ReadAllLines(infoFilePath);

            string chipType = "RP2040";
            string boardId = "";
            string bootloaderVersion = "";
            string driveLabel = "";

            foreach (string line in lines)
            {
                if (line.StartsWith("Model:", StringComparison.OrdinalIgnoreCase))
                {
                    string model = line.Substring("Model:".Length).Trim();

                    if (model.Contains("RP2350", StringComparison.OrdinalIgnoreCase))
                    {
                        chipType = "RP2350";
                    }
                    else
                    {
                        chipType = "RP2040";
                    }
                }
                else if (line.StartsWith("Board-ID:", StringComparison.OrdinalIgnoreCase))
                {
                    boardId = line.Substring("Board-ID:".Length).Trim();
                }
                else if (line.StartsWith("UF2 Bootloader", StringComparison.OrdinalIgnoreCase))
                {
                    bootloaderVersion = line.Trim();
                }
            }

            // try to get volume label
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var driveInfo = new DriveInfo(drivePath);
                    driveLabel = driveInfo.VolumeLabel;
                }
                else
                {
                    driveLabel = Path.GetFileName(drivePath.TrimEnd(Path.DirectorySeparatorChar));
                }
            }
            catch (Exception)
            {
                // ignore errors getting volume label
            }

            return new PicoDeviceInfo(chipType, boardId, bootloaderVersion, drivePath, driveLabel);
        }

        /// <summary>
        /// Check whether the given data is in UF2 format by verifying magic bytes.
        /// </summary>
        /// <param name="data">Raw file data.</param>
        /// <returns><c>true</c> if the data starts with valid UF2 magic bytes.</returns>
        public static bool IsUf2Data(byte[] data)
        {
            if (data == null || data.Length < UF2_BLOCK_SIZE)
            {
                return false;
            }

            uint magic0 = BitConverter.ToUInt32(data, 0);
            uint magic1 = BitConverter.ToUInt32(data, 4);

            return magic0 == UF2_MAGIC_START0 && magic1 == UF2_MAGIC_START1;
        }

        /// <summary>
        /// Extract raw binary data from UF2-formatted data.
        /// Reconstructs the binary by reading each block's target address and data payload,
        /// placing data at the correct offsets relative to the lowest address.
        /// </summary>
        /// <param name="uf2Data">Raw UF2 file data.</param>
        /// <returns>Extracted binary data, or <c>null</c> if the UF2 data is invalid.</returns>
        public static byte[] ExtractBinFromUf2(byte[] uf2Data)
        {
            if (uf2Data == null || uf2Data.Length < UF2_BLOCK_SIZE || uf2Data.Length % UF2_BLOCK_SIZE != 0)
            {
                return null;
            }

            int blockCount = uf2Data.Length / UF2_BLOCK_SIZE;

            // first pass: find address range
            uint minAddress = uint.MaxValue;
            uint maxAddress = 0;

            for (int i = 0; i < blockCount; i++)
            {
                int offset = i * UF2_BLOCK_SIZE;
                uint targetAddr = BitConverter.ToUInt32(uf2Data, offset + 12);
                uint dataLen = BitConverter.ToUInt32(uf2Data, offset + 16);

                if (targetAddr < minAddress)
                {
                    minAddress = targetAddr;
                }

                uint end = targetAddr + dataLen;

                if (end > maxAddress)
                {
                    maxAddress = end;
                }
            }

            if (minAddress >= maxAddress)
            {
                return null;
            }

            // second pass: copy data into output buffer
            byte[] result = new byte[maxAddress - minAddress];

            for (int i = 0; i < blockCount; i++)
            {
                int offset = i * UF2_BLOCK_SIZE;
                uint targetAddr = BitConverter.ToUInt32(uf2Data, offset + 12);
                uint dataLen = BitConverter.ToUInt32(uf2Data, offset + 16);

                if (dataLen > UF2_DATA_SIZE)
                {
                    dataLen = UF2_DATA_SIZE;
                }

                Array.Copy(uf2Data, offset + 32, result, targetAddr - minAddress, dataLen);
            }

            return result;
        }

        /// <summary>
        /// Validate the integrity of UF2 data. Checks magic bytes, block numbering,
        /// total block count consistency, family ID, and dataLen alignment.
        /// </summary>
        /// <param name="uf2Data">Raw UF2 file data.</param>
        /// <param name="verbosity">Verbosity level for output.</param>
        /// <returns><c>true</c> if the UF2 data is valid with no errors (warnings are allowed).</returns>
        public static bool ValidateUf2Data(byte[] uf2Data, VerbosityLevel verbosity)
        {
            if (uf2Data == null || uf2Data.Length < UF2_BLOCK_SIZE)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("UF2 validation: file is too small or empty.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return false;
            }

            if (uf2Data.Length % UF2_BLOCK_SIZE != 0)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"UF2 validation: file size ({uf2Data.Length}) is not a multiple of {UF2_BLOCK_SIZE}.");
                OutputWriter.ForegroundColor = ConsoleColor.White;

                return false;
            }

            int numBlocks = uf2Data.Length / UF2_BLOCK_SIZE;
            int errors = 0;
            int warnings = 0;
            uint? familyId = null;

            for (int i = 0; i < numBlocks; i++)
            {
                int off = i * UF2_BLOCK_SIZE;

                uint magic0 = BitConverter.ToUInt32(uf2Data, off);
                uint magic1 = BitConverter.ToUInt32(uf2Data, off + 4);
                uint magicEnd = BitConverter.ToUInt32(uf2Data, off + UF2_BLOCK_SIZE - 4);
                uint blockNo = BitConverter.ToUInt32(uf2Data, off + 20);
                uint totalBlocks = BitConverter.ToUInt32(uf2Data, off + 24);
                uint dataLen = BitConverter.ToUInt32(uf2Data, off + 16);
                uint flags = BitConverter.ToUInt32(uf2Data, off + 8);
                uint famId = BitConverter.ToUInt32(uf2Data, off + 28);

                // check magic bytes
                if (magic0 != UF2_MAGIC_START0 || magic1 != UF2_MAGIC_START1 || magicEnd != UF2_MAGIC_END)
                {
                    errors++;

                    if (verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"UF2 block {i}: invalid magic bytes.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }

                // check block numbering
                if (blockNo != (uint)i)
                {
                    errors++;

                    if (verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"UF2 block {i}: blockNo={blockNo}, expected {i}.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }

                // check total blocks consistency
                if (totalBlocks != (uint)numBlocks)
                {
                    errors++;

                    if (verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"UF2 block {i}: numBlocks={totalBlocks}, expected {numBlocks}.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }

                // check dataLen
                if (dataLen > 476)
                {
                    errors++;

                    if (verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine($"UF2 block {i}: dataLen={dataLen} exceeds maximum (476).");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }
                else if (dataLen != UF2_DATA_SIZE)
                {
                    warnings++;

                    if (verbosity >= VerbosityLevel.Normal && warnings <= 3)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine($"WARNING: UF2 block {i}: dataLen={dataLen}, not aligned to {UF2_DATA_SIZE}. Some bootloaders may not process this correctly.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }

                // track family ID consistency
                if ((flags & UF2_FLAG_FAMILY_ID) != 0)
                {
                    if (familyId == null)
                    {
                        familyId = famId;
                    }
                    else if (famId != familyId.Value)
                    {
                        warnings++;

                        if (verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                            OutputWriter.WriteLine($"WARNING: UF2 block {i}: family ID 0x{famId:X8} differs from block 0 (0x{familyId.Value:X8}).");
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                        }
                    }
                }
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                string famName = familyId switch
                {
                    FAMILY_ID_RP2040 => "RP2040",
                    FAMILY_ID_RP2350_ARM => "RP2350-ARM",
                    _ => familyId.HasValue ? $"0x{familyId.Value:X8}" : "not set"
                };

                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine($"UF2 validation: {numBlocks} blocks, family={famName}, errors={errors}, warnings={warnings}.");
            }

            return errors == 0;
        }

        /// <summary>
        /// Convert a raw binary firmware file to UF2 format.
        /// </summary>
        /// <param name="binaryData">Raw binary data.</param>
        /// <param name="baseAddress">Target flash address (typically 0x10000000 for RP2040/RP2350 XIP flash).</param>
        /// <param name="familyId">UF2 family ID.</param>
        /// <returns>UF2 formatted data.</returns>
        public static byte[] ConvertBinToUf2(byte[] binaryData, uint baseAddress, uint familyId)
        {
            if (binaryData == null || binaryData.Length == 0)
            {
                return [];
            }

            int numBlocks = (binaryData.Length + UF2_DATA_SIZE - 1) / UF2_DATA_SIZE;
            byte[] uf2Data = new byte[numBlocks * UF2_BLOCK_SIZE];

            for (int blockIndex = 0; blockIndex < numBlocks; blockIndex++)
            {
                int offset = blockIndex * UF2_BLOCK_SIZE;
                int dataOffset = blockIndex * UF2_DATA_SIZE;
                int dataLength = Math.Min(UF2_DATA_SIZE, binaryData.Length - dataOffset);

                // write UF2 block header
                WriteUInt32(uf2Data, offset + 0, UF2_MAGIC_START0);
                WriteUInt32(uf2Data, offset + 4, UF2_MAGIC_START1);
                WriteUInt32(uf2Data, offset + 8, UF2_FLAG_FAMILY_ID);
                WriteUInt32(uf2Data, offset + 12, baseAddress + (uint)dataOffset);
                WriteUInt32(uf2Data, offset + 16, (uint)dataLength);
                WriteUInt32(uf2Data, offset + 20, (uint)blockIndex);
                WriteUInt32(uf2Data, offset + 24, (uint)numBlocks);
                WriteUInt32(uf2Data, offset + 28, familyId);

                // copy payload data (rest stays zero-padded)
                Array.Copy(binaryData, dataOffset, uf2Data, offset + 32, dataLength);

                // write end magic
                WriteUInt32(uf2Data, offset + UF2_BLOCK_SIZE - 4, UF2_MAGIC_END);
            }

            return uf2Data;
        }

        /// <summary>
        /// Pad existing UF2 data with zero-filled blocks so that the entire flash is covered.
        /// Any 256-byte region not already present in the UF2 gets a zero-filled block,
        /// causing the bootloader to erase and write zeros to those sectors.
        /// </summary>
        /// <param name="uf2Data">Existing UF2 data (firmware or application).</param>
        /// <param name="flashBase">Flash base address (typically 0x10000000).</param>
        /// <param name="flashSize">Total flash size in bytes.</param>
        /// <param name="familyId">UF2 family ID.</param>
        /// <returns>New UF2 data covering the entire flash range.</returns>
        public static byte[] PadUf2ToFullFlash(byte[] uf2Data, uint flashBase, uint flashSize, uint familyId)
        {
            int totalBlocks = (int)(flashSize / UF2_DATA_SIZE);

            // track which 256-byte regions are already in the UF2
            var coveredAddresses = new System.Collections.Generic.HashSet<uint>();
            var existingBlocks = new System.Collections.Generic.Dictionary<uint, byte[]>();

            if (uf2Data != null && uf2Data.Length >= UF2_BLOCK_SIZE)
            {
                int inputBlocks = uf2Data.Length / UF2_BLOCK_SIZE;

                for (int i = 0; i < inputBlocks; i++)
                {
                    int off = i * UF2_BLOCK_SIZE;
                    uint addr = BitConverter.ToUInt32(uf2Data, off + 12);

                    coveredAddresses.Add(addr);

                    // extract the full 512-byte block for later re-use
                    byte[] block = new byte[UF2_BLOCK_SIZE];
                    Array.Copy(uf2Data, off, block, 0, UF2_BLOCK_SIZE);
                    existingBlocks[addr] = block;
                }
            }

            // build the full UF2 with all addresses covered
            byte[] result = new byte[totalBlocks * UF2_BLOCK_SIZE];
            int blockIndex = 0;

            for (uint addr = flashBase; addr < flashBase + flashSize; addr += UF2_DATA_SIZE)
            {
                int offset = blockIndex * UF2_BLOCK_SIZE;

                if (existingBlocks.TryGetValue(addr, out byte[] existingBlock))
                {
                    // copy existing block and update block numbering
                    Array.Copy(existingBlock, 0, result, offset, UF2_BLOCK_SIZE);
                }
                else
                {
                    // create zero-filled block (data area stays zero from array init)
                    WriteUInt32(result, offset + 0, UF2_MAGIC_START0);
                    WriteUInt32(result, offset + 4, UF2_MAGIC_START1);
                    WriteUInt32(result, offset + 8, UF2_FLAG_FAMILY_ID);
                    WriteUInt32(result, offset + 12, addr);
                    WriteUInt32(result, offset + 16, UF2_DATA_SIZE);
                    WriteUInt32(result, offset + 28, familyId);
                    WriteUInt32(result, offset + UF2_BLOCK_SIZE - 4, UF2_MAGIC_END);
                }

                // fix block numbering for all blocks
                WriteUInt32(result, offset + 20, (uint)blockIndex);
                WriteUInt32(result, offset + 24, (uint)totalBlocks);

                blockIndex++;
            }

            return result;
        }

        /// <summary>
        /// Deploy a UF2 file to a Pico device by copying it to the UF2 drive.
        /// </summary>
        /// <param name="uf2Data">UF2 formatted data.</param>
        /// <param name="drivePath">Path to the UF2 drive.</param>
        /// <param name="fileName">Target file name on the drive. If <c>null</c>, defaults to "firmware.uf2".</param>
        /// <returns>Exit code indicating success or failure.</returns>
        public static ExitCodes DeployUf2File(byte[] uf2Data, string drivePath, string fileName = null)
        {
            // ensure .uf2 extension for the target file
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "firmware.uf2";
            }
            else
            {
                fileName = Path.ChangeExtension(fileName, ".uf2");
            }

            string targetPath = Path.Combine(drivePath, fileName);

            try
            {
                // use WriteThrough to bypass OS write cache and flush directly to USB device;
                // this ensures the Pico bootloader sees the complete file and reboots automatically
                using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(uf2Data, 0, uf2Data.Length);
                    fs.Flush(true);
                }
            }
            catch (Exception)
            {
                return ExitCodes.E3002;
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Wait for a UF2 drive to appear, with a timeout.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <param name="verbosity">Verbosity level.</param>
        /// <returns>The drive path if found, or <c>null</c> if timed out.</returns>
        public static string WaitForDrive(int timeoutMs, VerbosityLevel verbosity)
        {
            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("Waiting for Pico device in BOOTSEL mode...");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            int elapsed = 0;
            const int pollInterval = 500;

            while (elapsed < timeoutMs)
            {
                string drivePath = FindUf2Drive();

                if (drivePath != null)
                {
                    return drivePath;
                }

                Thread.Sleep(pollInterval);
                elapsed += pollInterval;
            }

            return null;
        }

        /// <summary>
        /// Wait for a UF2 drive to disappear after firmware deployment.
        /// The Pico bootloader automatically reboots after processing a UF2 file,
        /// which causes the drive to disappear.
        /// </summary>
        /// <param name="drivePath">Path to the UF2 drive.</param>
        /// <param name="timeoutMs">Timeout in milliseconds to wait for the drive to disappear.</param>
        /// <param name="verbosity">Verbosity level.</param>
        /// <returns><c>true</c> if the drive disappeared (device rebooted), <c>false</c> if it's still present.</returns>
        public static bool WaitForDriveRemoval(string drivePath, int timeoutMs, VerbosityLevel verbosity)
        {
            int elapsed = 0;
            const int pollInterval = 500;

            while (elapsed < timeoutMs)
            {
                Thread.Sleep(pollInterval);
                elapsed += pollInterval;

                if (!Directory.Exists(drivePath))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Force-eject a UF2 drive. On Windows uses Win32 DeviceIoControl,
        /// on Linux/macOS uses the umount command.
        /// </summary>
        /// <param name="drivePath">Path to the UF2 drive to eject.</param>
        /// <returns><c>true</c> if the ejection succeeded.</returns>
        public static bool EjectDrive(string drivePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return EjectDriveWindows(drivePath);
            }
            else
            {
                return EjectDriveUnix(drivePath);
            }
        }

        #region private helpers

        private static bool EjectDriveWindows(string drivePath)
        {
            // extract drive letter (e.g. "E:\" → "E:")
            string driveLetter = drivePath.TrimEnd('\\');

            if (driveLetter.Length > 2)
            {
                driveLetter = driveLetter.Substring(0, 2);
            }

            string volumePath = @"\\.\" + driveLetter;

            IntPtr handle = CreateFile(
                volumePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == INVALID_HANDLE_VALUE)
            {
                return false;
            }

            try
            {
                uint bytesReturned;

                // lock the volume
                DeviceIoControl(handle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                // dismount the volume
                DeviceIoControl(handle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                // eject the media
                bool ejected = DeviceIoControl(handle, IOCTL_STORAGE_EJECT_MEDIA, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                return ejected;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool EjectDriveUnix(string drivePath)
        {
            try
            {
                string command = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "diskutil"
                    : "umount";

                string arguments = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? $"eject \"{drivePath}\""
                    : $"\"{drivePath}\"";

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                process?.WaitForExit(5000);

                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // Win32 constants and P/Invoke for drive ejection
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private const uint FSCTL_LOCK_VOLUME = 0x00090018;
        private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
        private const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize,
            IntPtr lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static string FindUf2DriveWindows()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (IsUf2Drive(drive))
                {
                    return drive.RootDirectory.FullName;
                }
            }

            return null;
        }

        private static bool IsUf2Drive(DriveInfo drive)
        {
            try
            {
                if (drive.DriveType == DriveType.Removable
                    && drive.IsReady
                    && KnownVolumeLabels.Contains(drive.VolumeLabel, StringComparer.OrdinalIgnoreCase))
                {
                    // verify INFO_UF2.TXT exists
                    return File.Exists(Path.Combine(drive.RootDirectory.FullName, "INFO_UF2.TXT"));
                }
            }
            catch (Exception)
            {
                // ignore errors accessing drive info
            }

            return false;
        }

        private static string FindUf2DriveLinux()
        {
            foreach (string basePath in new[] { "/media", "/run/media", "/mnt" })
            {
                string found = SearchDirectoryForUf2(basePath);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string FindUf2DriveMacOS()
        {
            return SearchDirectoryForUf2("/Volumes");
        }

        private static string SearchDirectoryForUf2(string basePath)
        {
            if (!Directory.Exists(basePath))
            {
                return null;
            }

            try
            {
                // search up to 3 levels deep for known volume labels with INFO_UF2.TXT
                return SearchDirectoryForUf2Recursive(basePath, 0, 3);
            }
            catch (Exception)
            {
                // ignore permission errors
            }

            return null;
        }

        private static string SearchDirectoryForUf2Recursive(string path, int depth, int maxDepth)
        {
            if (depth >= maxDepth)
            {
                return null;
            }

            try
            {
                foreach (string dir in Directory.EnumerateDirectories(path))
                {
                    string dirName = Path.GetFileName(dir);

                    if (KnownVolumeLabels.Contains(dirName, StringComparer.OrdinalIgnoreCase)
                        && File.Exists(Path.Combine(dir, "INFO_UF2.TXT")))
                    {
                        return dir;
                    }

                    string found = SearchDirectoryForUf2Recursive(dir, depth + 1, maxDepth);

                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            catch (Exception)
            {
                // ignore permission errors on individual directories
            }

            return null;
        }

        private static List<string> FindUf2DrivesUnix(params string[] basePaths)
        {
            var result = new List<string>();

            foreach (string basePath in basePaths)
            {
                if (!Directory.Exists(basePath))
                {
                    continue;
                }

                try
                {
                    FindUf2DrivesRecursive(basePath, 0, 3, result);
                }
                catch (Exception)
                {
                    // ignore permission errors
                }
            }

            return result;
        }

        private static void FindUf2DrivesRecursive(string path, int depth, int maxDepth, List<string> result)
        {
            if (depth >= maxDepth)
            {
                return;
            }

            try
            {
                foreach (string dir in Directory.EnumerateDirectories(path))
                {
                    string dirName = Path.GetFileName(dir);

                    if (KnownVolumeLabels.Contains(dirName, StringComparer.OrdinalIgnoreCase)
                        && File.Exists(Path.Combine(dir, "INFO_UF2.TXT")))
                    {
                        result.Add(dir);
                    }
                    else
                    {
                        FindUf2DrivesRecursive(dir, depth + 1, maxDepth, result);
                    }
                }
            }
            catch (Exception)
            {
                // ignore permission errors on individual directories
            }
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        #endregion
    }
}
