//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// Transport abstraction for SWD debug probe communication.
    /// Implementations exist for CMSIS-DAP (USB HID) and ST-LINK V2/V3 (USB bulk).
    /// </summary>
    internal interface ISwdTransport : IDisposable
    {
        /// <summary>
        /// Gets the product name of the connected probe.
        /// </summary>
        string ProductName { get; }

        /// <summary>
        /// Gets the serial number of the connected probe.
        /// </summary>
        string SerialNumber { get; }

        /// <summary>
        /// Gets the packet size supported by the probe.
        /// </summary>
        int PacketSize { get; }

        /// <summary>
        /// Connects to the target in SWD mode.
        /// </summary>
        /// <returns>True if SWD connection was established.</returns>
        bool Connect();

        /// <summary>
        /// Disconnects from the target.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Sets the SWD clock frequency.
        /// </summary>
        /// <param name="frequencyHz">Clock frequency in Hz.</param>
        /// <returns>True if the clock was set successfully.</returns>
        bool SetClock(uint frequencyHz);

        /// <summary>
        /// Configures transfer retry and timeout parameters.
        /// </summary>
        /// <param name="idleCycles">Number of idle cycles after each transfer.</param>
        /// <param name="waitRetry">Number of wait retries.</param>
        /// <param name="matchRetry">Number of match retries.</param>
        /// <returns>True if configuration succeeded.</returns>
        bool TransferConfigure(byte idleCycles, ushort waitRetry, ushort matchRetry);

        /// <summary>
        /// Configures SWD protocol parameters.
        /// </summary>
        /// <param name="turnaround">Turnaround period (1-4 clocks, value = clocks - 1).</param>
        /// <returns>True if configuration succeeded.</returns>
        bool SwdConfigure(byte turnaround = 0);

        /// <summary>
        /// Sends a bit sequence through the SWJ/SWD interface.
        /// Used for JTAG-to-SWD switching and line reset.
        /// </summary>
        /// <param name="bitCount">Number of bits to send (1-256, where 0 means 256).</param>
        /// <param name="data">Bit data (LSB first, padded to byte boundary).</param>
        /// <returns>True if the sequence was sent successfully.</returns>
        bool SwjSequence(byte bitCount, byte[] data);

        /// <summary>
        /// Executes one or more DP/AP register read/write transfers.
        /// </summary>
        /// <param name="dapIndex">DAP index (0 for single-DAP systems).</param>
        /// <param name="requests">Array of transfer requests.</param>
        /// <returns>Array of 32-bit values read (for read requests).</returns>
        uint[] ExecuteTransfer(byte dapIndex, TransferRequest[] requests);

        /// <summary>
        /// Controls the SWJ/JTAG debug pins (nRESET, etc.).
        /// </summary>
        /// <param name="pinOutput">Pin output values.</param>
        /// <param name="pinSelect">Pin select mask.</param>
        /// <param name="waitUs">Wait timeout in microseconds.</param>
        /// <returns>Actual pin values.</returns>
        byte SwjPins(byte pinOutput, byte pinSelect, uint waitUs);
    }
}
