//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// ARM ADIv5 SWD protocol layer.
    /// Provides Debug Port (DP) and Access Port (AP) register access through a SWD transport.
    /// </summary>
    internal class SwdProtocol : IDisposable
    {
        #region DP register addresses (A[3:2])

        /// <summary>DP IDCODE register (read, address 0x00).</summary>
        internal const byte DpIdcode = 0x00;

        /// <summary>DP ABORT register (write, address 0x00).</summary>
        internal const byte DpAbort = 0x00;

        /// <summary>DP CTRL/STAT register (read/write, address 0x04).</summary>
        internal const byte DpCtrlStat = 0x04;

        /// <summary>DP SELECT register (write, address 0x08).</summary>
        internal const byte DpSelect = 0x08;

        /// <summary>DP RDBUFF register (read, address 0x0C).</summary>
        internal const byte DpRdbuff = 0x0C;

        // CTRL/STAT bits
        internal const uint CtrlStatCsyspwrupreq = 1U << 30;
        internal const uint CtrlStatCdbgpwrupreq = 1U << 28;
        internal const uint CtrlStatCsyspwrupack = 1U << 31;
        internal const uint CtrlStatCdbgpwrupack = 1U << 29;
        internal const uint CtrlStatStickyerr = 1U << 5;
        internal const uint CtrlStatStickycmp = 1U << 4;
        internal const uint CtrlStatStickyorun = 1U << 1;

        // ABORT bits
        internal const uint AbortOrunerrclr = 1U << 4;
        internal const uint AbortWderrclr = 1U << 3;
        internal const uint AbortStkerrclr = 1U << 2;
        internal const uint AbortStkcmpclr = 1U << 1;
        internal const uint AbortDapabort = 1U << 0;

        #endregion

        #region AP register addresses

        /// <summary>MEM-AP CSW register (Control/Status Word, offset 0x00).</summary>
        internal const byte ApCsw = 0x00;

        /// <summary>MEM-AP TAR register (Transfer Address, offset 0x04).</summary>
        internal const byte ApTar = 0x04;

        /// <summary>MEM-AP DRW register (Data Read/Write, offset 0x0C).</summary>
        internal const byte ApDrw = 0x0C;

        /// <summary>AP IDR register (Identification, offset 0xFC).</summary>
        internal const byte ApIdr = 0xFC;

        // CSW bits for common configuration
        internal const uint CswSize32 = 0x02;        // 32-bit access
        internal const uint CswAddrinc_Single = 0x10; // single auto-increment
        internal const uint CswAddrinc_Off = 0x00;    // no auto-increment
        internal const uint CswDbgSwEnable = 1U << 31;
        internal const uint CswProt = 0x23000000;     // packed HNONSEC=1, MasterType=1

        #endregion

        // JTAG-to-SWD switching sequence (as per ARM ADIv5 spec)
        // More than 50 high bits, then 0xE79E (16-bit SWD magic), then 50+ high bits, then low bits
        private static readonly byte[] JtagToSwdSequence = new byte[]
        {
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 56 high bits (line reset)
            0x9E, 0xE7,                                 // 16-bit JTAG-to-SWD select (0xE79E, LSB first)
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 56 high bits (line reset)
            0x00                                        // idle cycles
        };

        private readonly ISwdTransport _dap;
        private uint _currentApSel;
        private uint _currentDpBank;
        private bool _disposed;

        /// <summary>
        /// Gets the Debug Port IDCODE.
        /// </summary>
        internal uint DpIdcodeValue { get; private set; }

        internal SwdProtocol(ISwdTransport dap)
        {
            _dap = dap ?? throw new ArgumentNullException(nameof(dap));
        }

        /// <summary>
        /// Initializes the SWD connection: JTAG-to-SWD switch, connect, configure.
        /// </summary>
        /// <param name="clockHz">SWD clock frequency in Hz (default 1 MHz).</param>
        internal void Initialize(uint clockHz = 1000000)
        {
            // Set clock
            if (!_dap.SetClock(clockHz))
            {
                throw new SwdProtocolException("Failed to set SWD clock frequency.");
            }

            // Connect in SWD mode
            if (!_dap.Connect())
            {
                throw new SwdProtocolException("Failed to connect to target via SWD.");
            }

            // Configure SWD (turnaround = 1 clock)
            if (!_dap.SwdConfigure(0))
            {
                throw new SwdProtocolException("Failed to configure SWD protocol.");
            }

            // Configure transfers: 8 idle cycles, 128 wait retries, 0 match retries
            if (!_dap.TransferConfigure(8, 128, 0))
            {
                throw new SwdProtocolException("Failed to configure DAP transfers.");
            }

            if (_dap is StLinkTransport stLinkAdapter)
            {
                // ST-LINK is a high-level adapter: its ENTER_SWD connect already powers up
                // the debug domain and configures the AP, and memory is accessed through the
                // probe's native read/write commands (see UsesNativeMemory / ReadMemoryWord).
                // The manual DAP-direct power-up and AP-register handshake are not reliably
                // supported by ST-LINK firmware, so read the IDCODE natively and skip the
                // manual ADIv5 bring-up.
                DpIdcodeValue = stLinkAdapter.ReadIdCodes();
                return;
            }

            // Perform JTAG-to-SWD switch sequence
            if (!_dap.SwjSequence(0, JtagToSwdSequence)) // 0 means 256 bits, but we only send needed
            {
                // Sending the full sequence length
            }

            // Send at least 136 bits (17 bytes of 0xFF) for line reset + switch
            _dap.SwjSequence((byte)(JtagToSwdSequence.Length * 8), JtagToSwdSequence);

            // Read IDCODE to verify connection
            DpIdcodeValue = ReadDp(DpIdcode);

            if (DpIdcodeValue == 0 || DpIdcodeValue == 0xFFFFFFFF)
            {
                throw new SwdProtocolException(
                    $"Invalid DP IDCODE: 0x{DpIdcodeValue:X8}. Check target connection.");
            }

            // Clear any sticky errors
            WriteDp(DpAbort,
                AbortOrunerrclr | AbortWderrclr | AbortStkerrclr | AbortStkcmpclr);

            // Request debug + system power-up.
            WriteDp(DpCtrlStat, CtrlStatCsyspwrupreq | CtrlStatCdbgpwrupreq);

            // Poll for the power-up acknowledgment. Some probes (notably ST-LINK in
            // DAP-direct mode) already power up the debug domain during their own connect
            // and may not surface the ACK bits through this path, so a missing ACK is
            // treated as non-fatal here and the connection is validated with a real AP
            // read below.
            uint ctrlStat = 0;

            for (int i = 0; i < 50; i++)
            {
                ctrlStat = ReadDp(DpCtrlStat);

                if ((ctrlStat & (CtrlStatCsyspwrupack | CtrlStatCdbgpwrupack))
                    == (CtrlStatCsyspwrupack | CtrlStatCdbgpwrupack))
                {
                    break;
                }

                Thread.Sleep(2);
            }

            // Select AP 0, bank 0
            _currentApSel = 0;
            _currentDpBank = 0;
            WriteDp(DpSelect, 0);

            bool powerAcked = (ctrlStat & (CtrlStatCsyspwrupack | CtrlStatCdbgpwrupack))
                              == (CtrlStatCsyspwrupack | CtrlStatCdbgpwrupack);

            if (!powerAcked)
            {
                // The ACK wasn't observed. Confirm whether the debug domain is actually up
                // by reading the AP Identification Register. A plausible value means the
                // probe powered up debug during connect and we can safely continue.
                uint apIdr = 0;

                try
                {
                    apIdr = ReadAp(ApIdr);
                }
                catch (SwdProtocolException)
                {
                    apIdr = 0;
                }

                if (apIdr == 0 || apIdr == 0xFFFFFFFF)
                {
                    throw new SwdProtocolException(
                        $"Debug access could not be established. CTRL/STAT: 0x{ctrlStat:X8}, AP IDR: 0x{apIdr:X8}. " +
                        "The target may be held in reset, unpowered, or in a low-power mode.");
                }
            }
        }

        /// <summary>
        /// Reads a Debug Port register.
        /// </summary>
        internal uint ReadDp(byte addr)
        {
            uint[] result = _dap.ExecuteTransfer(0, new[]
            {
                TransferRequest.DpRead(addr)
            });

            return result.Length > 0 ? result[0] : 0;
        }

        /// <summary>
        /// Writes a Debug Port register.
        /// </summary>
        internal void WriteDp(byte addr, uint data)
        {
            _dap.ExecuteTransfer(0, new[]
            {
                TransferRequest.DpWrite(addr, data)
            });
        }

        /// <summary>
        /// Selects the AP and register bank for subsequent AP accesses.
        /// </summary>
        /// <param name="apSel">AP selection (0-255).</param>
        /// <param name="addr">Register address (the bank is extracted from bits [7:4]).</param>
        internal void SelectApBank(uint apSel, byte addr)
        {
            uint bank = (uint)(addr & 0xF0);
            uint selectVal = (apSel << 24) | bank;

            if (selectVal != (_currentApSel << 24 | _currentDpBank))
            {
                WriteDp(DpSelect, selectVal);
                _currentApSel = apSel;
                _currentDpBank = bank;
            }
        }

        /// <summary>
        /// Reads an Access Port register.
        /// AP reads are posted — we must read RDBUFF to get the actual value.
        /// </summary>
        internal uint ReadAp(byte addr, uint apSel = 0)
        {
            SelectApBank(apSel, addr);

            // AP read is posted → issue read, then read RDBUFF for the result
            _dap.ExecuteTransfer(0, new[]
            {
                TransferRequest.ApRead(addr)
            });

            uint[] result = _dap.ExecuteTransfer(0, new[]
            {
                TransferRequest.DpRead(DpRdbuff)
            });

            return result.Length > 0 ? result[0] : 0;
        }

        /// <summary>
        /// Writes an Access Port register.
        /// </summary>
        internal void WriteAp(byte addr, uint data, uint apSel = 0)
        {
            SelectApBank(apSel, addr);

            _dap.ExecuteTransfer(0, new[]
            {
                TransferRequest.ApWrite(addr, data)
            });
        }

        /// <summary>
        /// Reads the AP IDR register for the given AP.
        /// </summary>
        internal uint ReadApIdr(uint apSel = 0)
        {
            return ReadAp(ApIdr, apSel);
        }

        /// <summary>
        /// Checks for and clears sticky errors.
        /// </summary>
        /// <returns>True if errors were found and cleared.</returns>
        internal bool ClearStickyErrors()
        {
            uint ctrlStat = ReadDp(DpCtrlStat);

            bool hadErrors = (ctrlStat & (CtrlStatStickyerr | CtrlStatStickycmp | CtrlStatStickyorun)) != 0;

            if (hadErrors)
            {
                WriteDp(DpAbort,
                    AbortOrunerrclr | AbortWderrclr | AbortStkerrclr | AbortStkcmpclr);
            }

            return hadErrors;
        }

        /// <summary>
        /// Issues a system reset via the AIRCR register (Application Interrupt and Reset Control).
        /// </summary>
        internal void SystemReset()
        {
            if (_dap is StLinkTransport stLink)
            {
                // ST-LINK is a high-level adapter: manual MEM-AP register access does not work,
                // so use the probe's native reset. First clear any halt-on-reset vector catch
                // that connect-under-reset may have armed (DEMCR.VC_CORERESET), so the core runs
                // the freshly flashed firmware instead of halting at the reset vector.
                try
                {
                    // DEMCR = TRCENA only (VC_CORERESET cleared).
                    stLink.WriteMemory32(0xE000EDFC, new byte[] { 0x00, 0x00, 0x00, 0x01 });
                }
                catch (SwdProtocolException)
                {
                    // Best-effort — proceed with the reset regardless.
                }

                stLink.ResetSystem();

                // Resume so the core executes the application after reset (it was halted for
                // flashing).
                stLink.RunCore();

                return;
            }

            const uint AIRCR_ADDR = 0xE000ED0C;
            const uint VECTKEY = 0x05FA0000;
            const uint SYSRESETREQ = 1U << 2;

            // Configure MEM-AP for 32-bit access, no auto-increment
            WriteAp(ApCsw, CswSize32 | CswAddrinc_Off | CswDbgSwEnable);

            // Set TAR to AIRCR
            WriteAp(ApTar, AIRCR_ADDR);

            // Write SYSRESETREQ with VECTKEY
            WriteAp(ApDrw, VECTKEY | SYSRESETREQ);

            // Wait for reset to complete
            Thread.Sleep(100);

            // Re-initialize DP after reset
            WriteDp(DpAbort,
                AbortOrunerrclr | AbortWderrclr | AbortStkerrclr | AbortStkcmpclr);
        }

        /// <summary>
        /// Halts the CPU core via the Debug Halting Control and Status Register.
        /// </summary>
        internal void HaltCore()
        {
            if (_dap is StLinkTransport stLink)
            {
                // Use the ST-LINK's native halt command (memory-mapped DHCSR access via
                // manual MEM-AP is not available through the ST-LINK DAP-direct path).
                stLink.HaltCore();
                Thread.Sleep(10);
                return;
            }

            const uint DHCSR_ADDR = 0xE000EDF0;
            const uint DBGKEY = 0xA05F0000;
            const uint C_DEBUGEN = 1U << 0;
            const uint C_HALT = 1U << 1;

            WriteAp(ApCsw, CswSize32 | CswAddrinc_Off | CswDbgSwEnable);
            WriteAp(ApTar, DHCSR_ADDR);
            WriteAp(ApDrw, DBGKEY | C_DEBUGEN | C_HALT);

            // Verify halt
            Thread.Sleep(10);
        }

        /// <summary>
        /// Resumes the CPU core.
        /// </summary>
        internal void ResumeCore()
        {
            if (_dap is StLinkTransport stLink)
            {
                stLink.RunCore();
                return;
            }

            const uint DHCSR_ADDR = 0xE000EDF0;
            const uint DBGKEY = 0xA05F0000;
            const uint C_DEBUGEN = 1U << 0;

            WriteAp(ApCsw, CswSize32 | CswAddrinc_Off | CswDbgSwEnable);
            WriteAp(ApTar, DHCSR_ADDR);
            WriteAp(ApDrw, DBGKEY | C_DEBUGEN); // C_HALT=0 → resume
        }

        /// <summary>
        /// Gets a value indicating whether the underlying transport provides its own
        /// (native) memory access, bypassing manual MEM-AP register access. This is the
        /// case for ST-LINK, which acts as a high-level adapter.
        /// </summary>
        internal bool UsesNativeMemory => _dap is StLinkTransport;

        /// <summary>
        /// Reads a 32-bit word from target memory, using the transport's native memory
        /// access when available (ST-LINK) or manual MEM-AP access otherwise.
        /// </summary>
        internal uint ReadMemoryWord(uint address)
        {
            if (_dap is StLinkTransport stLink)
            {
                uint[] words = stLink.ReadMemory32(address, 1);
                return words.Length > 0 ? words[0] : 0;
            }

            WriteAp(ApCsw, CswSize32 | CswAddrinc_Off | CswDbgSwEnable);
            WriteAp(ApTar, address);
            return ReadAp(ApDrw);
        }

        /// <summary>
        /// Writes a 32-bit word to target memory, using the transport's native memory
        /// access when available (ST-LINK) or manual MEM-AP access otherwise.
        /// </summary>
        internal void WriteMemoryWord(uint address, uint value)
        {
            if (_dap is StLinkTransport stLink)
            {
                stLink.WriteMemory32(address, new byte[]
                {
                    (byte)(value & 0xFF),
                    (byte)((value >> 8) & 0xFF),
                    (byte)((value >> 16) & 0xFF),
                    (byte)((value >> 24) & 0xFF),
                });

                return;
            }

            WriteAp(ApCsw, CswSize32 | CswAddrinc_Off | CswDbgSwEnable);
            WriteAp(ApTar, address);
            WriteAp(ApDrw, value);
        }

        /// <summary>
        /// Reads a block of 32-bit words using the transport's native memory access.
        /// Only valid when <see cref="UsesNativeMemory"/> is <see langword="true"/>.
        /// </summary>
        internal uint[] ReadMemoryBlock(uint address, int wordCount)
        {
            return ((StLinkTransport)_dap).ReadMemory32(address, wordCount);
        }

        /// <summary>
        /// Writes a block of raw bytes using the transport's native memory access.
        /// Only valid when <see cref="UsesNativeMemory"/> is <see langword="true"/>.
        /// </summary>
        internal void WriteMemoryBlock(uint address, byte[] data)
        {
            ((StLinkTransport)_dap).WriteMemory32(address, data);
        }

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
                        _dap.Disconnect();
                    }
                    catch
                    {
                        // Best-effort disconnect
                    }
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
