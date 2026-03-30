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

            // Power up debug and system
            WriteDp(DpCtrlStat, CtrlStatCsyspwrupreq | CtrlStatCdbgpwrupreq);

            // Wait for power-up acknowledgment
            int retries = 100;
            uint ctrlStat;

            do
            {
                ctrlStat = ReadDp(DpCtrlStat);
                retries--;

                if (retries <= 0)
                {
                    throw new SwdProtocolException(
                        "Timeout waiting for debug power-up. CTRL/STAT: 0x" + ctrlStat.ToString("X8"));
                }

                Thread.Sleep(10);
            }
            while ((ctrlStat & (CtrlStatCsyspwrupack | CtrlStatCdbgpwrupack))
                   != (CtrlStatCsyspwrupack | CtrlStatCdbgpwrupack));

            // Select AP 0, bank 0
            _currentApSel = 0;
            _currentDpBank = 0;
            WriteDp(DpSelect, 0);
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
            const uint DHCSR_ADDR = 0xE000EDF0;
            const uint DBGKEY = 0xA05F0000;
            const uint C_DEBUGEN = 1U << 0;

            WriteAp(ApCsw, CswSize32 | CswAddrinc_Off | CswDbgSwEnable);
            WriteAp(ApTar, DHCSR_ADDR);
            WriteAp(ApDrw, DBGKEY | C_DEBUGEN); // C_HALT=0 → resume
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
