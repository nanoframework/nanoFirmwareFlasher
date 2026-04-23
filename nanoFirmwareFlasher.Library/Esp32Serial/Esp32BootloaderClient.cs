// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Core protocol client for communicating with the ESP32 ROM bootloader over serial.
    /// Manages connection, synchronization, command send/receive, and baud rate changes.
    /// </summary>
    internal class Esp32BootloaderClient : IDisposable
    {
        /// <summary>Default initial baud rate for ROM bootloader communication.</summary>
        internal const int DefaultBaudRate = 115200;

        /// <summary>Default timeout for commands in milliseconds.</summary>
        internal const int DefaultTimeoutMs = 3000;

        /// <summary>Longer timeout for erase/flash operations.</summary>
        internal const int EraseTimeoutMs = 60000;

        /// <summary>Number of extra sync responses to drain after successful sync.</summary>
        private const int SyncDrainCount = 7;

        private SerialPort _port;
        private bool _disposed;
        private bool _isUsbJtag;

        /// <summary>Whether the stub loader is currently running (affects response parsing).</summary>
        internal bool IsStubRunning { get; set; }

        /// <summary>The current baud rate of the serial connection.</summary>
        internal int CurrentBaudRate { get; private set; }

        /// <summary>Verbosity level for output messages.</summary>
        internal VerbosityLevel Verbosity { get; set; }

        /// <summary>
        /// Create a new bootloader client for the specified serial port.
        /// Does not open the port — call <see cref="Connect"/> to start.
        /// </summary>
        /// <param name="portName">Serial port name (e.g. "COM3", "/dev/ttyUSB0").</param>
        /// <param name="initialBaudRate">Initial baud rate (default 115200).</param>
        /// <param name="verbosity">Verbosity level for diagnostic output.</param>
        internal Esp32BootloaderClient(
            string portName,
            int initialBaudRate = DefaultBaudRate,
            VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            _port = new SerialPort(portName)
            {
                BaudRate = initialBaudRate,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                ReadTimeout = DefaultTimeoutMs,
                WriteTimeout = DefaultTimeoutMs,
                // Buffer sizes for flash operations
                ReadBufferSize = 64 * 1024,
                WriteBufferSize = 64 * 1024,
            };

            CurrentBaudRate = initialBaudRate;
            Verbosity = verbosity;
        }

        /// <summary>
        /// Open the serial port, enter bootloader via DTR/RTS reset, and synchronize.
        /// </summary>
        /// <param name="maxRetries">Maximum number of sync attempts (with reset between each).</param>
        /// <exception cref="EspToolExecutionException">Cannot connect to the bootloader.</exception>
        internal void Connect(int maxRetries = 10)
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }

            _port.Open();

            if (Verbosity >= VerbosityLevel.Diagnostic)
            {
                OutputWriter.WriteLine($"[Connect] Port {_port.PortName} opened, initializing DTR/RTS...");
            }

            // Explicitly de-assert DTR/RTS after open to prevent spurious reset.
            // On Windows, some USB CDC drivers (usbser.sys) may toggle DTR during
            // CreateFile. esptool sets rts=False, dtr=False before open; .NET doesn't
            // allow that, so we do it immediately after.
            _port.DtrEnable = false;
            _port.RtsEnable = false;

            // Brief settle time for USB-to-UART bridge drivers
            // Keeping this for debug pusposes, but esptool does NOT have a delay here after open and before reset.
            // Thread.Sleep(50);

            // Flush any garbage in the buffers
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            bool synced = false;
            bool promptShown = false;

            // Detect USB device type to choose the optimal reset strategy.
            // Matches esptool's _construct_reset_strategy_sequence():
            //   - If PID matches Espressif USB-JTAG (0x1001) → only USB-JTAG resets
            //   - Otherwise (including unknown VID/PID) → only classic UART resets
            // esptool NEVER interleaves USB-JTAG with classic resets because the
            // USB-JTAG sequence disrupts the auto-reset circuit capacitors on
            // boards with USB-to-UART bridges, causing subsequent classic resets
            // to fail (chip boots normally instead of entering download mode).
            var (usbVid, usbPid) = SerialPortUsbInfo.GetUsbIds(_port.PortName);
            bool isUsbJtag = usbVid == SerialPortUsbInfo.EspressifVid
                             && usbPid == SerialPortUsbInfo.UsbJtagSerialPid;
            _isUsbJtag = isUsbJtag;

            // Show the prompt after a few failed attempts
            int promptAfterAttempt = 7;

            if (Verbosity >= VerbosityLevel.Diagnostic)
            {
                if (isUsbJtag)
                {
                    OutputWriter.WriteLine(
                        $"USB device detected: VID=0x{usbVid:X4} PID=0x{usbPid:X4}"
                        + " (Espressif USB-JTAG/Serial)");
                }
                else if (usbVid >= 0 && usbPid >= 0)
                {
                    OutputWriter.WriteLine(
                        $"USB device detected: VID=0x{usbVid:X4} PID=0x{usbPid:X4}");
                }
                else
                {
                    OutputWriter.WriteLine("USB VID/PID not available, using classic reset sequence.");
                }
            }

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                // Clear buffer before reset so post-reset boot log is isolated.
                // Matches esptool's reset_input_buffer() before reset_strategy().
                _port.DiscardInBuffer();

                // Select reset strategy based on detected USB type.
                // Matches esptool: USB-JTAG resets ONLY when positively identified.
                // For all other cases (known UART bridge OR unknown VID/PID),
                // use ClassicReset with cycling delays (50ms / 550ms).
                if (isUsbJtag)
                {
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Connect] Attempt {attempt + 1}/{maxRetries}: USB-JTAG reset");
                    }

                    Esp32ResetSequence.EnterBootloaderUsbJtag(_port);
                }
                else
                {
                    int delay = (attempt % 2 == 0)
                        ? Esp32ResetSequence.DefaultResetDelayMs
                        : Esp32ResetSequence.ExtraResetDelayMs;

                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Connect] Attempt {attempt + 1}/{maxRetries}: ClassicReset (delay={delay}ms)");
                    }

                    Esp32ResetSequence.EnterBootloader(_port, delay);
                }

                // Read any boot log output from the ROM after reset.
                // This serves two purposes:
                //  1. Consumes boot log bytes so they don't confuse SYNC parsing.
                //  2. Detects the boot mode (download vs normal) for better diagnostics.
                // Matches esptool's boot log detection in _connect_attempt().
                bool bootLogDetected = false;
                bool downloadMode = false;
                int waiting = _port.BytesToRead;

                if (waiting > 0)
                {
                    byte[] bootBytes = new byte[waiting];
                    int bytesRead = _port.Read(bootBytes, 0, waiting);

                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Connect] Post-reset: {bytesRead} bytes in buffer");
                    }

                    // Look for the ROM boot log pattern: "boot:0x??(.*waiting for download)?"
                    // ESP32-S2 and later output at 115200 baud, so this can match.
                    // ESP32 (original) outputs at 74880 baud — will appear as garbage, no match.
                    string bootText = System.Text.Encoding.ASCII.GetString(bootBytes, 0, bytesRead);
                    Match bootMatch = Regex.Match(bootText, @"boot:(0x[0-9a-fA-F]+)(.*waiting for download)?", RegexOptions.Singleline);

                    if (bootMatch.Success)
                    {
                        bootLogDetected = true;
                        downloadMode = bootMatch.Groups[2].Success;

                        if (Verbosity >= VerbosityLevel.Diagnostic)
                        {
                            OutputWriter.WriteLine(
                                $"[Connect] Boot log: mode={bootMatch.Groups[1].Value}"
                                + (downloadMode ? " (download mode)" : " (normal boot)"));
                        }
                    }
                    else if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        // Show hex of first 32 bytes if no boot pattern matched
                        int showLen = Math.Min(bytesRead, 32);
                        string hex = BitConverter.ToString(bootBytes, 0, showLen).Replace("-", " ");
                        OutputWriter.WriteLine($"[Connect] Post-reset data (no boot pattern): {hex}{(bytesRead > 32 ? "..." : "")}");
                    }
                }
                else if (Verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine("[Connect] Post-reset: 0 bytes in buffer (no boot log)");
                }

                if (!promptShown && attempt >= promptAfterAttempt)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Magenta;
                    OutputWriter.WriteLine("*** Hold down the BOOT/FLASH button in ESP32 board ***");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    promptShown = true;
                }

                // Try to sync
                if (TrySync())
                {
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Connect] Sync succeeded on attempt {attempt + 1}/{maxRetries}");
                    }

                    synced = true;
                    break;
                }
                else if (Verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine($"[Connect] Sync failed on attempt {attempt + 1}/{maxRetries}");
                }
            }

            if (!synced)
            {
                _port.Close();
                throw new EspToolExecutionException(
                    "Failed to connect to ESP32 bootloader. No sync response received.");
            }

            // Drain any remaining sync responses
            DrainSyncResponses();

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"Connected to ESP32 bootloader on {_port.PortName} @ {CurrentBaudRate} baud.");
            }
        }

        /// <summary>
        /// Send a command packet and wait for the matching response.
        /// </summary>
        /// <param name="command">Command opcode.</param>
        /// <param name="data">Optional payload data.</param>
        /// <param name="checksum">Checksum (for data-bearing commands).</param>
        /// <param name="timeoutMs">Response timeout in milliseconds.</param>
        /// <returns>Parsed response packet.</returns>
        /// <exception cref="TimeoutException">No response received within timeout.</exception>
        /// <exception cref="Esp32BootloaderException">The bootloader returned an error.</exception>
        internal Esp32ResponsePacket SendCommand(
            Esp32Command command,
            byte[] data = null,
            uint checksum = 0,
            int timeoutMs = DefaultTimeoutMs)
        {
            ThrowIfDisposed();

            // Build and send the SLIP-framed packet
            byte[] packet = Esp32CommandPacket.Build(command, data, checksum);
            _port.Write(packet, 0, packet.Length);

            // Wait until all bytes have been physically transmitted by the UART.
            // Without this, the read timeout would start while data is still in
            // the OS output buffer — at 115200 baud a 6 KB stub block takes ~550 ms
            // to transmit, eating into the response timeout.
            _port.BaseStream.Flush();

            // Read responses until we get one matching our command
            // The bootloader may send leftover responses from previous commands
            var overallTimeout = Stopwatch.StartNew();

            while (overallTimeout.ElapsedMilliseconds < timeoutMs)
            {
                int remainingMs = Math.Max(100, timeoutMs - (int)overallTimeout.ElapsedMilliseconds);

                byte[] responsePayload;

                try
                {
                    responsePayload = SlipFraming.ReadFrame(_port, remainingMs);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException(
                        $"No response for command {command} within {timeoutMs}ms.");
                }

                // Validate minimum length
                if (responsePayload.Length < Esp32ResponsePacket.MinimumPacketSize)
                {
                    // Too short — skip and try next frame
                    continue;
                }

                // Check if it's a response (direction = 0x01)
                if (responsePayload[0] != Esp32ResponsePacket.ResponseDirection)
                {
                    continue;
                }

                var response = Esp32ResponsePacket.Parse(responsePayload, IsStubRunning);

                // Check if this response matches our command
                if (response.Command == command)
                {
                    return response;
                }

                // Otherwise it's a stale response — discard and read next
            }

            throw new TimeoutException(
                $"No matching response for command {command} within {timeoutMs}ms.");
        }

        /// <summary>
        /// Read a 32-bit hardware register on the ESP32.
        /// </summary>
        /// <param name="address">Register address.</param>
        /// <returns>The 32-bit register value.</returns>
        internal uint ReadRegister(uint address)
        {
            byte[] data = new byte[4];
            Esp32CommandPacket.WriteUInt32LE(data, 0, address);

            var response = SendCommand(Esp32Command.ReadReg, data);
            response.ThrowIfError();

            return response.Value;
        }

        /// <summary>
        /// Write a 32-bit hardware register on the ESP32.
        /// </summary>
        /// <param name="address">Register address.</param>
        /// <param name="value">Value to write.</param>
        /// <param name="mask">Bit mask (default: all bits).</param>
        /// <param name="delayUs">Delay in microseconds after write.</param>
        internal void WriteRegister(uint address, uint value, uint mask = 0xFFFFFFFF, uint delayUs = 0)
        {
            byte[] data = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(data, 0, address);
            Esp32CommandPacket.WriteUInt32LE(data, 4, value);
            Esp32CommandPacket.WriteUInt32LE(data, 8, mask);
            Esp32CommandPacket.WriteUInt32LE(data, 12, delayUs);

            var response = SendCommand(Esp32Command.WriteReg, data);
            response.ThrowIfError();
        }

        /// <summary>
        /// Change the serial communication baud rate.
        /// For ROM bootloader: only the new baud rate is used (old is ignored).
        /// For stub: both old and new baud rates are sent.
        /// </summary>
        /// <param name="newBaudRate">New baud rate to switch to.</param>
        internal void ChangeBaudRate(int newBaudRate)
        {
            byte[] data = new byte[8];
            Esp32CommandPacket.WriteUInt32LE(data, 0, (uint)newBaudRate);
            Esp32CommandPacket.WriteUInt32LE(data, 4, IsStubRunning ? (uint)CurrentBaudRate : 0);

            var response = SendCommand(Esp32Command.ChangeBaudrate, data);
            response.ThrowIfError();

            // Update the serial port baud rate on our side
            _port.BaudRate = newBaudRate;
            CurrentBaudRate = newBaudRate;

            // Small delay for the baud rate change to take effect
            Thread.Sleep(50);

            // Flush any garbage from the baud rate transition
            _port.DiscardInBuffer();
        }

        /// <summary>
        /// Perform a hard reset to exit the bootloader and run the application.
        /// </summary>
        internal void HardReset()
        {
            ThrowIfDisposed();

            if (_port.IsOpen)
            {
                Esp32ResetSequence.HardReset(_port, _isUsbJtag);
            }
        }

        /// <summary>
        /// Get the underlying serial port (for direct serial reading, e.g. PSRAM detection).
        /// </summary>
        internal SerialPort Port => _port;

        #region Sync Implementation

        /// <summary>
        /// Attempt to synchronize with the bootloader by sending a SYNC command.
        /// Makes multiple fast attempts within a single reset cycle.
        /// </summary>
        /// <returns>True if sync was successful.</returns>
        private bool TrySync()
        {
            byte[] syncPacket = Esp32CommandPacket.BuildSync();

            // esptool does 5 sync attempts per reset cycle, each with
            // flush_input + flushOutput + sync(timeout=0.1).
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    // Flush before each attempt, matching esptool's _connect_attempt loop
                    _port.DiscardInBuffer();
                    _port.BaseStream.Flush();

                    _port.Write(syncPacket, 0, syncPacket.Length);

                    // SYNC_TIMEOUT = 0.1s in esptool
                    byte[] responsePayload = SlipFraming.ReadFrame(_port, 100);

                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine(
                            $"[Sync] Sub-attempt {i + 1}/5: got {responsePayload.Length} bytes,"
                            + $" dir=0x{(responsePayload.Length > 0 ? responsePayload[0] : 0):X2}");
                    }

                    if (responsePayload.Length >= Esp32ResponsePacket.MinimumPacketSize
                        && responsePayload[0] == Esp32ResponsePacket.ResponseDirection)
                    {
                        var response = Esp32ResponsePacket.Parse(responsePayload, IsStubRunning);

                        if (response.Command == Esp32Command.Sync && response.IsSuccess)
                        {
                            return true;
                        }

                        if (Verbosity >= VerbosityLevel.Diagnostic)
                        {
                            OutputWriter.WriteLine(
                                $"[Sync] Response: cmd=0x{(byte)response.Command:X2}"
                                + $" success={response.IsSuccess} value=0x{response.Value:X8}");
                        }
                    }
                }
                catch (TimeoutException)
                {
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Sync] Sub-attempt {i + 1}/5: timeout (no response)");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Sync] Sub-attempt {i + 1}/5: invalid SLIP frame: {ex.Message}");
                    }
                }

                Thread.Sleep(50);
            }

            return false;
        }

        /// <summary>
        /// After a successful sync, drain any extra sync responses that the bootloader may send.
        /// The ROM bootloader sends multiple (up to 8) responses to a single SYNC.
        /// </summary>
        private void DrainSyncResponses()
        {
            int drained = 0;

            for (int i = 0; i < SyncDrainCount; i++)
            {
                try
                {
                    SlipFraming.ReadFrame(_port, 200);
                    drained++;
                }
                catch (TimeoutException)
                {
                    // No more responses to drain
                    break;
                }
                catch (InvalidOperationException)
                {
                    // Invalid frame — ignore
                }
            }

            if (Verbosity >= VerbosityLevel.Diagnostic)
            {
                OutputWriter.WriteLine($"[Connect] Drained {drained} extra sync response(s)");
            }

            // Final buffer flush
            _port.DiscardInBuffer();
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Close the serial port and release resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                if (_port != null)
                {
                    if (_port.IsOpen)
                    {
                        try
                        {
                            _port.DiscardInBuffer();
                            _port.DiscardOutBuffer();
                            _port.Close();
                        }
                        catch
                        {
                            // Swallow exceptions during cleanup
                        }
                    }

                    _port.Dispose();
                    _port = null;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Esp32BootloaderClient));
            }
        }

        #endregion
    }
}
