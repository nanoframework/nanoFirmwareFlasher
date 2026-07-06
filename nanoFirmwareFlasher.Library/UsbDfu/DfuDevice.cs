//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.UsbDfu
{
    /// <summary>
    /// Low-level USB DFU/DfuSe device that communicates via USB control transfers.
    /// Implements the USB DFU 1.1 protocol with ST DfuSe extensions (AN3156).
    /// Cross-platform: uses WinUSB on Windows, libusb-1.0 on Linux/macOS.
    /// </summary>
    internal class DfuDevice : IDisposable
    {
        private IUsbDevice _usb;
        private bool _disposed;

        /// <summary>
        /// Gets the device path of the opened device.
        /// </summary>
        internal string DevicePath { get; private set; }

        /// <summary>
        /// Gets the serial number extracted from the device path.
        /// </summary>
        internal string SerialNumber { get; private set; }

        /// <summary>
        /// Gets the transfer size per block (default 2048 for STM32).
        /// </summary>
        internal int TransferSize { get; set; } = DfuConst.DefaultTransferSize;

        /// <summary>
        /// Opens a DFU device by its device path.
        /// </summary>
        /// <param name="devicePath">The platform-specific device path.</param>
        internal void Open(string devicePath)
        {
            DevicePath = devicePath;

            _usb = UsbDeviceFactory.Create();
            _usb.Open(devicePath);

            // Extract serial from path on Windows, use raw path elsewhere
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
            {
                SerialNumber = ParseSerialFromPath(devicePath);
            }
            else
            {
                SerialNumber = devicePath;
            }
        }

        /// <summary>
        /// Lists all connected STM32 DFU devices on the current platform.
        /// </summary>
        /// <returns>List of (serialNumber, devicePath) tuples.</returns>
        internal static List<(string serial, string devicePath)> Enumerate()
        {
            return UsbDeviceFactory.Enumerate();
        }

        #region DFU protocol commands

        /// <summary>
        /// Sends DFU_GETSTATUS and returns the parsed status result.
        /// </summary>
        internal DfuStatusResult GetStatus()
        {
            byte[] buffer = new byte[6];

            ControlTransferIn(DfuConst.DfuGetStatus, 0, buffer, buffer.Length);

            return new DfuStatusResult
            {
                Status = (DfuStatus)buffer[0],
                PollTimeoutLow = buffer[1],
                PollTimeoutMid = buffer[2],
                PollTimeoutHigh = buffer[3],
                State = (DfuState)buffer[4],
                StringIndex = buffer[5],
            };
        }

        /// <summary>
        /// Sends DFU_CLRSTATUS to clear any error state.
        /// </summary>
        internal void ClearStatus()
        {
            ControlTransferOut(DfuConst.DfuClrStatus, 0, null, 0);
        }

        /// <summary>
        /// Sends DFU_GETSTATE and returns the current state byte.
        /// </summary>
        internal DfuState GetState()
        {
            byte[] buffer = new byte[1];

            ControlTransferIn(DfuConst.DfuGetState, 0, buffer, buffer.Length);

            return (DfuState)buffer[0];
        }

        /// <summary>
        /// Sends DFU_ABORT to return the device to dfuIDLE state.
        /// </summary>
        internal void Abort()
        {
            ControlTransferOut(DfuConst.DfuAbort, 0, null, 0);
        }

        /// <summary>
        /// Sends DFU_DNLOAD with the specified block number and data.
        /// </summary>
        /// <param name="blockNumber">Block number (0 for DfuSe commands, >=2 for data).</param>
        /// <param name="data">Data to download. Null or empty for zero-length transfer (leave DFU mode).</param>
        internal void Download(ushort blockNumber, byte[] data)
        {
            ControlTransferOut(DfuConst.DfuDnload, blockNumber, data, data != null ? data.Length : 0);
        }

        #endregion

        #region DfuSe commands (ST extensions)

        /// <summary>
        /// Sets the DfuSe address pointer. Subsequent data downloads will target this address.
        /// </summary>
        /// <param name="address">Target flash address.</param>
        internal void SetAddress(uint address)
        {
            byte[] cmd = new byte[5];
            cmd[0] = DfuConst.DfuSeSetAddress;
            cmd[1] = (byte)(address & 0xFF);
            cmd[2] = (byte)((address >> 8) & 0xFF);
            cmd[3] = (byte)((address >> 16) & 0xFF);
            cmd[4] = (byte)((address >> 24) & 0xFF);

            Download(0, cmd);
            WaitForIdle("SetAddress");
        }

        /// <summary>
        /// Erases a single flash page containing the specified address.
        /// </summary>
        /// <param name="pageAddress">Address within the page to erase.</param>
        internal void ErasePage(uint pageAddress)
        {
            byte[] cmd = new byte[5];
            cmd[0] = DfuConst.DfuSeErase;
            cmd[1] = (byte)(pageAddress & 0xFF);
            cmd[2] = (byte)((pageAddress >> 8) & 0xFF);
            cmd[3] = (byte)((pageAddress >> 16) & 0xFF);
            cmd[4] = (byte)((pageAddress >> 24) & 0xFF);

            Download(0, cmd);
            WaitForEraseComplete("ErasePage");
        }

        /// <summary>
        /// Performs a full-chip erase, one flash sector at a time.
        /// </summary>
        /// <remarks>
        /// The single DfuSe mass-erase command (0x41 with no address) makes the STM32
        /// bootloader run the whole multi-second erase synchronously inside the
        /// DFU_GETSTATUS handler, during which it stops servicing the USB control pipe.
        /// The USB stack then aborts the transfer (WinUSB ERROR_SEM_TIMEOUT) and, once
        /// aborted, the control pipe cannot be reused. Erasing one sector at a time keeps
        /// every operation well within the control-transfer timeout, which is how
        /// STM32CubeProgrammer and dfu-util perform a full erase over DFU.
        /// </remarks>
        internal void MassErase()
        {
            List<(uint address, uint size)> sectors = GetInternalFlashSectors();

            if (sectors.Count == 0)
            {
                throw new DfuOperationFailedException(
                    "Could not determine the STM32 internal flash sector layout from the DFU " +
                    "descriptor, so the device cannot be erased.");
            }

            foreach ((uint address, uint size) sector in sectors)
            {
                ErasePage(sector.address);
            }
        }

        /// <summary>
        /// Downloads (writes) a firmware data block at the specified block number.
        /// The address must have been set previously via <see cref="SetAddress"/>.
        /// </summary>
        /// <param name="blockNumber">Block number (starting from <see cref="DfuConst.DfuSeDataBlockOffset"/>).</param>
        /// <param name="data">Firmware data (max <see cref="TransferSize"/> bytes).</param>
        internal void DownloadBlock(ushort blockNumber, byte[] data)
        {
            Download(blockNumber, data);
            WaitForIdle("DownloadBlock");
        }

        /// <summary>
        /// Leaves DFU mode and starts execution at the address previously set via <see cref="SetAddress"/>.
        /// </summary>
        internal void LeaveDfuMode()
        {
            // Zero-length DFU_DNLOAD with non-zero block number signals "leave DFU mode"
            Download(0, new byte[0]);

            // Get status — device should transition through dfuMANIFEST
            try
            {
                DfuStatusResult status = GetStatus();

                // Wait briefly for manifestation
                if (status.PollTimeout > 0)
                {
                    Thread.Sleep(Math.Min(status.PollTimeout, 5000));
                }
            }
            catch
            {
                // Device may reset and disconnect before we can read status — this is normal
            }
        }

        #endregion

        #region High-level operations

        /// <summary>
        /// Ensures the device is in dfuIDLE state, clearing errors if necessary.
        /// </summary>
        internal void EnsureIdle()
        {
            DfuStatusResult status = GetStatus();

            if (status.State == DfuState.DfuError)
            {
                ClearStatus();
                status = GetStatus();
            }

            if (status.State != DfuState.DfuIdle)
            {
                Abort();
                status = GetStatus();
            }

            if (status.State != DfuState.DfuIdle)
            {
                throw new DfuOperationFailedException(
                    $"Device not in dfuIDLE state. Current state: {status.State}, status: {status.Status}");
            }
        }

        /// <summary>
        /// Writes a contiguous block of firmware data to flash at the specified address.
        /// Handles chunking into transfer-size blocks and polling for completion.
        /// </summary>
        /// <param name="address">Start flash address.</param>
        /// <param name="data">Data to write (any length).</param>
        /// <param name="progressCallback">Optional callback (bytesWritten, totalBytes).</param>
        internal void WriteFirmware(uint address, byte[] data, Action<int, int> progressCallback = null)
        {
            // Set the write address
            SetAddress(address);

            int offset = 0;
            ushort blockNum = DfuConst.DfuSeDataBlockOffset;

            while (offset < data.Length)
            {
                int chunkSize = Math.Min(TransferSize, data.Length - offset);
                byte[] chunk = new byte[chunkSize];
                Buffer.BlockCopy(data, offset, chunk, 0, chunkSize);

                DownloadBlock(blockNum, chunk);

                offset += chunkSize;
                blockNum++;
                progressCallback?.Invoke(offset, data.Length);
            }
        }

        /// <summary>
        /// Reads the USB device descriptor to get VID, PID, and string descriptor indices.
        /// </summary>
        internal (ushort vendorId, ushort productId, byte iSerialNumber, byte iProduct) GetDeviceDescriptor()
        {
            byte[] buffer = new byte[18]; // USB device descriptor is 18 bytes

            int transferred = _usb.GetDeviceDescriptor(buffer);

            if (transferred < 18)
            {
                throw new DfuOperationFailedException(
                    $"Incomplete USB device descriptor (got {transferred} bytes, expected 18).");
            }

            ushort vid = (ushort)(buffer[8] | (buffer[9] << 8));
            ushort pid = (ushort)(buffer[10] | (buffer[11] << 8));
            byte iSerial = buffer[16];
            byte iProduct = buffer[15];

            return (vid, pid, iSerial, iProduct);
        }

        /// <summary>
        /// Reads a USB string descriptor.
        /// </summary>
        /// <param name="index">String descriptor index.</param>
        /// <returns>The string value, or empty if not available.</returns>
        internal string GetStringDescriptor(byte index)
        {
            if (index == 0)
            {
                return string.Empty;
            }

            byte[] buffer = new byte[256];

            int transferred = _usb.GetStringDescriptor(index, 0x0409, buffer);

            if (transferred < 4)
            {
                return string.Empty;
            }

            // String descriptor: byte bLength, byte bDescriptorType, wchar_t bString[]
            int stringLength = buffer[0] - 2; // subtract header bytes

            if (stringLength <= 0)
            {
                return string.Empty;
            }

            // String is UTF-16LE starting at offset 2
            return System.Text.Encoding.Unicode.GetString(buffer, 2, stringLength);
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of unmanaged resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _usb != null)
                {
                    _usb.Dispose();
                    _usb = null;
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~DfuDevice()
        {
            Dispose(false);
        }

        #endregion

        #region Private helpers

        private void ControlTransferOut(byte request, ushort value, byte[] data, int length)
        {
            _usb.ControlTransferOut(DfuConst.DfuRequestOut, request, value, 0, data, length);
        }

        private void ControlTransferIn(byte request, ushort value, byte[] buffer, int length)
        {
            _usb.ControlTransferIn(DfuConst.DfuRequestIn, request, value, 0, buffer, length);
        }

        /// <summary>
        /// Reads and parses the STM32 internal flash sector map from the DfuSe memory
        /// layout string exposed in the alternate-setting interface descriptor
        /// (for example <c>@Internal Flash /0x08000000/04*016Kg,01*064Kg,03*128Kg</c>).
        /// </summary>
        /// <returns>The ordered list of (address, size) flash sectors, or an empty list
        /// if the layout could not be determined.</returns>
        internal List<(uint address, uint size)> GetInternalFlashSectors()
        {
            var sectors = new List<(uint address, uint size)>();

            // Read the configuration descriptor header to obtain wTotalLength.
            byte[] header = new byte[9];
            int transferred = _usb.ControlTransferIn(
                0x80,             // Device-to-host, Standard, Device
                0x06,             // GET_DESCRIPTOR
                0x02 << 8,        // Configuration descriptor, index 0
                0,
                header,
                header.Length);

            if (transferred < 9)
            {
                return sectors;
            }

            int totalLength = header[2] | (header[3] << 8);

            if (totalLength <= 9)
            {
                return sectors;
            }

            byte[] config = new byte[totalLength];
            transferred = _usb.ControlTransferIn(
                0x80,
                0x06,
                0x02 << 8,
                0,
                config,
                config.Length);

            if (transferred < totalLength)
            {
                return sectors;
            }

            // Walk the descriptor list looking for interface descriptors and their
            // iInterface strings, which carry the DfuSe memory layout.
            int offset = 0;

            while (offset + 2 <= config.Length)
            {
                int bLength = config[offset];

                if (bLength < 2)
                {
                    break;
                }

                int bDescriptorType = config[offset + 1];

                // Interface descriptor (0x04) is 9 bytes; iInterface is at offset 8.
                if (bDescriptorType == 0x04 && offset + 9 <= config.Length)
                {
                    byte iInterface = config[offset + 8];

                    if (iInterface != 0)
                    {
                        string layout = GetStringDescriptor(iInterface);

                        if (!string.IsNullOrEmpty(layout)
                            && layout.StartsWith("@Internal Flash", StringComparison.OrdinalIgnoreCase))
                        {
                            ParseDfuSeLayout(layout, sectors);

                            if (sectors.Count > 0)
                            {
                                return sectors;
                            }
                        }
                    }
                }

                offset += bLength;
            }

            return sectors;
        }

        /// <summary>
        /// Parses a DfuSe memory layout string into a list of flash sectors.
        /// </summary>
        /// <param name="layout">The layout string, e.g.
        /// <c>@Internal Flash /0x08000000/04*016Kg,01*064Kg,03*128Kg</c>.</param>
        /// <param name="sectors">The list to populate with (address, size) sectors.</param>
        internal static void ParseDfuSeLayout(string layout, List<(uint address, uint size)> sectors)
        {
            // Format: "@Name /<addr>/<spec>[,<spec>...][/<addr>/<spec>...]"
            // where each spec is "<count>*<size><unit><type>", e.g. "04*016Kg".
            string[] parts = layout.Split('/');

            for (int i = 1; i + 1 < parts.Length; i += 2)
            {
                string addrText = parts[i].Trim();
                string specText = parts[i + 1].Trim();

                if (!addrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    || !uint.TryParse(
                        addrText.Substring(2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out uint address))
                {
                    continue;
                }

                foreach (string rawSpec in specText.Split(','))
                {
                    string spec = rawSpec.Trim();
                    int star = spec.IndexOf('*');

                    if (star <= 0)
                    {
                        continue;
                    }

                    if (!int.TryParse(
                            spec.Substring(0, star),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int count)
                        || count <= 0)
                    {
                        continue;
                    }

                    string sizeText = spec.Substring(star + 1);
                    int digits = 0;

                    while (digits < sizeText.Length && char.IsDigit(sizeText[digits]))
                    {
                        digits++;
                    }

                    if (digits == 0
                        || !int.TryParse(
                            sizeText.Substring(0, digits),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int sizeValue)
                        || sizeValue <= 0)
                    {
                        continue;
                    }

                    uint multiplier = 1;

                    if (digits < sizeText.Length)
                    {
                        char unit = char.ToUpperInvariant(sizeText[digits]);

                        if (unit == 'K')
                        {
                            multiplier = 1024;
                        }
                        else if (unit == 'M')
                        {
                            multiplier = 1024 * 1024;
                        }
                    }

                    uint sectorSize = (uint)sizeValue * multiplier;

                    if (sectorSize == 0)
                    {
                        continue;
                    }

                    for (int c = 0; c < count; c++)
                    {
                        sectors.Add((address, sectorSize));
                        address += sectorSize;
                    }
                }
            }
        }

        /// <summary>
        /// Waits for the device to return to dfuIDLE or dfuDN-LOAD-IDLE state after a command.
        /// </summary>
        private void WaitForIdle(string context)
        {
            for (int i = 0; i < DfuConst.MaxStatusPolls; i++)
            {
                DfuStatusResult status = GetStatus();

                if (status.Status != DfuStatus.Ok)
                {
                    ClearStatus();
                    throw new DfuOperationFailedException(
                        $"DFU error during {context}: status={status.Status}, state={status.State}");
                }

                if (status.State == DfuState.DfuIdle ||
                    status.State == DfuState.DfuDnloadIdle)
                {
                    return;
                }

                // Use the poll timeout reported by the device, with a minimum of StatusPollInterval
                int pollMs = Math.Max(status.PollTimeout, DfuConst.StatusPollInterval);
                Thread.Sleep(pollMs);
            }

            throw new DfuOperationFailedException(
                $"Timeout waiting for DFU idle state during {context}.");
        }

        /// <summary>
        /// Waits for a flash erase (mass or page) to complete.
        /// STM32 bootloaders run the erase synchronously inside the DFU_GETSTATUS
        /// handler and stop servicing the USB control pipe while the flash is busy,
        /// so the control transfer for GET_STATUS can be aborted by the USB stack
        /// (WinUSB ERROR_SEM_TIMEOUT). Such timeouts are transient: we simply keep
        /// polling GET_STATUS until the device answers with an idle state or the
        /// overall erase budget elapses.
        /// </summary>
        /// <param name="context">Description of the operation, used in error messages.</param>
        private void WaitForEraseComplete(string context)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            while (true)
            {
                DfuStatusResult status;

                try
                {
                    status = GetStatus();
                }
                catch (DfuControlTimeoutException)
                {
                    // Device is still erasing and not servicing the control pipe yet.
                    if (stopwatch.ElapsedMilliseconds >= DfuConst.EraseTimeout)
                    {
                        throw new DfuOperationFailedException(
                            $"Timeout waiting for {context} to complete after {DfuConst.EraseTimeout} ms.");
                    }

                    // Brief pause so we don't busy-spin if the transport returns immediately.
                    Thread.Sleep(DfuConst.StatusPollInterval);
                    continue;
                }

                if (status.Status != DfuStatus.Ok)
                {
                    ClearStatus();
                    throw new DfuOperationFailedException(
                        $"DFU error during {context}: status={status.Status}, state={status.State}");
                }

                if (status.State == DfuState.DfuIdle ||
                    status.State == DfuState.DfuDnloadIdle)
                {
                    return;
                }

                if (stopwatch.ElapsedMilliseconds >= DfuConst.EraseTimeout)
                {
                    throw new DfuOperationFailedException(
                        $"Timeout waiting for {context} to complete after {DfuConst.EraseTimeout} ms.");
                }

                // Device is busy (dfuDNBUSY). Wait the advised poll timeout before retrying.
                int pollMs = Math.Max(status.PollTimeout, DfuConst.StatusPollInterval);
                Thread.Sleep(pollMs);
            }
        }

        /// <summary>
        /// Parses the serial number from a WinUSB device path.
        /// Example path: \\?\usb#vid_0483&amp;pid_df11#serial_number#{guid}
        /// </summary>
        private static string ParseSerialFromPath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
            {
                return string.Empty;
            }

            // Device path format: \\?\usb#vid_XXXX&pid_XXXX#SERIAL#{GUID}
            // The serial number is the 3rd segment when split by '#'
            string[] parts = devicePath.Split('#');

            if (parts.Length >= 3)
            {
                return parts[2];
            }

            return string.Empty;
        }

        #endregion
    }
}
