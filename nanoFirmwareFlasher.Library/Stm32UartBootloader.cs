//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.IO.Ports;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Implements the STM32 UART bootloader protocol as defined in ST AN3155.
    /// Provides low-level communication with the built-in bootloader present in
    /// the System Memory of all STM32 microcontrollers.
    /// </summary>
    public class Stm32UartBootloader : IDisposable
    {
        // Protocol bytes
        private const byte Ack = 0x79;
        private const byte Nack = 0x1F;
        private const byte SyncByte = 0x7F;

        // AN3155 command codes
        private const byte CmdGet = 0x00;
        private const byte CmdGetVersion = 0x01;
        private const byte CmdGetId = 0x02;
        private const byte CmdReadMemory = 0x11;
        private const byte CmdGo = 0x21;
        private const byte CmdWriteMemory = 0x31;
        private const byte CmdErase = 0x43;
        private const byte CmdExtendedErase = 0x44;

        // Limits
        private const int MaxWriteBlockSize = 256;
        private const int MaxReadBlockSize = 256;
        private const int DefaultTimeoutMs = 5000;
        private const int EraseTimeoutMs = 30000;

        private SerialPort _port;
        private bool _disposed;
        private byte _bootloaderVersion;
        private byte[] _supportedCommands;
        private bool _usesExtendedErase;

        /// <summary>
        /// Gets the bootloader version reported by the device.
        /// </summary>
        public byte BootloaderVersion => _bootloaderVersion;

        /// <summary>
        /// Gets whether the connected bootloader uses Extended Erase (0x44) or standard Erase (0x43).
        /// </summary>
        public bool UsesExtendedErase => _usesExtendedErase;

        /// <summary>
        /// Option to output progress messages.
        /// </summary>
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Normal;

        /// <summary>
        /// Opens a connection to the STM32 UART bootloader on the specified serial port
        /// and performs the initial synchronization handshake.
        /// </summary>
        /// <param name="portName">Serial port name (e.g. COM3, /dev/ttyUSB0).</param>
        /// <param name="baudRate">Baud rate to use. Default is 115200.</param>
        /// <exception cref="Stm32UartBootloaderException">Failed to sync or communicate with the bootloader.</exception>
        public void Open(string portName, int baudRate = 115200)
        {
            _port = new SerialPort(portName, baudRate, Parity.Even, 8, StopBits.One)
            {
                ReadTimeout = DefaultTimeoutMs,
                WriteTimeout = DefaultTimeoutMs,
                DtrEnable = false,
                RtsEnable = false,
            };

            _port.Open();

            // give the device time to settle after port open
            Thread.Sleep(100);

            // discard any leftover data
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            // AN3155: Send 0x7F to synchronize
            WriteByte(SyncByte);

            byte response = ReadByte();

            if (response != Ack)
            {
                throw new Stm32UartBootloaderException(
                    $"Bootloader sync failed. Expected ACK (0x{Ack:X2}), received 0x{response:X2}. " +
                    "Make sure the device is in bootloader mode (BOOT0 pin high).");
            }

            // Read bootloader capabilities via the Get command
            InitializeBootloader();
        }

        /// <summary>
        /// Gets the chip product ID from the bootloader.
        /// </summary>
        /// <returns>The 16-bit product ID (PID) of the connected STM32 device.</returns>
        /// <exception cref="Stm32UartBootloaderException">Communication error.</exception>
        public ushort GetChipId()
        {
            SendCommand(CmdGetId);

            // Response: N (number of bytes - 1), PID_high, PID_low, ACK
            byte n = ReadByte();
            byte[] pidBytes = ReadBytes(n + 1);
            WaitForAck("GetID data");

            // PID is MSB first
            return (ushort)((pidBytes[0] << 8) | pidBytes[1]);
        }

        /// <summary>
        /// Reads memory from the device.
        /// </summary>
        /// <param name="address">Start address to read from.</param>
        /// <param name="length">Number of bytes to read (1-256).</param>
        /// <returns>The data bytes read from the device.</returns>
        /// <exception cref="Stm32UartBootloaderException">Communication error or NACK received.</exception>
        public byte[] ReadMemory(uint address, int length)
        {
            if (length < 1 || length > MaxReadBlockSize)
            {
                throw new ArgumentOutOfRangeException(nameof(length), $"Read length must be 1-{MaxReadBlockSize}.");
            }

            SendCommand(CmdReadMemory);

            // Send address (4 bytes MSB first) + checksum (XOR of address bytes)
            SendAddress(address);

            // Send N-1 (number of bytes to read minus 1) + checksum (complement)
            byte n = (byte)(length - 1);
            WriteBytes(new byte[] { n, (byte)~n });
            WaitForAck("ReadMemory length");

            return ReadBytes(length);
        }

        /// <summary>
        /// Writes a block of data to the device memory.
        /// The data block size must not exceed 256 bytes and must be word-aligned.
        /// </summary>
        /// <param name="address">Start address (must be word-aligned for flash writes).</param>
        /// <param name="data">Data to write (max 256 bytes).</param>
        /// <exception cref="Stm32UartBootloaderException">Communication error or NACK received.</exception>
        public void WriteMemory(uint address, byte[] data)
        {
            if (data.Length == 0 || data.Length > MaxWriteBlockSize)
            {
                throw new ArgumentOutOfRangeException(nameof(data), $"Write block size must be 1-{MaxWriteBlockSize}.");
            }

            SendCommand(CmdWriteMemory);

            // Send address
            SendAddress(address);

            // Send: N (number of bytes - 1), data[0..N], checksum
            byte n = (byte)(data.Length - 1);
            byte checksum = n;

            for (int i = 0; i < data.Length; i++)
            {
                checksum ^= data[i];
            }

            byte[] block = new byte[1 + data.Length + 1];
            block[0] = n;
            Buffer.BlockCopy(data, 0, block, 1, data.Length);
            block[block.Length - 1] = checksum;

            WriteBytes(block);
            WaitForAck("WriteMemory data");
        }

        /// <summary>
        /// Writes a large buffer to flash, automatically splitting into 256-byte chunks.
        /// </summary>
        /// <param name="startAddress">Base flash address.</param>
        /// <param name="data">Data to write (any length).</param>
        /// <param name="progressCallback">Optional callback for progress reporting (bytes written, total bytes).</param>
        /// <exception cref="Stm32UartBootloaderException">Communication error.</exception>
        public void WriteMemoryBlock(uint startAddress, byte[] data, Action<int, int> progressCallback = null)
        {
            int offset = 0;

            while (offset < data.Length)
            {
                int chunkSize = Math.Min(MaxWriteBlockSize, data.Length - offset);
                byte[] chunk = new byte[chunkSize];
                Buffer.BlockCopy(data, offset, chunk, 0, chunkSize);

                WriteMemory(startAddress + (uint)offset, chunk);

                offset += chunkSize;
                progressCallback?.Invoke(offset, data.Length);
            }
        }

        /// <summary>
        /// Verifies a large buffer against flash by reading back in 256-byte chunks.
        /// </summary>
        /// <param name="startAddress">Base flash address.</param>
        /// <param name="expectedData">Expected data that was written.</param>
        /// <param name="progressCallback">Optional callback for progress reporting (bytes verified, total bytes).</param>
        /// <returns><c>true</c> if all data matches; <c>false</c> otherwise.</returns>
        public bool VerifyMemoryBlock(uint startAddress, byte[] expectedData, Action<int, int> progressCallback = null)
        {
            int offset = 0;

            while (offset < expectedData.Length)
            {
                int chunkSize = Math.Min(MaxReadBlockSize, expectedData.Length - offset);
                byte[] readBack = ReadMemory(startAddress + (uint)offset, chunkSize);

                for (int i = 0; i < chunkSize; i++)
                {
                    if (readBack[i] != expectedData[offset + i])
                    {
                        return false;
                    }
                }

                offset += chunkSize;
                progressCallback?.Invoke(offset, expectedData.Length);
            }

            return true;
        }

        /// <summary>
        /// Performs a global mass erase of the flash memory.
        /// </summary>
        /// <exception cref="Stm32UartBootloaderException">Communication error or NACK received.</exception>
        public void GlobalErase()
        {
            int previousTimeout = _port.ReadTimeout;
            _port.ReadTimeout = EraseTimeoutMs;

            try
            {
                if (_usesExtendedErase)
                {
                    // Extended Erase command (0x44)
                    SendCommand(CmdExtendedErase);

                    // Global mass erase: send 0xFFFF + checksum (0x00)
                    WriteBytes([0xFF, 0xFF, 0x00]);
                    WaitForAck("ExtendedErase mass erase");
                }
                else
                {
                    // Standard Erase command (0x43)
                    SendCommand(CmdErase);

                    // Global mass erase: send 0xFF + checksum (0x00)
                    WriteBytes([0xFF, 0x00]);
                    WaitForAck("Erase mass erase");
                }
            }
            finally
            {
                _port.ReadTimeout = previousTimeout;
            }
        }

        /// <summary>
        /// Erases specific flash pages using the Extended Erase command.
        /// </summary>
        /// <param name="pages">Array of page numbers to erase.</param>
        /// <exception cref="Stm32UartBootloaderException">Communication error or NACK received.</exception>
        public void ErasePages(ushort[] pages)
        {
            if (!_usesExtendedErase)
            {
                throw new Stm32UartBootloaderException(
                    "Page-level erase requires Extended Erase command (0x44), but the connected bootloader only supports standard Erase (0x43).");
            }

            int previousTimeout = _port.ReadTimeout;
            _port.ReadTimeout = EraseTimeoutMs;

            try
            {
                SendCommand(CmdExtendedErase);

                // N-1 (number of pages - 1) as 2 bytes, then page numbers as 2 bytes each, then checksum
                ushort n = (ushort)(pages.Length - 1);
                byte[] packet = new byte[2 + (pages.Length * 2) + 1];
                packet[0] = (byte)(n >> 8);
                packet[1] = (byte)(n & 0xFF);

                byte checksum = (byte)(packet[0] ^ packet[1]);

                for (int i = 0; i < pages.Length; i++)
                {
                    byte high = (byte)(pages[i] >> 8);
                    byte low = (byte)(pages[i] & 0xFF);
                    packet[2 + (i * 2)] = high;
                    packet[2 + (i * 2) + 1] = low;
                    checksum ^= high;
                    checksum ^= low;
                }

                packet[packet.Length - 1] = checksum;
                WriteBytes(packet);
                WaitForAck("ExtendedErase pages");
            }
            finally
            {
                _port.ReadTimeout = previousTimeout;
            }
        }

        /// <summary>
        /// Jumps to the specified address and starts execution.
        /// After this command the bootloader session is ended.
        /// </summary>
        /// <param name="address">The address to start execution from (usually 0x08000000 for STM32).</param>
        /// <exception cref="Stm32UartBootloaderException">Communication error or NACK received.</exception>
        public void Go(uint address)
        {
            SendCommand(CmdGo);
            SendAddress(address);
        }

        /// <summary>
        /// Closes the serial port connection.
        /// </summary>
        public void Close()
        {
            if (_port != null && _port.IsOpen)
            {
                _port.Close();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of managed resources.
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Close();
                    _port?.Dispose();
                }

                _disposed = true;
            }
        }

        #region Private methods

        private void InitializeBootloader()
        {
            // Issue Get command to learn bootloader version and supported commands
            SendCommand(CmdGet);

            // Response: N (num bytes to follow), bootloader version, cmd1, cmd2, ..., cmdN, ACK
            byte n = ReadByte();
            byte[] payload = ReadBytes(n + 1);
            WaitForAck("Get command");

            _bootloaderVersion = payload[0];
            _supportedCommands = new byte[n];
            Buffer.BlockCopy(payload, 1, _supportedCommands, 0, n);

            // Determine if this device uses Extended Erase or standard Erase
            _usesExtendedErase = Array.IndexOf(_supportedCommands, CmdExtendedErase) >= 0;
        }

        private void SendCommand(byte command)
        {
            // AN3155: send command byte followed by complement
            WriteBytes(new byte[] { command, (byte)~command });
            WaitForAck($"command 0x{command:X2}");
        }

        private void SendAddress(uint address)
        {
            // 4 bytes MSB first + XOR checksum of the 4 address bytes
            byte[] addrBytes = new byte[5];
            addrBytes[0] = (byte)((address >> 24) & 0xFF);
            addrBytes[1] = (byte)((address >> 16) & 0xFF);
            addrBytes[2] = (byte)((address >> 8) & 0xFF);
            addrBytes[3] = (byte)(address & 0xFF);
            addrBytes[4] = (byte)(addrBytes[0] ^ addrBytes[1] ^ addrBytes[2] ^ addrBytes[3]);

            WriteBytes(addrBytes);
            WaitForAck("address");
        }

        private void WaitForAck(string context)
        {
            byte response = ReadByte();

            if (response == Nack)
            {
                throw new Stm32UartBootloaderException($"NACK received during: {context}");
            }

            if (response != Ack)
            {
                throw new Stm32UartBootloaderException(
                    $"Unexpected response during {context}: 0x{response:X2} (expected ACK 0x{Ack:X2})");
            }
        }

        private void WriteByte(byte b)
        {
            _port.Write(new byte[] { b }, 0, 1);
        }

        private void WriteBytes(byte[] data)
        {
            _port.Write(data, 0, data.Length);
        }

        private byte ReadByte()
        {
            int b = _port.ReadByte();

            if (b < 0)
            {
                throw new Stm32UartBootloaderException("Failed to read byte from bootloader (timeout or end of stream).");
            }

            return (byte)b;
        }

        private byte[] ReadBytes(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;

            while (offset < count)
            {
                int read = _port.Read(buffer, offset, count - offset);

                if (read <= 0)
                {
                    throw new Stm32UartBootloaderException(
                        $"Failed to read expected {count} bytes from bootloader (got {offset} bytes).");
                }

                offset += read;
            }

            return buffer;
        }

        #endregion
    }
}
