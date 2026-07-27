//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// ST-LINK V2/V3 USB transport layer.
    /// Communicates with ST-LINK debug probes via USB bulk transfers.
    /// Implements <see cref="ISwdTransport"/> so it can be used with
    /// <see cref="SwdProtocol"/>, <see cref="ArmMemAp"/>, and <see cref="Stm32FlashProgrammer"/>.
    /// Cross-platform: uses WinUSB on Windows, libusb-1.0 on Linux/macOS.
    /// </summary>
    internal class StLinkTransport : ISwdTransport
    {
        #region ST-LINK command constants

        // ST-LINK command sizes
        private const int CmdSize = 16;   // Command packet is always 16 bytes
        private const int DataInSize = 64; // Default data-in packet size

        // Top-level commands
        private const byte StLinkGetVersion = 0xF1;
        private const byte StLinkDebugCommand = 0xF2;
        private const byte StLinkDfuCommand = 0xF3;
        private const byte StLinkGetCurrentMode = 0xF5;
        private const byte StLinkGetTargetVoltage = 0xF7;

        // Debug sub-commands
        private const byte StLinkDebugEnterSwd = 0x30;

        // Wire-protocol selector that follows the APIv2 debug-enter command (0x30).
        // 0xA3 selects SWD; 0x00 would select JTAG. Most STM32 boards (Nucleo,
        // Discovery) wire the debug port for SWD only, so SWD must be requested
        // explicitly.
        private const byte StLinkDebugEnterSwdMode = 0xA3;

        private const byte StLinkDebugExit = 0x21;
        private const byte StLinkDebugReadIdCodes = 0x31;

        // Block memory access opcodes. These return / accept raw little-endian data of the
        // requested length (no status prefix). NOTE: 0x36/0x35 are the single-register
        // READ/WRITE_DEBUG_REG commands (status+value), NOT block memory access.
        private const byte StLinkDebugReadMemory32 = 0x07;  // STLINK_DEBUG_READMEM_32BIT
        private const byte StLinkDebugWriteMemory32 = 0x08;  // STLINK_DEBUG_WRITEMEM_32BIT
        private const byte StLinkDebugReadMemory8 = 0x0C;   // STLINK_DEBUG_READMEM_8BIT
        private const byte StLinkDebugWriteMemory8 = 0x0D;   // STLINK_DEBUG_WRITEMEM_8BIT
        private const byte StLinkDebugSetSwdClk = 0x43;
        private const byte StLinkDebugReadRegister = 0x33;
        private const byte StLinkDebugWriteRegister = 0x34;
        private const byte StLinkDebugRunCore = 0x09;
        private const byte StLinkDebugHaltCore = 0x02;
        private const byte StLinkDebugResetSys = 0x32;
        private const byte StLinkDebugReadCoreStatus = 0x01;

        // DAP register access (V2 API)
        private const byte StLinkDebugApiV2ReadDap = 0x45;
        private const byte StLinkDebugApiV2WriteDap = 0x46;

        // Reports whether the last block memory read/write actually succeeded at the
        // SWD protocol level (distinct from the USB transfer status).
        private const byte StLinkDebugApiV2GetLastRwStatus2 = 0x3E;

        // Hardware reset line (NRST) control (V2 API).
        // Used for "connect under reset": holding the MCU in reset lets the probe enter
        // SWD even when the running application gates the debug port (e.g. low-power modes).
        private const byte StLinkDebugApiV2DriveNrst = 0x3C;
        private const byte StLinkDebugApiV2DriveNrstLow = 0x00;
        private const byte StLinkDebugApiV2DriveNrstHigh = 0x01;
        private const byte StLinkDebugApiV2DriveNrstPulse = 0x02;

        // Cortex-M debug registers (System Control Space) used to arm a halt-on-reset so the
        // core comes out of reset already halted, before it can run code that re-gates debug.
        private const uint CortexMDhcsr = 0xE000EDF0;
        private const uint CortexMDemcr = 0xE000EDFC;
        // DHCSR = DBGKEY (0xA05F0000) | C_HALT (0x2) | C_DEBUGEN (0x1)
        private const uint CortexMDhcsrHalt = 0xA05F0003;
        // DEMCR = TRCENA (0x01000000) | VC_CORERESET (0x1) -> halt on core reset
        private const uint CortexMDemcrHaltOnReset = 0x01000001;
        // DEMCR = TRCENA only (VC_CORERESET cleared) -> run normally after reset
        private const uint CortexMDemcrTrcenaOnly = 0x01000000;

        // Mode values
        private const byte StLinkModeDebugSwd = 0x02;
        private const byte StLinkModeDfu = 0x00;
        private const byte StLinkModeMass = 0x01;

        // Status codes
        private const byte StLinkDebugErrOk = 0x80;

        // SWD clock speed divisor values (for STLINK_DEBUG_SET_SWD_CLK)
        // Maps to STLINK_DEBUG_APIV2_SWD_SET_FREQ
        private const ushort StLinkSwdClk4000K = 0;
        private const ushort StLinkSwdClk1800K = 1;
        private const ushort StLinkSwdClk1200K = 2;
        private const ushort StLinkSwdClk950K = 3;
        private const ushort StLinkSwdClk480K = 7;
        private const ushort StLinkSwdClk240K = 15;
        private const ushort StLinkSwdClk125K = 31;
        private const ushort StLinkSwdClk100K = 40;
        private const ushort StLinkSwdClk50K = 79;
        private const ushort StLinkSwdClk25K = 158;
        private const ushort StLinkSwdClk15K = 265;

        // ST-LINK VID and known PIDs
        private const ushort StLinkVid = 0x0483;
        private static readonly ushort[] StLinkPids = { 0x3748, 0x374B, 0x374D, 0x374E, 0x374F, 0x3752, 0x3753 };

        // USB endpoint addresses
        private const byte EpOut = 0x02; // Bulk OUT
        private const byte EpIn = 0x81;  // Bulk IN (V2)

        #endregion

        #region Platform USB abstraction

        private IStLinkUsb _usb;
        private bool _disposed;
        private ushort _stLinkVersion;
        private byte _jtag_api;

        public string ProductName { get; private set; }
        public string SerialNumber { get; private set; }
        public int PacketSize => 64;

        /// <summary>
        /// Opens a connection to an ST-LINK probe by device path.
        /// </summary>
        internal void Open(string devicePath)
        {
            // Some probes need a full USB reset before the first handshake succeeds
            // (documented OpenOCD workaround). Retry once with a reset in between.
            const int maxOpenAttempts = 2;
            SwdProtocolException lastError = null;
            bool resetAttempted = false;

            for (int attempt = 0; attempt < maxOpenAttempts; attempt++)
            {
                _usb = StLinkUsbFactory.Create();
                _usb.Open(devicePath);

                ProductName = _usb.ProductName ?? "ST-LINK";
                SerialNumber = _usb.SerialNumber ?? string.Empty;

                try
                {
                    ReadVersion();
                    LeaveCurrentMode();

                    return;
                }
                catch (SwdProtocolException ex)
                {
                    lastError = ex;

                    if (attempt == maxOpenAttempts - 1)
                    {
                        break;
                    }

                    resetAttempted = true;

                    OutputWriter.WriteLine(
                        "ST-LINK did not respond to the initial handshake - resetting the USB "
                        + "device and retrying (known issue with some ST-LINK/V2 probes)...");

                    try
                    {
                        _usb.ResetDevice();
                    }
                    catch
                    {
                        // Best-effort.
                    }

                    _usb.Dispose();
                    _usb = null;

                    Thread.Sleep(1000);
                }
            }

            // Note if a reset was already tried, so a repeat failure is unambiguous.
            string suffix = resetAttempted
                ? " (this occurred again even after a full USB device reset and reconnect.)"
                : string.Empty;

            throw new SwdProtocolException(lastError.Message + suffix, lastError);
        }

        /// <summary>
        /// Enumerates connected ST-LINK debug probes.
        /// </summary>
        internal static List<(string productName, string serialNumber, string devicePath)> Enumerate()
        {
            return StLinkUsbFactory.Enumerate();
        }

        #endregion

        #region ISwdTransport implementation

        public bool Connect()
        {
            // A plain SWD enter works when the target's debug port is available. Retry a
            // couple of times to ride out transient WAIT/timeout responses from the probe.
            if (TryEnterSwd(3))
            {
                return true;
            }

            // The target is likely running application code that gates the debug port (for
            // example an STM32 in a low-power STOP/STANDBY mode). Fall back to a
            // "connect under reset" strategy, mirroring what STM32CubeProgrammer's
            // "Under reset" mode (and the manual "hold NRST, release" procedure) do.
            OutputWriter.WriteLine(
                "Target did not respond to SWD - attempting to connect under reset...");

            // Strategy 1: hold the MCU in reset while connecting. The SWJ-DP stays alive in
            // the always-on debug power domain, so ENTER_SWD succeeds; arm a halt-on-reset
            // so the core stays halted the instant reset is released.
            if (TryConnectUnderReset())
            {
                return true;
            }

            // Strategy 2: pulse reset and race to enter SWD in the brief window right after
            // reset, before the application can disable debug. This matches pressing the
            // board's reset button while the tool connects.
            return TryConnectAfterResetPulse();
        }

        /// <summary>
        /// Connects while holding the target in hardware reset, then arms a halt-on-reset and
        /// releases reset so the core comes up halted.
        /// </summary>
        private bool TryConnectUnderReset()
        {
            DriveNrst(StLinkDebugApiV2DriveNrstLow);
            Thread.Sleep(20);

            bool entered = TryEnterSwd(5);

            if (entered)
            {
                // Best-effort: halt now and set vector-catch so the core halts at its reset
                // vector before executing any application code once reset is released.
                TryArmHaltOnReset();
            }

            Thread.Sleep(5);
            DriveNrst(StLinkDebugApiV2DriveNrstHigh);
            Thread.Sleep(20);

            if (entered)
            {
                // Re-assert the halt after the core has left reset.
                try
                {
                    HaltCore();
                }
                catch (SwdProtocolException)
                {
                    // Non-fatal: the connection is established either way.
                }
            }

            return entered;
        }

        /// <summary>
        /// Pulses the reset line and repeatedly attempts to enter SWD to catch the short
        /// window after reset before application code can disable the debug port.
        /// </summary>
        private bool TryConnectAfterResetPulse()
        {
            DriveNrst(StLinkDebugApiV2DriveNrstLow);
            Thread.Sleep(5);
            DriveNrst(StLinkDebugApiV2DriveNrstHigh);

            for (int i = 0; i < 30; i++)
            {
                if (TryEnterSwd(1))
                {
                    try
                    {
                        HaltCore();
                    }
                    catch (SwdProtocolException)
                    {
                        // Non-fatal.
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Arms a Cortex-M halt-on-reset (vector catch) and halts the core, best-effort.
        /// Used during connect-under-reset so the core does not run application code that
        /// would re-gate the debug port once reset is released.
        /// </summary>
        private void TryArmHaltOnReset()
        {
            try
            {
                HaltCore();
                WriteMemory32(CortexMDemcr, ToLittleEndian(CortexMDemcrHaltOnReset));
                WriteMemory32(CortexMDhcsr, ToLittleEndian(CortexMDhcsrHalt));
            }
            catch (SwdProtocolException)
            {
                // Best-effort: the debug registers may not be accessible while the core is
                // held in reset on some targets. The reset-pulse strategy covers those.
            }
        }

        private static byte[] ToLittleEndian(uint value)
        {
            return new byte[]
            {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF),
            };
        }

        /// <summary>
        /// Sends the ENTER_SWD command up to <paramref name="attempts"/> times and returns
        /// <see langword="true"/> as soon as the probe reports success.
        /// </summary>
        private bool TryEnterSwd(int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                byte[] cmd = BuildEnterSwdCommand();

                byte[] resp = SendCommand(cmd, 2);

                if (resp != null && resp.Length >= 2 && resp[0] == StLinkDebugErrOk)
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        /// <summary>
        /// Drives the target hardware reset (NRST) line via the ST-LINK APIv2 command.
        /// Failures are ignored: not every probe/board wires NRST, and this is only used
        /// as a best-effort fallback during connect.
        /// </summary>
        /// <param name="state">
        /// One of <see cref="StLinkDebugApiV2DriveNrstLow"/>,
        /// <see cref="StLinkDebugApiV2DriveNrstHigh"/> or
        /// <see cref="StLinkDebugApiV2DriveNrstPulse"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the probe acknowledged the command (status OK);
        /// <see langword="false"/> if it rejected it or the transfer failed.
        /// </returns>
        private bool DriveNrst(byte state)
        {
            try
            {
                byte[] resp = SendCommand(BuildDriveNrstCommand(state), 2);
                return resp != null && resp.Length >= 2 && resp[0] == StLinkDebugErrOk;
            }
            catch (SwdProtocolException)
            {
                // Best-effort only — ignore probes/boards that don't support NRST control.
                return false;
            }
        }

        /// <summary>
        /// Builds the ST-LINK APIv2 DRIVE_NRST command (0xF2 0x3C &lt;state&gt;) used for
        /// connect-under-reset. The opcode MUST be 0x3C; other values drive the wrong
        /// subsystem.
        /// </summary>
        internal static byte[] BuildDriveNrstCommand(byte state)
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugApiV2DriveNrst;
            cmd[2] = state;

            return cmd;
        }

        /// <summary>
        /// Builds the ST-LINK "enter debug" command for the SWD wire protocol.
        /// 0xF2 0x30 is the STLINK_DEBUG_APIv2_ENTER command; the third byte
        /// selects the wire protocol. It MUST be 0xA3 (SWD) — without it the probe
        /// defaults to JTAG entry, which fails on the many STM32 boards
        /// (Nucleo/Discovery, e.g. STM32F769I-DISCO) whose debug port is wired for
        /// SWD only.
        /// </summary>
        internal static byte[] BuildEnterSwdCommand()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugEnterSwd;
            cmd[2] = StLinkDebugEnterSwdMode;

            return cmd;
        }

        public void Disconnect()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugExit;

            SendCommand(cmd, 0);
        }

        public bool SetClock(uint frequencyHz)
        {
            ushort divisor = GetSwdClockDivisor(frequencyHz);

            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugSetSwdClk;
            cmd[2] = (byte)(divisor & 0xFF);
            cmd[3] = (byte)((divisor >> 8) & 0xFF);

            byte[] resp = SendCommand(cmd, 2);
            return resp != null && resp.Length >= 2 && resp[0] == StLinkDebugErrOk;
        }

        public bool TransferConfigure(byte idleCycles, ushort waitRetry, ushort matchRetry)
        {
            // ST-LINK protocol doesn't have an equivalent of DAP_TransferConfigure.
            // Retry behavior is handled at the protocol layer.
            return true;
        }

        public bool SwdConfigure(byte turnaround = 0)
        {
            // ST-LINK configures SWD parameters internally.
            return true;
        }

        public bool SwjSequence(byte bitCount, byte[] data)
        {
            // ST-LINK handles JTAG-to-SWD switching internally when entering SWD mode.
            // No explicit sequence needed.
            return true;
        }

        public uint[] ExecuteTransfer(byte dapIndex, TransferRequest[] requests)
        {
            var results = new List<uint>();

            for (int i = 0; i < requests.Length; i++)
            {
                bool isRead = (requests[i].Request & 0x02) != 0;
                bool isAp = (requests[i].Request & 0x01) != 0;
                byte regAddr = (byte)(requests[i].Request & 0x0C);

                if (isRead)
                {
                    uint value = isAp
                        ? ReadDapRegister(1, regAddr)
                        : ReadDapRegister(0, regAddr);
                    results.Add(value);
                }
                else
                {
                    if (isAp)
                    {
                        WriteDapRegister(1, regAddr, requests[i].Data);
                    }
                    else
                    {
                        WriteDapRegister(0, regAddr, requests[i].Data);
                    }
                }
            }

            return results.ToArray();
        }

        public byte SwjPins(byte pinOutput, byte pinSelect, uint waitUs)
        {
            // ST-LINK does not have a direct pin manipulation command like CMSIS-DAP.
            // For nRESET, use the dedicated reset commands.
            if ((pinSelect & 0x80) != 0) // nRESET pin
            {
                if ((pinOutput & 0x80) == 0)
                {
                    // Assert reset (pull low)
                    HardwareReset(true);
                }
                else
                {
                    // Deassert reset (release)
                    HardwareReset(false);
                    Thread.Sleep(10);
                }
            }

            return pinOutput; // Return requested state as "actual" state
        }

        #endregion

        #region ST-LINK specific operations

        /// <summary>
        /// Maximum bytes per single ST-LINK read/write memory transfer.
        /// </summary>
        private const int MaxTransferSize = 6144;

        /// <summary>
        /// Safe TAR auto-increment wrap boundary (bytes). A single block command must not
        /// cross this or the target may wrap the address instead of continuing (see
        /// OpenOCD's stlink_max_block_size()). 1024 is the conservative default for all cores.
        /// </summary>
        private const uint TarAutoIncrementBlock = 1024;

        /// <summary>
        /// Bytes transferable from <paramref name="address"/> before the next
        /// <see cref="TarAutoIncrementBlock"/> boundary.
        /// </summary>
        internal static int GetTarBlockRemaining(uint address)
        {
            uint remaining = TarAutoIncrementBlock - ((TarAutoIncrementBlock - 1) & address);
            return remaining == 0 ? 4 : (int)remaining;
        }

        /// <summary>
        /// Reads a 32-bit aligned block of memory.
        /// </summary>
        internal uint[] ReadMemory32(uint address, int wordCount)
        {
            uint[] result = new uint[wordCount];
            int wordsRead = 0;

            while (wordsRead < wordCount)
            {
                int remaining = wordCount - wordsRead;
                uint chunkAddr = address + (uint)(wordsRead * 4);

                // Cap chunk to the TAR wrap boundary (see TarAutoIncrementBlock).
                int boundaryLimitBytes = GetTarBlockRemaining(chunkAddr) & ~0x3;
                int maxChunkBytes = Math.Min(MaxTransferSize, Math.Max(boundaryLimitBytes, 4));
                int chunkWords = Math.Min(remaining, maxChunkBytes / 4);
                int chunkBytes = chunkWords * 4;

                byte[] cmd = BuildReadMemory32Command(chunkAddr, chunkBytes);

                byte[] data = SendCommand(cmd, chunkBytes);

                if (data == null || data.Length < chunkBytes)
                {
                    throw new SwdProtocolException(
                        $"Failed to read {chunkBytes} bytes from 0x{chunkAddr:X8}.");
                }

                for (int i = 0; i < chunkWords; i++)
                {
                    int off = i * 4;
                    result[wordsRead + i] = (uint)(data[off] | (data[off + 1] << 8) |
                                                   (data[off + 2] << 16) | (data[off + 3] << 24));
                }

                wordsRead += chunkWords;
            }

            return result;
        }

        /// <summary>
        /// Writes a 32-bit aligned block of memory.
        /// </summary>
        internal void WriteMemory32(uint address, byte[] data)
        {
            int offset = 0;

            while (offset < data.Length)
            {
                int remaining = data.Length - offset;
                uint chunkAddr = address + (uint)offset;

                // Cap chunk to the TAR wrap boundary (see ReadMemory32).
                int boundaryLimitBytes = GetTarBlockRemaining(chunkAddr) & ~0x3;
                int maxChunkBytes = Math.Min(MaxTransferSize, Math.Max(boundaryLimitBytes, 4));
                int chunkLen = Math.Min(remaining, maxChunkBytes);

                // Round down to 4-byte alignment
                chunkLen = (chunkLen / 4) * 4;

                if (chunkLen == 0)
                {
                    break;
                }

                byte[] cmd = BuildWriteMemory32Command(chunkAddr, chunkLen);

                byte[] chunk = new byte[chunkLen];
                Buffer.BlockCopy(data, offset, chunk, 0, chunkLen);

                SendCommandWithData(cmd, chunk);
                CheckLastReadWriteStatus(chunkAddr);

                offset += chunkLen;
            }
        }

        /// <summary>
        /// Queries whether the last block memory read/write succeeded at the SWD protocol
        /// level (GETLASTRWSTATUS2) and throws if it did not.
        /// </summary>
        private void CheckLastReadWriteStatus(uint address)
        {
            byte[] resp = SendCommand(BuildGetLastRwStatus2Command(), 12);

            if (resp != null && resp.Length >= 1 && resp[0] != StLinkDebugErrOk)
            {
                throw new SwdProtocolException(
                    $"ST-LINK memory write failed at 0x{address:X8}: status 0x{resp[0]:X2}.");
            }
        }

        /// <summary>
        /// Builds the ST-LINK GETLASTRWSTATUS2 command (0xF2 0x3E), which reports whether the
        /// last block memory read/write succeeded at the SWD protocol level.
        /// </summary>
        internal static byte[] BuildGetLastRwStatus2Command()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugApiV2GetLastRwStatus2;

            return cmd;
        }

        /// <summary>
        /// Builds the ST-LINK block memory <b>read</b> command (STLINK_DEBUG_READMEM_32BIT).
        /// The opcode MUST be 0x07 (block memory read, returns raw data); 0x36 is
        /// READ_DEBUG_REG (single register, returns status+value) and must not be used here.
        /// </summary>
        internal static byte[] BuildReadMemory32Command(uint address, int byteCount)
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugReadMemory32;
            cmd[2] = (byte)(address & 0xFF);
            cmd[3] = (byte)((address >> 8) & 0xFF);
            cmd[4] = (byte)((address >> 16) & 0xFF);
            cmd[5] = (byte)((address >> 24) & 0xFF);
            cmd[6] = (byte)(byteCount & 0xFF);
            cmd[7] = (byte)((byteCount >> 8) & 0xFF);

            return cmd;
        }

        /// <summary>
        /// Builds the ST-LINK block memory <b>write</b> command (STLINK_DEBUG_WRITEMEM_32BIT).
        /// The opcode MUST be 0x08 (block memory write); 0x35 is WRITE_DEBUG_REG (single
        /// register) and must not be used here.
        /// </summary>
        internal static byte[] BuildWriteMemory32Command(uint address, int byteCount)
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugWriteMemory32;
            cmd[2] = (byte)(address & 0xFF);
            cmd[3] = (byte)((address >> 8) & 0xFF);
            cmd[4] = (byte)((address >> 16) & 0xFF);
            cmd[5] = (byte)((address >> 24) & 0xFF);
            cmd[6] = (byte)(byteCount & 0xFF);
            cmd[7] = (byte)((byteCount >> 8) & 0xFF);

            return cmd;
        }

        /// <summary>
        /// Reads the target IDCODE via ST-LINK.
        /// </summary>
        internal uint ReadIdCodes()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugReadIdCodes;

            byte[] resp = SendCommand(cmd, 12);

            if (resp == null || resp.Length < 4)
            {
                return 0;
            }

            // Bytes 0-3: status + padding, bytes 4-7: IDCODE
            if (resp.Length >= 8)
            {
                return (uint)(resp[4] | (resp[5] << 8) | (resp[6] << 16) | (resp[7] << 24));
            }

            return 0;
        }

        /// <summary>
        /// Halts the target core.
        /// </summary>
        internal void HaltCore()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugHaltCore;

            SendCommand(cmd, 2);
        }

        /// <summary>
        /// Runs (resumes) the target core.
        /// </summary>
        internal void RunCore()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugRunCore;

            SendCommand(cmd, 2);
        }

        /// <summary>
        /// Resets the target system via the ST-LINK native system-reset command.
        /// </summary>
        /// <returns><see langword="true"/> if the probe acknowledged the command.</returns>
        internal bool ResetSystem()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugResetSys;

            byte[] resp = SendCommand(cmd, 2);
            Thread.Sleep(50);

            return resp != null && resp.Length >= 2 && resp[0] == StLinkDebugErrOk;
        }

        /// <summary>
        /// Resets the target and leaves it running the application. Clears any halt-on-reset
        /// vector catch, performs a debug system reset (works even when NRST is not wired),
        /// then drives a hardware NRST pulse — the equivalent of pressing the board's reset
        /// button — so the MCU boots cleanly into the freshly flashed firmware.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the probe acknowledged at least one reset method
        /// (system reset or NRST); <see langword="false"/> if every reset command was
        /// rejected, meaning the target was most likely NOT reset.
        /// </returns>
        internal bool ResetTarget()
        {
            // Clear DEMCR.VC_CORERESET so the core does not halt at the reset vector (it may
            // have been armed during connect-under-reset).
            try
            {
                WriteMemory32(CortexMDemcr, ToLittleEndian(CortexMDemcrTrcenaOnly));
            }
            catch (SwdProtocolException)
            {
                // Best-effort — continue with the reset.
            }

            bool acknowledged = false;

            // Software system reset + run. Works even on boards where NRST is not routed to
            // the ST-LINK.
            try
            {
                acknowledged |= ResetSystem();
                RunCore();
            }
            catch (SwdProtocolException)
            {
                // Best-effort.
            }

            // Hardware reset via the NRST line, matching the board's reset button. Done last
            // so the board ends up freshly reset and running.
            bool nrstLow = DriveNrst(StLinkDebugApiV2DriveNrstLow);
            Thread.Sleep(20);
            bool nrstHigh = DriveNrst(StLinkDebugApiV2DriveNrstHigh);
            Thread.Sleep(30);

            bool hardwareReset = false;

            if (nrstLow && nrstHigh)
            {
                acknowledged = true;
                hardwareReset = true;
            }
            else if (DriveNrst(StLinkDebugApiV2DriveNrstPulse))
            {
                // Some ST-LINK firmware only honors the single-shot pulse variant.
                acknowledged = true;
                hardwareReset = true;
                Thread.Sleep(30);
            }

            if (acknowledged && !hardwareReset)
            {
                // A soft (SYSRESETREQ) reset succeeded but the physical NRST line could not be
                // driven. Let the user know the reset was not a full hardware reset.
                OutputWriter.WriteLine(
                    "Note: the target NRST line is not available through the debug probe; a soft "
                    + "system reset was performed. Peripherals and the backup domain are not fully "
                    + "reset — press the board reset button or power-cycle for a full hardware reset.");
            }

            return acknowledged;
        }

        /// <summary>
        /// Checks if the target core is halted.
        /// </summary>
        internal bool IsCoreHalted()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugReadCoreStatus;

            byte[] resp = SendCommand(cmd, 4);

            if (resp != null && resp.Length >= 4)
            {
                uint status = (uint)(resp[0] | (resp[1] << 8) | (resp[2] << 16) | (resp[3] << 24));
                return (status & 0x01) != 0; // DHCSR S_HALT bit
            }

            return false;
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
                if (disposing)
                {
                    try
                    {
                        Disconnect();
                    }
                    catch
                    {
                        // Ignore disconnect errors during dispose
                    }

                    _usb?.Dispose();
                    _usb = null;
                }

                _disposed = true;
            }
        }

        ~StLinkTransport()
        {
            Dispose(false);
        }

        #endregion

        #region Private helpers

        private void ReadVersion()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkGetVersion;

            byte[] resp = SendCommand(cmd, 6);

            if (resp == null || resp.Length < 6)
            {
                throw new SwdProtocolException("Failed to read ST-LINK version.");
            }

            // Parse version: bytes 0-1 contain the version bitfield
            // Bits [15:12]=major, [11:6]=JTAG/SWD version, [5:0]=SWIM version
            ushort versionRaw = (ushort)((resp[0] << 8) | resp[1]);
            int major = (versionRaw >> 12) & 0x0F;
            int jtag = (versionRaw >> 6) & 0x3F;
            // int swim = versionRaw & 0x3F;  // not used

            _stLinkVersion = versionRaw;

            // V2 uses API v2 if JTAG version >= 11, V3 always uses API v2
            _jtag_api = (byte)(jtag >= 11 || major >= 3 ? 2 : 1);

            if (ProductName == null || ProductName == "ST-LINK")
            {
                ProductName = $"ST-LINK/V{major}";
            }
        }

        /// <summary>
        /// Reads the target VDD voltage in millivolts, or -1 if it could not be read.
        /// </summary>
        internal int ReadTargetVoltageMillivolts()
        {
            try
            {
                byte[] cmd = new byte[CmdSize];
                cmd[0] = StLinkGetTargetVoltage;

                byte[] resp = SendCommand(cmd, 8);

                if (resp == null || resp.Length < 8)
                {
                    return -1;
                }

                uint factor = (uint)(resp[0] | (resp[1] << 8) | (resp[2] << 16) | (resp[3] << 24));
                uint reading = (uint)(resp[4] | (resp[5] << 8) | (resp[6] << 16) | (resp[7] << 24));

                if (factor == 0)
                {
                    return -1;
                }

                return (int)(2400L * reading / factor);
            }
            catch (SwdProtocolException)
            {
                return -1;
            }
        }

        private void LeaveCurrentMode()
        {
            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkGetCurrentMode;

            byte[] resp = SendCommand(cmd, 2);

            if (resp == null || resp.Length < 2)
            {
                return;
            }

            byte mode = resp[0];

            if (mode == StLinkModeDebugSwd)
            {
                // Already in debug mode — leave first so we can re-enter cleanly
                byte[] leaveCmd = new byte[CmdSize];
                leaveCmd[0] = StLinkDebugCommand;
                leaveCmd[1] = StLinkDebugExit;
                SendCommand(leaveCmd, 0);
            }
        }

        private uint ReadDapRegister(byte port, byte addr)
        {
            if (_jtag_api < 2)
            {
                throw new SwdProtocolException(
                    "ST-LINK firmware is too old for DAP register access. " +
                    "Update your ST-LINK firmware to JTAG version >= 11.");
            }

            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugApiV2ReadDap;
            cmd[2] = port;   // 0 = DP, 1 = AP
            cmd[3] = 0;
            cmd[4] = addr;   // register address
            cmd[5] = 0;

            byte[] resp = SendCommand(cmd, 8);

            if (resp == null || resp.Length < 8)
            {
                throw new SwdProtocolException(
                    $"Failed to read DAP register (port={port}, addr=0x{addr:X2}).");
            }

            // Check status in first 2 bytes
            if (resp[0] != StLinkDebugErrOk)
            {
                throw new SwdProtocolException(
                    $"ST-LINK DAP read error: status=0x{resp[0]:X2} (port={port}, addr=0x{addr:X2}).");
            }

            return (uint)(resp[4] | (resp[5] << 8) | (resp[6] << 16) | (resp[7] << 24));
        }

        private void WriteDapRegister(byte port, byte addr, uint data)
        {
            if (_jtag_api < 2)
            {
                throw new SwdProtocolException(
                    "ST-LINK firmware is too old for DAP register access. " +
                    "Update your ST-LINK firmware to JTAG version >= 11.");
            }

            byte[] cmd = new byte[CmdSize];
            cmd[0] = StLinkDebugCommand;
            cmd[1] = StLinkDebugApiV2WriteDap;
            cmd[2] = port;
            cmd[3] = 0;
            cmd[4] = addr;
            cmd[5] = 0;
            cmd[6] = (byte)(data & 0xFF);
            cmd[7] = (byte)((data >> 8) & 0xFF);
            cmd[8] = (byte)((data >> 16) & 0xFF);
            cmd[9] = (byte)((data >> 24) & 0xFF);

            byte[] resp = SendCommand(cmd, 2);

            if (resp != null && resp.Length >= 2 && resp[0] != StLinkDebugErrOk)
            {
                throw new SwdProtocolException(
                    $"ST-LINK DAP write error: status=0x{resp[0]:X2} (port={port}, addr=0x{addr:X2}, data=0x{data:X8}).");
            }
        }

        private void HardwareReset(bool assert)
        {
            // Use SwjPins to toggle nRESET (not natively supported by ST-LINK directly via this method)
            // Instead, use system reset command
            if (assert)
            {
                // Hold core in halt and reset
                HaltCore();
            }
            else
            {
                ResetSystem();
            }
        }

        private ushort GetSwdClockDivisor(uint frequencyHz)
        {
            // Map desired frequency to the closest ST-LINK clock divisor
            if (frequencyHz >= 4000000)
            {
                return StLinkSwdClk4000K;
            }

            if (frequencyHz >= 1800000)
            {
                return StLinkSwdClk1800K;
            }

            if (frequencyHz >= 1200000)
            {
                return StLinkSwdClk1200K;
            }

            if (frequencyHz >= 950000)
            {
                return StLinkSwdClk950K;
            }

            if (frequencyHz >= 480000)
            {
                return StLinkSwdClk480K;
            }

            if (frequencyHz >= 240000)
            {
                return StLinkSwdClk240K;
            }

            if (frequencyHz >= 125000)
            {
                return StLinkSwdClk125K;
            }

            if (frequencyHz >= 100000)
            {
                return StLinkSwdClk100K;
            }

            if (frequencyHz >= 50000)
            {
                return StLinkSwdClk50K;
            }

            if (frequencyHz >= 25000)
            {
                return StLinkSwdClk25K;
            }

            return StLinkSwdClk15K;
        }

        private byte[] SendCommand(byte[] cmd, int responseLength)
        {
            // Some probes stall the bulk pipes intermittently; reset and retry.
            const int maxAttempts = 3;
            SwdProtocolException lastError = null;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    _usb.ResetPipes();
                }

                try
                {
                    _usb.WriteBulk(EpOut, cmd, cmd.Length);

                    if (responseLength <= 0)
                    {
                        return null;
                    }

                    byte[] response = new byte[responseLength];
                    int bytesRead = _usb.ReadBulk(EpIn, response, responseLength);

                    if (bytesRead < responseLength)
                    {
                        // Partial read — resize
                        byte[] partial = new byte[bytesRead];
                        Buffer.BlockCopy(response, 0, partial, 0, bytesRead);
                        return partial;
                    }

                    return response;
                }
                catch (SwdProtocolException ex)
                {
                    lastError = ex;
                }
            }

            throw lastError;
        }

        private void SendCommandWithData(byte[] cmd, byte[] data)
        {
            _usb.WriteBulk(EpOut, cmd, cmd.Length);
            _usb.WriteBulk(EpOut, data, data.Length);
        }

        #endregion
    }

    #region ST-LINK USB abstraction

    /// <summary>
    /// Low-level USB bulk transfer interface for ST-LINK probes.
    /// </summary>
    internal interface IStLinkUsb : IDisposable
    {
        void Open(string devicePath);
        void WriteBulk(byte endpoint, byte[] data, int length);
        int ReadBulk(byte endpoint, byte[] buffer, int length);
        void ResetPipes();
        void ResetDevice();
        string ProductName { get; }
        string SerialNumber { get; }
    }

    /// <summary>
    /// Factory for creating ST-LINK USB instances using LibUsbDotNet.
    /// </summary>
    internal static class StLinkUsbFactory
    {
        internal static IStLinkUsb Create()
        {
            return new LibUsbDotNetStLinkUsb();
        }

        internal static List<(string productName, string serialNumber, string devicePath)> Enumerate()
        {
            return LibUsbDotNetStLinkUsb.EnumerateDevices();
        }
    }

    /// <summary>
    /// Cross-platform ST-LINK USB implementation using LibUsbDotNet (bulk transfers).
    /// Replaces the per-platform WindowsStLinkUsb and LibUsbStLinkUsb implementations.
    /// </summary>
    internal class LibUsbDotNetStLinkUsb : IStLinkUsb
    {
        private const int StLinkVid = 0x0483;
        private static readonly int[] KnownPids = { 0x3748, 0x374B, 0x374D, 0x374E, 0x374F, 0x3752, 0x3753 };

        // PID of the standalone ST-LINK/V2 dongle. This is the only variant that
        // uses bulk OUT endpoint 0x02; the embedded ST-LINK/V2-1 (Nucleo/Discovery)
        // and all ST-LINK/V3 variants use bulk OUT endpoint 0x01.
        private const int StLinkV2Pid = 0x3748;

        private const int BulkTransferTimeout = 5000;

        // Appended to bulk-transfer failures when the probe enumerated but transfers failed.
        private const string DriverHint =
            " The probe was found but the USB transfer failed. If the ST-LINK debug interface is "
            + "still on ST's proprietary 'STM32 STLink' driver, bind it to a WinUSB-class driver "
            + "(WinUSB/libusbK/libusb-win32) with Zadig (https://zadig.akeo.ie). If it is already "
            + "on WinUSB, the probe may be an ST-LINK/V2 clone that does not tolerate raw USB "
            + "reliably (e.g. it stalls the response pipe) — try a genuine ST-LINK, a Nucleo/Discovery "
            + "on-board probe, or a CMSIS-DAP probe.";

        private LibUsbDotNet.UsbDevice _device;
        private LibUsbDotNet.UsbEndpointWriter _writer;
        private LibUsbDotNet.UsbEndpointReader _reader;
        private int _productId;
        private byte _writeEndpoint;
        private byte _readEndpoint;
        private string _endpointSource = "unknown";
        private bool _disposed;

        public string ProductName { get; private set; }
        public string SerialNumber { get; private set; }

        public void Open(string devicePath)
        {
            // Try each known ST-LINK PID
            for (int i = 0; i < KnownPids.Length && _device == null; i++)
            {
                var finder = new LibUsbDotNet.Main.UsbDeviceFinder(StLinkVid, KnownPids[i]);
                _device = LibUsbDotNet.UsbDevice.OpenUsbDevice(finder);

                if (_device != null)
                {
                    _productId = KnownPids[i];
                }
            }

            if (_device == null)
            {
                throw new SwdProtocolException(
                    "No ST-LINK device found. Make sure the probe is connected. " +
                    "On Windows, the native transport uses raw USB (LibUsbDotNet), so the ST-LINK debug interface must be bound to a WinUSB-class driver (WinUSB, libusbK or libusb-win32): use Zadig (https://zadig.akeo.ie) to install WinUSB on the 'ST-Link Debug' interface. " +
                    "ST's proprietary STSW-LINK009 driver is not compatible with this transport; reinstall it (https://www.st.com/en/development-tools/stsw-link009.html) only to restore STM32CubeProgrammer/STM32CubeIDE for that probe. " +
                    "On Linux, add a udev rule: echo 'SUBSYSTEM==\"usb\", ATTR{idVendor}==\"0483\", MODE=\"0666\"' " +
                    "| sudo tee /etc/udev/rules.d/70-st-link.rules && sudo udevadm control --reload-rules. " +
                    "On macOS, no additional drivers are needed.");
            }

            // If this is a "whole" USB device (libusb), we need to claim the interface
            LibUsbDotNet.IUsbDevice wholeDevice = _device as LibUsbDotNet.IUsbDevice;

            if (wholeDevice != null)
            {
                wholeDevice.SetAutoDetachKernelDriver(true);

                // Only reconfigure if not already on configuration 1.
                bool needsConfiguration = true;

                try
                {
                    if (wholeDevice.GetConfiguration(out byte currentConfig) && currentConfig == 1)
                    {
                        needsConfiguration = false;
                    }
                }
                catch
                {
                    // Not supported on this backend — fall back to setting it.
                }

                if (needsConfiguration)
                {
                    wholeDevice.SetConfiguration(1);
                }

                // If another app (STM32CubeProgrammer, ST-LINK Utility, a leftover nanoff
                // instance, etc.) already holds this interface, claiming fails and every
                // later transfer would fail with a confusing low-level USB error instead.
                if (!wholeDevice.ClaimInterface(0))
                {
                    throw new SwdProtocolException(
                        "Failed to claim the ST-LINK USB interface. It is likely already in "
                        + "use by another application (STM32CubeProgrammer, STM32CubeIDE, "
                        + "ST-LINK Utility, or a previous nanoff instance that did not release "
                        + "the probe). Close any application using the probe, unplug and "
                        + "replug it, and try again.");
                }
            }

            // Read product name and serial number
            try
            {
                if (_device.Info != null && !string.IsNullOrEmpty(_device.Info.ProductString))
                {
                    ProductName = _device.Info.ProductString;
                }
                else
                {
                    ProductName = "ST-LINK";
                }
            }
            catch
            {
                ProductName = "ST-LINK";
            }

            try
            {
                if (_device.Info != null && !string.IsNullOrEmpty(_device.Info.SerialString))
                {
                    SerialNumber = _device.Info.SerialString;
                }
                else
                {
                    SerialNumber = string.Empty;
                }
            }
            catch
            {
                SerialNumber = string.Empty;
            }

            // Open bulk endpoints. Prefer the endpoints advertised by the device's own USB
            // descriptors (robust across all ST-LINK variants and clones); fall back to the
            // PID-based heuristic if the descriptors can't be read. Using the wrong endpoint
            // makes the very first USB transfer fail on connect.
            DiscoverBulkEndpoints(out byte outEndpoint, out byte inEndpoint);

            _writeEndpoint = outEndpoint;
            _readEndpoint = inEndpoint;

            _writer = _device.OpenEndpointWriter((LibUsbDotNet.Main.WriteEndpointID)outEndpoint);
            _reader = _device.OpenEndpointReader((LibUsbDotNet.Main.ReadEndpointID)inEndpoint);

            // Match libusb-1.0's WinUSB backend (used by OpenOCD/st-link): enable
            // AUTO_CLEAR_STALL so a stalled pipe is cleared automatically instead of failing.
            if (_device is LibUsbDotNet.WinUsb.WindowsDevice windowsDevice)
            {
                try
                {
                    windowsDevice.EndpointPolicies((LibUsbDotNet.Main.ReadEndpointID)inEndpoint).AutoClearStall = true;
                    windowsDevice.EndpointPolicies((LibUsbDotNet.Main.WriteEndpointID)outEndpoint).AutoClearStall = true;
                }
                catch
                {
                    // Not supported on this backend — continue.
                }
            }

            // Clear any stale/halted pipe state left by a previous session (common with
            // ST-LINK/V2 dongles shared between tools). A halted pipe makes the first transfer
            // fail even though the endpoints are correct. Best-effort: ignore if unsupported.
            try
            {
                _writer.Reset();
            }
            catch
            {
                // Pipe reset not supported on this backend — continue.
            }

            try
            {
                _reader.Reset();
                _reader.ReadFlush();
            }
            catch
            {
                // Pipe reset/flush not supported on this backend — continue.
            }
        }

        /// <summary>
        /// Determines the bulk OUT (command) and bulk IN (response) endpoint addresses for the
        /// open device by inspecting its USB descriptors. Falls back to the PID-based defaults
        /// (<see cref="GetWriteEndpointForPid"/> and 0x81) when the descriptors are unavailable.
        /// The command IN endpoint is the lowest-address bulk IN (0x81), never the higher trace
        /// endpoint (0x83).
        /// </summary>
        private void DiscoverBulkEndpoints(out byte outEndpoint, out byte inEndpoint)
        {
            // PID-based defaults.
            outEndpoint = GetWriteEndpointForPid(_productId);
            inEndpoint = 0x81;
            _endpointSource = "PID heuristic (descriptors unavailable)";

            try
            {
                if (_device.Configs == null || _device.Configs.Count == 0)
                {
                    return;
                }

                var allEndpoints = new List<string>();

                foreach (LibUsbDotNet.Info.UsbInterfaceInfo iface in _device.Configs[0].InterfaceInfoList)
                {
                    byte foundOut = 0;
                    byte foundIn = 0;

                    foreach (LibUsbDotNet.Info.UsbEndpointInfo ep in iface.EndpointInfoList)
                    {
                        byte address = ep.Descriptor.EndpointID;
                        byte attrs = ep.Descriptor.Attributes;
                        bool isBulk = (attrs & 0x03) == 0x02;

                        allEndpoints.Add($"0x{address:X2}(attr=0x{attrs:X2})");

                        if (!isBulk)
                        {
                            continue;
                        }

                        if ((address & 0x80) == 0)
                        {
                            // Bulk OUT — take the first one (the command endpoint).
                            if (foundOut == 0)
                            {
                                foundOut = address;
                            }
                        }
                        else if (foundIn == 0 || address < foundIn)
                        {
                            // Bulk IN — take the lowest address (0x81), not trace (0x83).
                            foundIn = address;
                        }
                    }

                    if (foundOut != 0 && foundIn != 0)
                    {
                        outEndpoint = foundOut;
                        inEndpoint = foundIn;
                        _endpointSource = $"descriptors [{string.Join(",", allEndpoints)}]";
                        return;
                    }
                }

                _endpointSource = allEndpoints.Count > 0
                    ? $"PID heuristic (no bulk pair in descriptors [{string.Join(",", allEndpoints)}])"
                    : "PID heuristic (empty descriptors)";
            }
            catch
            {
                // Descriptor enumeration not available on this backend — keep the defaults.
                _endpointSource = "PID heuristic (descriptor read failed)";
            }
        }


        /// <summary>
        /// Gets the bulk OUT endpoint address to use for a given ST-LINK product ID.
        /// The standalone ST-LINK/V2 dongle (PID 0x3748) uses endpoint 0x02, while the
        /// embedded ST-LINK/V2-1 (found on Nucleo/Discovery boards such as the
        /// STM32F769I-DISCO) and all ST-LINK/V3 variants use endpoint 0x01.
        /// </summary>
        /// <param name="productId">USB product ID of the ST-LINK probe.</param>
        /// <returns>The bulk OUT endpoint address (0x01 or 0x02).</returns>
        internal static byte GetWriteEndpointForPid(int productId)
        {
            return productId == StLinkV2Pid ? (byte)0x02 : (byte)0x01;
        }

        public void WriteBulk(byte endpoint, byte[] data, int length)
        {
            var error = _writer.Write(data, 0, length, BulkTransferTimeout, out _);

            if (error != LibUsbDotNet.Main.ErrorCode.None)
            {
                throw new SwdProtocolException(
                    $"ST-LINK USB bulk write failed (PID 0x{_productId:X4}, OUT endpoint "
                    + $"0x{_writeEndpoint:X2}, source: {_endpointSource}). Error: {error} "
                    + $"(Win32 {LibUsbDotNet.UsbDevice.LastErrorNumber}: {LibUsbDotNet.UsbDevice.LastErrorString})."
                    + DriverHint);
            }
        }

        public int ReadBulk(byte endpoint, byte[] buffer, int length)
        {
            var error = _reader.Read(buffer, 0, length, BulkTransferTimeout, out int bytesRead);

            if (error != LibUsbDotNet.Main.ErrorCode.None)
            {
                throw new SwdProtocolException(
                    $"ST-LINK USB bulk read failed (PID 0x{_productId:X4}, IN endpoint "
                    + $"0x{_readEndpoint:X2}, source: {_endpointSource}). Error: {error} "
                    + $"(Win32 {LibUsbDotNet.UsbDevice.LastErrorNumber}: {LibUsbDotNet.UsbDevice.LastErrorString})."
                    + DriverHint);
            }

            return bytesRead;
        }

        public void ResetPipes()
        {
            // Clear a stalled bulk pipe and drop stale data. Best-effort.
            try
            {
                _writer?.Reset();
            }
            catch
            {
                // ignore
            }

            try
            {
                _reader?.Reset();
                _reader?.ReadFlush();
            }
            catch
            {
                // ignore
            }
        }

        public void ResetDevice()
        {
            // Full USB port reset (heavier than a pipe reset) — mirrors OpenOCD's
            // stlink_usb_usb_open() retry workaround.
            if (_device is LibUsbDotNet.IUsbDevice wholeDevice)
            {
                wholeDevice.ResetDevice();
            }
        }

        internal static List<(string productName, string serialNumber, string devicePath)> EnumerateDevices()
        {
            var devices = new List<(string productName, string serialNumber, string devicePath)>();

            LibUsbDotNet.Main.UsbRegDeviceList allDevices = LibUsbDotNet.UsbDevice.AllDevices;

            foreach (LibUsbDotNet.Main.UsbRegistry reg in allDevices)
            {
                if (reg.Vid != StLinkVid || !IsKnownPid((ushort)reg.Pid))
                {
                    continue;
                }

                string devicePath = reg.DevicePath ?? reg.SymbolicName ?? string.Empty;
                string serial = string.Empty;
                string productName = "ST-LINK";

                try
                {
                    LibUsbDotNet.UsbDevice dev = reg.Device;

                    if (dev != null)
                    {
                        if (dev.Info != null)
                        {
                            if (!string.IsNullOrEmpty(dev.Info.SerialString))
                            {
                                serial = dev.Info.SerialString;
                            }

                            if (!string.IsNullOrEmpty(dev.Info.ProductString))
                            {
                                productName = dev.Info.ProductString;
                            }
                        }

                        dev.Close();
                    }
                }
                catch
                {
                    // If we can't open it, use the device path
                }

                if (string.IsNullOrEmpty(serial))
                {
                    serial = devicePath;
                }

                devices.Add((productName, serial, devicePath));
            }

            return devices;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_device != null)
                {
                    LibUsbDotNet.IUsbDevice wholeDevice = _device as LibUsbDotNet.IUsbDevice;

                    if (wholeDevice != null)
                    {
                        try
                        {
                            wholeDevice.ReleaseInterface(0);
                        }
                        catch
                        {
                            // Ignore release errors during dispose
                        }
                    }

                    _device.Close();
                    _device = null;
                }

                _disposed = true;
            }
        }

        private static bool IsKnownPid(ushort pid)
        {
            for (int i = 0; i < KnownPids.Length; i++)
            {
                if (KnownPids[i] == pid)
                {
                    return true;
                }
            }

            return false;
        }
    }

    #endregion
}
