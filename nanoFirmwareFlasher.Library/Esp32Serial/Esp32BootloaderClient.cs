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

        /// <summary>Additional connection attempts used only after baseline retries fail.</summary>
        private const int AdaptiveExtraAttempts = 6;

        /// <summary>
        /// For Espressif VID ports with a non-standard PID, begin adaptive fallback earlier
        /// so USB-JTAG probing starts before all classic retries are exhausted.
        /// </summary>
        private const int EarlyAdaptiveStartAttemptForEspressifCustomPid = 3;

        private const uint Esp32C5PcrSysclkConfReg = 0x60096110;
        private const uint Esp32C5PcrSysclkXtalFreqMask = 0x7Fu << 24;
        private const int Esp32C5PcrSysclkXtalFreqShift = 24;

        private const uint Esp32P4EfuseBlock1Addr = 0x5012D044;
        private const uint Esp32P4EfuseRdRepeatData1Reg = 0x5012D034;
        private const uint Esp32P4EfuseDownloadModeXpdOnMask = 0x1u << 16;
        private const uint Esp32P4LpSystemRegAnaXpdPadGroupReg = 0x5011010C;
        private const uint Esp32P4PmuExtLdoP0_0P1AAnaReg = 0x501151BC;
        private const uint Esp32P4PmuAna0P1AEnCurLim0 = 1u << 27;
        private const uint Esp32P4PmuExtLdoP0_0P1AReg = 0x501151B8;
        private const uint Esp32P4Pmu0P1ATarget00 = 0xFFu << 23;
        private const uint Esp32P4Pmu0P1AForceTiehSel0 = 1u << 7;
        private const uint Esp32P4PmuDateReg = 0x501153FC;
        private const uint Esp32P4LpWdtConfig0Reg = 0x50116000;
        private const uint Esp32P4LpWdtWprotectReg = 0x50116018;
        private const uint Esp32P4LpSwdConfReg = 0x5011601C;
        private const uint Esp32P4LpSwdWprotectReg = 0x50116020;
        private const uint Esp32P4LpWdtWkey = 0x50D83AA1;
        private const uint Esp32P4LpSwdAutoFeedEn = 1u << 18;

        private const uint Esp32S31LpWdtConfig0Reg = 0x20801000;
        private const uint Esp32S31LpWdtConfig1Reg = 0x20801004;
        private const uint Esp32S31LpWdtWprotectReg = 0x20801018;
        private const uint Esp32S31LpWdtWkey = 0x50D83AA1;

        private const uint Esp32E22GpioStrapReg = 0xC310D000;
        private const uint Esp32E22GpioStrapSpiBootMask = 0x1u << 3;
        private const uint Esp32E22RtcCntlOption1Reg = 0x3F408128;
        private const uint Esp32E22RtcCntlForceDownloadBootMask = 0x1u;

        private SerialPort _port;
        private bool _disposed;
        private bool _isUsbJtag;
        private bool _p4FlashPrepared;
        private Esp32ChipConfig _runtimeConfig;

        private struct SyncAttemptStats
        {
            internal int Timeouts;
            internal int InvalidSlipFrames;
            internal int StaleFrames;
        }

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
            bool staleHeavyLastAttempt = false;
            int consecutiveTimeoutHeavy = 0;

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
                             && SerialPortUsbInfo.IsUsbJtagSerialPid(usbPid);
            bool isEspressifCustomPid = usbVid == SerialPortUsbInfo.EspressifVid
                                        && usbPid >= 0
                                        && !SerialPortUsbInfo.IsUsbJtagSerialPid(usbPid);
            _isUsbJtag = isUsbJtag;

            int adaptiveStartAttempt = isEspressifCustomPid
                ? Math.Min(maxRetries, EarlyAdaptiveStartAttemptForEspressifCustomPid)
                : maxRetries;

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
                else if (isEspressifCustomPid)
                {
                    OutputWriter.WriteLine(
                        $"USB device detected: VID=0x{usbVid:X4} PID=0x{usbPid:X4}"
                        + " (Espressif custom PID; enabling early adaptive USB-JTAG fallback)");
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

            int totalAttempts = maxRetries + AdaptiveExtraAttempts;

            for (int attempt = 0; attempt < totalAttempts; attempt++)
            {
                bool isAdaptiveAttempt = attempt >= adaptiveStartAttempt;

                if (isAdaptiveAttempt && attempt == adaptiveStartAttempt && Verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine("Switching to adaptive recovery attempts...");
                }

                // In adaptive mode, periodically reopen the port to recover from flaky USB/serial stack state.
                if (isAdaptiveAttempt && ((attempt - adaptiveStartAttempt) % 2 == 1 || consecutiveTimeoutHeavy >= 3))
                {
                    ReopenPortForRecovery();
                }

                // Clear buffer before reset so post-reset boot log is isolated.
                // Matches esptool's reset_input_buffer() before reset_strategy().
                _port.DiscardInBuffer();

                // Select reset strategy based on detected USB type.
                // Matches esptool: USB-JTAG resets ONLY when positively identified.
                // In adaptive recovery mode, probe USB-JTAG on alternating retries too,
                // which helps native USB boards recover if VID/PID detection was inconclusive.
                bool useUsbJtagReset = ShouldUseUsbJtagReset(isUsbJtag, isAdaptiveAttempt, attempt, adaptiveStartAttempt);

                if (useUsbJtagReset)
                {
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine(
                            $"[Connect] Attempt {attempt + 1}/{totalAttempts}: "
                            + (isUsbJtag ? "USB-JTAG reset" : "USB-JTAG fallback reset"));
                    }

                    Esp32ResetSequence.EnterBootloaderUsbJtag(_port);
                }
                else
                {
                    int delay = (attempt % 2 == 0)
                        ? Esp32ResetSequence.DefaultResetDelayMs
                        : Esp32ResetSequence.ExtraResetDelayMs;

                    if (consecutiveTimeoutHeavy >= 2)
                    {
                        delay = Math.Min(delay + (50 * (consecutiveTimeoutHeavy - 1)), 900);
                    }

                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Connect] Attempt {attempt + 1}/{totalAttempts}: ClassicReset (delay={delay}ms)");
                    }

                    Esp32ResetSequence.EnterBootloader(_port, delay);
                }

                // Read any boot log output from the ROM after reset.
                // This serves two purposes:
                //  1. Consumes boot log bytes so they don't confuse SYNC parsing.
                //  2. Detects the boot mode (download vs normal) for better diagnostics.
                // Matches esptool's boot log detection in _connect_attempt().
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
                        bool downloadMode = bootMatch.Groups[2].Success;
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
                if (staleHeavyLastAttempt)
                {
                    AggressiveDrainSyncResponses();
                }

                if (consecutiveTimeoutHeavy >= 2)
                {
                    int settleDelay = Math.Min(150 + ((consecutiveTimeoutHeavy - 2) * 50), 350);
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Connect] Applying extra settle delay before SYNC: {settleDelay}ms");
                    }

                    Thread.Sleep(settleDelay);
                }

                int syncSubAttempts = isAdaptiveAttempt
                    ? Math.Min(8, 3 + (attempt - adaptiveStartAttempt))
                    : 5;

                int syncTimeoutMs = 100;
                if (isAdaptiveAttempt && (attempt - adaptiveStartAttempt) >= 3)
                {
                    syncTimeoutMs = Math.Min(200, 150 + (((attempt - adaptiveStartAttempt) - 3) * 25));
                }

                // Try to sync
                if (TrySync(syncSubAttempts, syncTimeoutMs, 50, out SyncAttemptStats syncStats))
                {
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Connect] Sync succeeded on attempt {attempt + 1}/{totalAttempts}");
                    }

                    synced = true;
                    break;
                }
                else if (Verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine($"[Connect] Sync failed on attempt {attempt + 1}/{totalAttempts}");
                    OutputWriter.WriteLine(
                        $"[Connect] Sync diagnostics: timeouts={syncStats.Timeouts}, invalidSlip={syncStats.InvalidSlipFrames}, stale={syncStats.StaleFrames}");
                }

                bool timeoutHeavy = syncStats.Timeouts >= Math.Max(2, syncSubAttempts - 1);
                bool staleHeavy = syncStats.StaleFrames >= Math.Max(2, syncSubAttempts / 2)
                                  && syncStats.Timeouts == 0;

                consecutiveTimeoutHeavy = timeoutHeavy ? consecutiveTimeoutHeavy + 1 : 0;
                staleHeavyLastAttempt = staleHeavy;

                if (staleHeavy && Verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine("[Connect] Stale-response-heavy attempt detected, increasing cleanup.");
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

        internal static bool ShouldUseUsbJtagReset(bool isUsbJtag, bool isAdaptiveAttempt, int attempt, int adaptiveStartAttempt)
        {
            if (isUsbJtag)
            {
                return true;
            }

            return isAdaptiveAttempt && ((attempt - adaptiveStartAttempt) % 2 == 0);
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
        /// <param name="config">Detected chip configuration used for ROM-specific baud quirks.</param>
        internal void ChangeBaudRate(int newBaudRate, Esp32ChipConfig config = null)
        {
            int commandBaudRate = GetCommandBaudRate(newBaudRate, config);

            byte[] data = new byte[8];
            Esp32CommandPacket.WriteUInt32LE(data, 0, (uint)commandBaudRate);
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

        internal void PrepareForFlashOperations(Esp32ChipConfig config)
        {
            if (config == null || IsStubRunning)
            {
                return;
            }

            if (config.ChipType == "esp32p4")
            {
                PrepareEsp32P4ForFlashOperations();
            }
        }

        /// <summary>
        /// Perform a hard reset to exit the bootloader and run the application.
        /// </summary>
        internal void HardReset()
        {
            ThrowIfDisposed();

            if (_port.IsOpen)
            {
                if (_runtimeConfig?.ChipType == "esp32s31" && _runtimeConfig.UsesUsbOtg)
                {
                    WatchdogResetEsp32S31();
                    return;
                }

                if (_runtimeConfig?.ChipType == "esp32e22" && _runtimeConfig.UsesUsbOtg)
                {
                    uint strapReg = ReadRegister(Esp32E22GpioStrapReg);
                    uint forceDownloadReg = ReadRegister(Esp32E22RtcCntlOption1Reg);

                    if ((strapReg & Esp32E22GpioStrapSpiBootMask) == 0
                        && (forceDownloadReg & Esp32E22RtcCntlForceDownloadBootMask) == 0)
                    {
                        // Upstream routes this case through the chip's watchdog-reset path.
                        // We don't have a published E22-specific watchdog register sequence,
                        // so use the existing USB-aware hard-reset primitive after matching the branch condition.
                        Esp32ResetSequence.HardReset(_port, true);
                        return;
                    }
                }

                Esp32ResetSequence.HardReset(_port, _isUsbJtag);
            }
        }

        /// <summary>
        /// Tries to get security information (chip ID and Eco version) from the boot loader.
        /// </summary>
        /// <returns>A tuple containing the chip ID and sub-ID, or null if unavailable.</returns>
        internal (uint chipId, uint ecoVersion)? TryGetSecurityInfo()
        {
            byte[] data;

            if (IsStubRunning)
                return null;

            try
            {
                // Check if the GetSecurityInfo command is supported by trying to send it
                var resp = SendCommand(Esp32Command.GetSecurityInfo, Array.Empty<byte>());
                if (!resp.IsSuccess || resp.Data.Length < 20)
                    return null;

                data = resp.Data;
            }
            catch (TimeoutException)
            {
                // Command not supported or no response — treat as unavailable
                return null;
            }

            // Layout:
            // 0..3   = flags (ignored)
            // 4      = flash_crypt_cnt (ignored)
            // 5..11  = key_purposes (ignored)
            // 12..15 = chip_id (uint32, LE)
            // 16..19 = eco_version/api_version (uint32, LE)

            uint chipId = BitConverter.ToUInt32(data, 12);
            uint ecoVersion = BitConverter.ToUInt32(data, 16);

            return (chipId, ecoVersion);
        }


        /// <summary>
        /// Get the underlying serial port (for direct serial reading, e.g. PSRAM detection).
        /// </summary>
        internal SerialPort Port => _port;

        internal void SetRuntimeConfig(Esp32ChipConfig config)
        {
            _runtimeConfig = config;
        }

        private int GetCommandBaudRate(int requestedBaudRate, Esp32ChipConfig config)
        {
            if (IsStubRunning || config == null)
            {
                return requestedBaudRate;
            }

            if (config.ChipType == "esp32c2")
            {
                int crystalFrequency = EstimateCrystalFrequencyMHz(config);
                if (crystalFrequency == 26)
                {
                    return requestedBaudRate * 40 / 26;
                }
            }
            else if (config.ChipType == "esp32c5")
            {
                int crystalFrequency = EstimateCrystalFrequencyMHz(config);
                int romExpectedCrystalFrequency = (int)((ReadRegister(Esp32C5PcrSysclkConfReg) & Esp32C5PcrSysclkXtalFreqMask) >> Esp32C5PcrSysclkXtalFreqShift);

                if (crystalFrequency == 48 && romExpectedCrystalFrequency == 40)
                {
                    return requestedBaudRate * 40 / 48;
                }

                if (crystalFrequency == 40 && romExpectedCrystalFrequency == 48)
                {
                    return requestedBaudRate * 48 / 40;
                }
            }

            return requestedBaudRate;
        }

        private int EstimateCrystalFrequencyMHz(Esp32ChipConfig config)
        {
            uint uartClkDivAddr;

            if (config.ChipType == "esp32")
            {
                uartClkDivAddr = 0x3FF40014;
            }
            else if (config.ChipType == "esp32e22")
            {
                uartClkDivAddr = 0xC3102014;
            }
            else
            {
                uartClkDivAddr = 0x60000014;
            }

            uint uartClkDiv = ReadRegister(uartClkDivAddr);
            uint divisor = uartClkDiv & 0xFFFFF;
            double estXtal = ((double)CurrentBaudRate * divisor) / 1_000_000.0 / config.XtalClkDivider;

            if (estXtal > 45)
            {
                return 48;
            }

            if (estXtal > 33)
            {
                return 40;
            }

            return 26;
        }

        private void PrepareEsp32P4ForFlashOperations()
        {
            if (_p4FlashPrepared)
            {
                return;
            }

            if (_isUsbJtag)
            {
                WriteRegister(Esp32P4LpWdtWprotectReg, Esp32P4LpWdtWkey);
                WriteRegister(Esp32P4LpWdtConfig0Reg, 0);
                WriteRegister(Esp32P4LpWdtWprotectReg, 0);

                WriteRegister(Esp32P4LpSwdWprotectReg, Esp32P4LpWdtWkey);
                WriteRegister(Esp32P4LpSwdConfReg, ReadRegister(Esp32P4LpSwdConfReg) | Esp32P4LpSwdAutoFeedEn);
                WriteRegister(Esp32P4LpSwdWprotectReg, 0);
            }

            int revision = GetEsp32P4Revision();

            if (revision == 302
                && (ReadRegister(Esp32P4EfuseRdRepeatData1Reg) & Esp32P4EfuseDownloadModeXpdOnMask) != 0)
            {
                WriteRegister(Esp32P4PmuDateReg, 0);
                _p4FlashPrepared = true;
                return;
            }

            if (revision == 301 || revision == 302)
            {
                WriteRegister(Esp32P4LpSystemRegAnaXpdPadGroupReg, 1);
                Thread.Sleep(10);

                WriteRegister(Esp32P4PmuExtLdoP0_0P1AAnaReg, ReadRegister(Esp32P4PmuExtLdoP0_0P1AAnaReg) | Esp32P4PmuAna0P1AEnCurLim0);
                WriteRegister(Esp32P4PmuExtLdoP0_0P1AReg, ReadRegister(Esp32P4PmuExtLdoP0_0P1AReg) | Esp32P4Pmu0P1AForceTiehSel0);
                WriteRegister(Esp32P4PmuDateReg, ReadRegister(Esp32P4PmuDateReg) | 0x3u);
                Thread.Sleep(1);

                WriteRegister(Esp32P4PmuExtLdoP0_0P1AAnaReg, ReadRegister(Esp32P4PmuExtLdoP0_0P1AAnaReg) & ~Esp32P4PmuAna0P1AEnCurLim0);
                WriteRegister(Esp32P4PmuExtLdoP0_0P1AReg, ReadRegister(Esp32P4PmuExtLdoP0_0P1AReg) & ~Esp32P4Pmu0P1ATarget00);
                WriteRegister(Esp32P4PmuExtLdoP0_0P1AReg, ReadRegister(Esp32P4PmuExtLdoP0_0P1AReg) | 0x80u);
                WriteRegister(Esp32P4PmuExtLdoP0_0P1AReg, ReadRegister(Esp32P4PmuExtLdoP0_0P1AReg) & ~Esp32P4Pmu0P1AForceTiehSel0);
                Thread.Sleep(2);
            }

            _p4FlashPrepared = true;
        }

        private int GetEsp32P4Revision()
        {
            uint block1Word2 = ReadRegister(Esp32P4EfuseBlock1Addr + 8);
            int majorRev = (int)((((block1Word2 >> 23) & 0x01) << 2) | ((block1Word2 >> 4) & 0x03));
            int minorRev = (int)(block1Word2 & 0x0F);

            return (majorRev * 100) + minorRev;
        }

        private void WatchdogResetEsp32S31()
        {
            WriteRegister(Esp32S31LpWdtWprotectReg, Esp32S31LpWdtWkey);
            WriteRegister(Esp32S31LpWdtConfig1Reg, 2000);
            WriteRegister(Esp32S31LpWdtConfig0Reg, (1u << 31) | (5u << 28) | (1u << 8) | 2u);
            WriteRegister(Esp32S31LpWdtWprotectReg, 0);
            Thread.Sleep(500);
        }

        #region Sync Implementation

        /// <summary>
        /// Attempt to synchronize with the bootloader by sending a SYNC command.
        /// Makes multiple fast attempts within a single reset cycle.
        /// </summary>
        /// <returns>True if sync was successful.</returns>
        private bool TrySync(int subAttempts, int syncTimeoutMs, int interAttemptDelayMs, out SyncAttemptStats stats)
        {
            stats = new SyncAttemptStats();
            byte[] syncPacket = Esp32CommandPacket.BuildSync();

            for (int i = 0; i < subAttempts; i++)
            {
                try
                {
                    // Flush before each attempt, matching esptool's _connect_attempt loop
                    _port.DiscardInBuffer();
                    _port.BaseStream.Flush();

                    _port.Write(syncPacket, 0, syncPacket.Length);

                    byte[] responsePayload = SlipFraming.ReadFrame(_port, syncTimeoutMs);

                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine(
                            $"[Sync] Sub-attempt {i + 1}/{subAttempts}: got {responsePayload.Length} bytes,"
                            + $" dir=0x{(responsePayload.Length > 0 ? responsePayload[0] : 0):X2}");
                    }

                    if (responsePayload.Length < Esp32ResponsePacket.MinimumPacketSize)
                    {
                        stats.StaleFrames++;
                        Thread.Sleep(interAttemptDelayMs);
                        continue;
                    }

                    if (responsePayload[0] != Esp32ResponsePacket.ResponseDirection)
                    {
                        stats.StaleFrames++;
                        Thread.Sleep(interAttemptDelayMs);
                        continue;
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

                        stats.StaleFrames++;
                    }
                }
                catch (TimeoutException)
                {
                    stats.Timeouts++;
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Sync] Sub-attempt {i + 1}/{subAttempts}: timeout (no response)");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    stats.InvalidSlipFrames++;
                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"[Sync] Sub-attempt {i + 1}/{subAttempts}: invalid SLIP frame: {ex.Message}");
                    }
                }

                Thread.Sleep(interAttemptDelayMs);
            }

            return false;
        }

        private void AggressiveDrainSyncResponses()
        {
            int drained = 0;

            for (int i = 0; i < 16; i++)
            {
                try
                {
                    SlipFraming.ReadFrame(_port, 60);
                    drained++;
                }
                catch (TimeoutException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    // ignore malformed frame while draining
                }
            }

            _port.DiscardInBuffer();

            if (Verbosity >= VerbosityLevel.Diagnostic)
            {
                OutputWriter.WriteLine($"[Connect] Aggressive cleanup drained {drained} frame(s)");
            }
        }

        private void ReopenPortForRecovery()
        {
            if (Verbosity >= VerbosityLevel.Diagnostic)
            {
                OutputWriter.WriteLine("[Connect] Reopening serial port for recovery...");
            }

            if (_port.IsOpen)
            {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                _port.Close();
            }

            Thread.Sleep(120);

            _port.Open();
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
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
