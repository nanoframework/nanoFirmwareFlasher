//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
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
            WaitForIdle("ErasePage");
        }

        /// <summary>
        /// Performs a DfuSe mass erase of the entire flash.
        /// </summary>
        internal void MassErase()
        {
            byte[] cmd = new byte[] { DfuConst.DfuSeErase };

            Download(0, cmd);
            WaitForIdle("MassErase");
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
