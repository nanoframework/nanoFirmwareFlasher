//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// CMSIS-DAP transport layer using USB HID.
    /// Communicates with CMSIS-DAP compliant debug probes via USB HID reports.
    /// Cross-platform: uses hid.dll on Windows, hidraw on Linux, IOKit on macOS.
    /// </summary>
    internal class CmsisDap : ISwdTransport
    {
        #region CMSIS-DAP command IDs

        internal const byte DapInfo = 0x00;
        internal const byte DapHostStatus = 0x01;
        internal const byte DapConnect = 0x02;
        internal const byte DapDisconnect = 0x03;
        internal const byte DapTransferConfigure = 0x04;
        internal const byte DapTransfer = 0x05;
        internal const byte DapTransferBlock = 0x06;
        internal const byte DapTransferAbort = 0x07;
        internal const byte DapWriteAbort = 0x08;
        internal const byte DapDelay = 0x09;
        internal const byte DapResetTarget = 0x0A;
        internal const byte DapSwjPins = 0x10;
        internal const byte DapSwjClock = 0x11;
        internal const byte DapSwjSequence = 0x12;
        internal const byte DapSwdConfigure = 0x13;
        internal const byte DapSwdSequence = 0x1D;

        // Response status
        internal const byte DapOk = 0x00;
        internal const byte DapError = 0xFF;

        // DAP_Connect port selection
        internal const byte DapPortSwd = 1;

        // DAP_Info IDs
        internal const byte DapInfoVendorId = 0x01;
        internal const byte DapInfoProductId = 0x02;
        internal const byte DapInfoSerialNumber = 0x03;
        internal const byte DapInfoFirmwareVersion = 0x04;
        internal const byte DapInfoDeviceVendor = 0x05;
        internal const byte DapInfoDeviceName = 0x06;
        internal const byte DapInfoCapabilities = 0xF0;
        internal const byte DapInfoPacketCount = 0xFE;
        internal const byte DapInfoPacketSize = 0xFF;

        #endregion

        private IHidDevice _hid;
        private bool _disposed;
        private int _reportSize = 65; // default HID report size (1 byte report ID + 64 bytes data)

        /// <summary>
        /// Gets the product name of the connected probe.
        /// </summary>
        public string ProductName { get; private set; }

        /// <summary>
        /// Gets the serial number of the connected probe.
        /// </summary>
        public string SerialNumber { get; private set; }

        /// <summary>
        /// Gets the packet size reported by the device.
        /// </summary>
        public int PacketSize => _reportSize - 1; // subtract report ID byte

        /// <summary>
        /// Enumerates all connected CMSIS-DAP HID devices.
        /// </summary>
        /// <returns>List of (productName, serialNumber, devicePath) tuples.</returns>
        internal static List<(string productName, string serialNumber, string devicePath)> Enumerate()
        {
            return HidDeviceFactory.Enumerate();
        }

        /// <summary>
        /// Opens a connection to a CMSIS-DAP probe by device path.
        /// </summary>
        internal void Open(string devicePath)
        {
            _hid = HidDeviceFactory.Create();
            _hid.Open(devicePath);

            _reportSize = _hid.ReportSize;
            ProductName = _hid.ProductName;
            SerialNumber = _hid.SerialNumber;
        }

        /// <summary>
        /// Sends a CMSIS-DAP command and returns the response.
        /// </summary>
        /// <param name="command">Command buffer (first byte is the command ID).</param>
        /// <returns>Response buffer (first byte is the command ID echo).</returns>
        internal byte[] Transfer(byte[] command)
        {
            // Build HID output report: report ID (0x00) + command padded to report size
            byte[] report = new byte[_reportSize];
            report[0] = 0x00; // HID report ID
            int copyLen = Math.Min(command.Length, _reportSize - 1);
            Buffer.BlockCopy(command, 0, report, 1, copyLen);

            if (!_hid.Write(report))
            {
                throw new SwdProtocolException("Failed to write to CMSIS-DAP device.");
            }

            // Read response
            byte[] response = new byte[_reportSize];
            int bytesRead = _hid.Read(response);

            if (bytesRead <= 0)
            {
                throw new SwdProtocolException("Failed to read from CMSIS-DAP device.");
            }

            // Skip report ID byte, return from offset 1
            byte[] result = new byte[_reportSize - 1];
            Buffer.BlockCopy(response, 1, result, 0, result.Length);
            return result;
        }

        #region CMSIS-DAP commands

        /// <summary>
        /// Sends DAP_Connect to connect in SWD mode.
        /// </summary>
        /// <returns>True if SWD connection was established.</returns>
        public bool Connect()
        {
            byte[] response = Transfer(new byte[] { DapConnect, DapPortSwd });
            return response[0] == DapConnect && response[1] == DapPortSwd;
        }

        /// <summary>
        /// Sends DAP_Disconnect.
        /// </summary>
        public void Disconnect()
        {
            Transfer(new byte[] { DapDisconnect });
        }

        /// <summary>
        /// Configures the SWJ clock frequency.
        /// </summary>
        /// <param name="frequencyHz">Clock frequency in Hz.</param>
        public bool SetClock(uint frequencyHz)
        {
            byte[] cmd = new byte[5];
            cmd[0] = DapSwjClock;
            cmd[1] = (byte)(frequencyHz & 0xFF);
            cmd[2] = (byte)((frequencyHz >> 8) & 0xFF);
            cmd[3] = (byte)((frequencyHz >> 16) & 0xFF);
            cmd[4] = (byte)((frequencyHz >> 24) & 0xFF);

            byte[] response = Transfer(cmd);
            return response[0] == DapSwjClock && response[1] == DapOk;
        }

        /// <summary>
        /// Configures transfer retries and timeouts.
        /// </summary>
        /// <param name="idleCycles">Number of idle cycles after each transfer.</param>
        /// <param name="waitRetry">Number of wait retries.</param>
        /// <param name="matchRetry">Number of match retries.</param>
        public bool TransferConfigure(byte idleCycles, ushort waitRetry, ushort matchRetry)
        {
            byte[] cmd = new byte[6];
            cmd[0] = DapTransferConfigure;
            cmd[1] = idleCycles;
            cmd[2] = (byte)(waitRetry & 0xFF);
            cmd[3] = (byte)((waitRetry >> 8) & 0xFF);
            cmd[4] = (byte)(matchRetry & 0xFF);
            cmd[5] = (byte)((matchRetry >> 8) & 0xFF);

            byte[] response = Transfer(cmd);
            return response[0] == DapTransferConfigure && response[1] == DapOk;
        }

        /// <summary>
        /// Configures SWD protocol parameters.
        /// </summary>
        /// <param name="turnaround">Turnaround period (1-4 clocks, value = clocks - 1).</param>
        public bool SwdConfigure(byte turnaround = 0)
        {
            byte[] response = Transfer(new byte[] { DapSwdConfigure, turnaround });
            return response[0] == DapSwdConfigure && response[1] == DapOk;
        }

        /// <summary>
        /// Sends DAP_SWJ_Sequence — one or more bit sequences through the SWJ interface.
        /// Used for JTAG-to-SWD switching and line reset.
        /// </summary>
        /// <param name="bitCount">Number of bits to send (1-256, where 0 means 256).</param>
        /// <param name="data">Bit data (LSB first, padded to byte boundary).</param>
        public bool SwjSequence(byte bitCount, byte[] data)
        {
            byte[] cmd = new byte[2 + data.Length];
            cmd[0] = DapSwjSequence;
            cmd[1] = bitCount;
            Buffer.BlockCopy(data, 0, cmd, 2, data.Length);

            byte[] response = Transfer(cmd);
            return response[0] == DapSwjSequence && response[1] == DapOk;
        }

        /// <summary>
        /// Performs a DAP_Transfer — reads or writes DP/AP registers.
        /// </summary>
        /// <param name="dapIndex">DAP index (0 for single-DAP).</param>
        /// <param name="requests">Array of transfer requests.</param>
        /// <returns>Array of 32-bit values read (for read requests).</returns>
        public uint[] ExecuteTransfer(byte dapIndex, TransferRequest[] requests)
        {
            // Build command: DapTransfer, DAP_Index, Transfer_Count, Request[0], [Data32[0]], ...
            int cmdSize = 3;
            for (int i = 0; i < requests.Length; i++)
            {
                cmdSize += 1; // request byte
                if ((requests[i].Request & 0x02) == 0) // bit 1 = 0 means WRITE
                {
                    cmdSize += 4; // 32-bit write data
                }
            }

            byte[] cmd = new byte[cmdSize];
            cmd[0] = DapTransfer;
            cmd[1] = dapIndex;
            cmd[2] = (byte)requests.Length;

            int pos = 3;

            for (int i = 0; i < requests.Length; i++)
            {
                cmd[pos++] = requests[i].Request;

                if ((requests[i].Request & 0x02) == 0) // WRITE
                {
                    cmd[pos++] = (byte)(requests[i].Data & 0xFF);
                    cmd[pos++] = (byte)((requests[i].Data >> 8) & 0xFF);
                    cmd[pos++] = (byte)((requests[i].Data >> 16) & 0xFF);
                    cmd[pos++] = (byte)((requests[i].Data >> 24) & 0xFF);
                }
            }

            byte[] response = Transfer(cmd);

            // Parse response: DapTransfer, Transfer_Count, Transfer_Response, Data[0..n]
            if (response[0] != DapTransfer)
            {
                throw new SwdProtocolException("Unexpected DAP_Transfer response");
            }

            int responseCount = response[1];
            byte responseStatus = response[2];

            if ((responseStatus & 0x07) != 0x01) // bits [2:0] should be 0b001 = OK/ACK
            {
                throw new SwdProtocolException(
                    $"DAP_Transfer failed. Response count: {responseCount}, status: 0x{responseStatus:X2}. " +
                    GetTransferErrorMessage(responseStatus));
            }

            // Extract read values
            var readValues = new List<uint>();
            int dataPos = 3;

            for (int i = 0; i < requests.Length; i++)
            {
                if ((requests[i].Request & 0x02) != 0) // bit 1 = 1 means READ
                {
                    if (dataPos + 4 <= response.Length)
                    {
                        uint val = (uint)(response[dataPos] | (response[dataPos + 1] << 8) |
                                          (response[dataPos + 2] << 16) | (response[dataPos + 3] << 24));
                        readValues.Add(val);
                        dataPos += 4;
                    }
                }
            }

            return readValues.ToArray();
        }

        /// <summary>
        /// Reads DAP_Info string by its ID.
        /// </summary>
        internal string ReadInfoString(byte infoId)
        {
            byte[] response = Transfer(new byte[] { DapInfo, infoId });

            if (response[0] != DapInfo)
            {
                return string.Empty;
            }

            byte length = response[1];

            if (length == 0)
            {
                return string.Empty;
            }

            byte[] strBytes = new byte[length];
            Buffer.BlockCopy(response, 2, strBytes, 0, length);
            return System.Text.Encoding.ASCII.GetString(strBytes).TrimEnd('\0');
        }

        /// <summary>
        /// Reads the DAP_Info packet size.
        /// </summary>
        internal ushort ReadPacketSize()
        {
            byte[] response = Transfer(new byte[] { DapInfo, DapInfoPacketSize });

            if (response[0] == DapInfo && response[1] >= 2)
            {
                return (ushort)(response[2] | (response[3] << 8));
            }

            return 64; // default
        }

        /// <summary>
        /// Performs a DAP reset on the target.
        /// </summary>
        internal bool ResetTarget()
        {
            byte[] response = Transfer(new byte[] { DapResetTarget });
            return response[0] == DapResetTarget && response[1] == DapOk;
        }

        /// <summary>
        /// Controls the SWJ/JTAG pins.
        /// </summary>
        /// <param name="pinOutput">Pin output values (bit field: SWCLK_TCK=0, SWDIO_TMS=1, TDI=2, TDO=3, nTRST=5, nRESET=7).</param>
        /// <param name="pinSelect">Pin select mask — 1 means the corresponding pin in pinOutput is driven.</param>
        /// <param name="waitUs">Wait timeout in microseconds.</param>
        /// <returns>Actual pin values.</returns>
        public byte SwjPins(byte pinOutput, byte pinSelect, uint waitUs)
        {
            byte[] cmd = new byte[7];
            cmd[0] = DapSwjPins;
            cmd[1] = pinOutput;
            cmd[2] = pinSelect;
            cmd[3] = (byte)(waitUs & 0xFF);
            cmd[4] = (byte)((waitUs >> 8) & 0xFF);
            cmd[5] = (byte)((waitUs >> 16) & 0xFF);
            cmd[6] = (byte)((waitUs >> 24) & 0xFF);

            byte[] response = Transfer(cmd);
            return response[1]; // pin input values
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _hid != null)
                {
                    _hid.Dispose();
                    _hid = null;
                }

                _disposed = true;
            }
        }

        ~CmsisDap()
        {
            Dispose(false);
        }

        #endregion

        #region Private helpers

        private static string GetTransferErrorMessage(byte status)
        {
            int ack = status & 0x07;

            switch (ack)
            {
                case 0x02:
                    return "WAIT response — target is busy. Try increasing retry count.";
                case 0x04:
                    return "FAULT response — target reported a fault. The debug interface may need to be reset.";
                default:
                    if ((status & 0x08) != 0)
                    {
                        return "Protocol error — SWD parity or framing error.";
                    }

                    return "No response from target — check probe connection and target power.";
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents a single DAP_Transfer request.
    /// </summary>
    internal struct TransferRequest
    {
        /// <summary>
        /// Request byte. Bit fields:
        ///   [0] = APnDP (0=DP, 1=AP)
        ///   [1] = RnW (0=Write, 1=Read)  
        ///   [2:3] = A[2:3] register address bits
        /// </summary>
        internal byte Request;

        /// <summary>
        /// 32-bit data for write requests, or 0 for reads.
        /// </summary>
        internal uint Data;

        /// <summary>
        /// Creates a DP read request.
        /// </summary>
        internal static TransferRequest DpRead(byte addr)
        {
            return new TransferRequest { Request = (byte)(0x02 | ((addr & 0x0C) << 0)), Data = 0 };
        }

        /// <summary>
        /// Creates a DP write request.
        /// </summary>
        internal static TransferRequest DpWrite(byte addr, uint data)
        {
            return new TransferRequest { Request = (byte)(0x00 | ((addr & 0x0C) << 0)), Data = data };
        }

        /// <summary>
        /// Creates an AP read request.
        /// </summary>
        internal static TransferRequest ApRead(byte addr)
        {
            return new TransferRequest { Request = (byte)(0x03 | ((addr & 0x0C) << 0)), Data = 0 };
        }

        /// <summary>
        /// Creates an AP write request.
        /// </summary>
        internal static TransferRequest ApWrite(byte addr, uint data)
        {
            return new TransferRequest { Request = (byte)(0x01 | ((addr & 0x0C) << 0)), Data = data };
        }
    }
}
