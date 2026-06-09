//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// STM32 flash programming via MEM-AP.
    /// Supports flash unlock, erase, and programming for major STM32 families.
    /// </summary>
    internal class Stm32FlashProgrammer
    {
        #region Flash unlock keys

        private const uint FlashKey1 = 0x45670123;
        private const uint FlashKey2 = 0xCDEF89AB;

        // Option byte unlock keys (same across families)
        private const uint OptKey1 = 0x08192A3B;
        private const uint OptKey2 = 0x4C5D6E7F;

        #endregion

        #region STM32 Family definitions

        internal enum Stm32Family
        {
            Unknown,
            F0,
            F1,
            F4,
            F7,
            H7,
            L0,
            L4,
            L5,
            G0,
            G4,
            WB,
            WL,
            U5,
            C0,
            H5,
        }

        private struct FlashRegisters
        {
            internal uint FlashBase;
            internal uint KeyrOffset;
            internal uint CrOffset;
            internal uint SrOffset;

            // CR bits
            internal uint PgBit;   // programming enable
            internal uint SerBit;  // sector erase (F4/F7) or page erase (L4/G0)
            internal uint MerBit;  // mass erase
            internal uint StrtBit; // start
            internal uint LockBit; // lock

            // SR bits
            internal uint BsyBit;  // busy
            internal uint EopBit;  // end of programming

            // Sector number shift in CR (F4/F7 store sector in bits [6:3])
            internal int SectorShift;

            // Page size for page-erase families
            internal uint PageSize;
        }

        // DBGMCU IDCODE register addresses by core type
        private const uint DbgmcuIdcode_M3M4 = 0xE0042000;  // Cortex-M3/M4/M7
        private const uint DbgmcuIdcode_M0 = 0x40015800;    // Cortex-M0/M0+
        private const uint DbgmcuIdcode_M33 = 0x44024000;   // Cortex-M33 (H5, U5)

        #endregion

        private readonly ArmMemAp _mem;
        private FlashRegisters _regs;
        private Stm32Family _family = Stm32Family.Unknown;

        internal Stm32FlashProgrammer(ArmMemAp mem)
        {
            _mem = mem ?? throw new ArgumentNullException(nameof(mem));
        }

        /// <summary>
        /// Gets the detected STM32 family.
        /// </summary>
        internal Stm32Family DetectedFamily => _family;

        /// <summary>
        /// Gets the chip IDCODE.
        /// </summary>
        internal uint ChipIdcode { get; private set; }

        /// <summary>
        /// Detects the STM32 family from the DBGMCU IDCODE register.
        /// </summary>
        internal Stm32Family DetectFamily()
        {
            // Try M3/M4/M7 address first
            uint idcode = _mem.ReadWord(DbgmcuIdcode_M3M4);

            if (idcode == 0 || idcode == 0xFFFFFFFF)
            {
                // Try M0/M0+ address
                idcode = _mem.ReadWord(DbgmcuIdcode_M0);
            }

            if (idcode == 0 || idcode == 0xFFFFFFFF)
            {
                // Try M33 address (H5, U5)
                idcode = _mem.ReadWord(DbgmcuIdcode_M33);
            }

            ChipIdcode = idcode;
            ushort devId = (ushort)(idcode & 0xFFF);

            _family = ClassifyDevice(devId);
            _regs = GetFlashRegisters(_family);

            return _family;
        }

        /// <summary>
        /// Unlocks the flash for programming.
        /// </summary>
        internal void UnlockFlash()
        {
            EnsureFamilyDetected();

            // Check if already unlocked (LOCK bit = 0 in CR)
            uint cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);

            if ((cr & _regs.LockBit) != 0)
            {
                // Write KEY1 then KEY2 to KEYR
                _mem.WriteWord(_regs.FlashBase + _regs.KeyrOffset, FlashKey1);
                _mem.WriteWord(_regs.FlashBase + _regs.KeyrOffset, FlashKey2);

                // Verify unlock
                cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);

                if ((cr & _regs.LockBit) != 0)
                {
                    throw new SwdProtocolException("Failed to unlock STM32 flash.");
                }
            }
        }

        /// <summary>
        /// Locks the flash after programming.
        /// </summary>
        internal void LockFlash()
        {
            EnsureFamilyDetected();

            uint cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            cr |= _regs.LockBit;
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
        }

        /// <summary>
        /// Performs a mass erase of the entire flash.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        internal void MassErase(int timeoutMs = 30000)
        {
            EnsureFamilyDetected();
            UnlockFlash();

            try
            {
                // Clear any pending errors
                ClearFlashErrors();

                // Set MER bit and start
                uint cr = _regs.MerBit | _regs.StrtBit;
                _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);

                // Wait for completion
                WaitForFlashReady(timeoutMs);

                // Clear MER bit
                cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
                cr &= ~_regs.MerBit;
                _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
            }
            finally
            {
                LockFlash();
            }
        }

        /// <summary>
        /// Erases flash pages/sectors covering the given address range.
        /// </summary>
        /// <param name="startAddress">Start address of the range to erase.</param>
        /// <param name="length">Number of bytes to erase.</param>
        /// <param name="timeoutMs">Timeout per page/sector in milliseconds.</param>
        internal void EraseRange(uint startAddress, int length, int timeoutMs = 5000)
        {
            EnsureFamilyDetected();
            UnlockFlash();

            try
            {
                ClearFlashErrors();

                if (IsSectorEraseFamily())
                {
                    EraseSectors(startAddress, length, timeoutMs);
                }
                else
                {
                    ErasePages(startAddress, length, timeoutMs);
                }
            }
            finally
            {
                LockFlash();
            }
        }

        /// <summary>
        /// Programs data to flash. The target range must already be erased.
        /// </summary>
        /// <param name="address">Flash start address (must be word-aligned).</param>
        /// <param name="data">Data bytes to program.</param>
        /// <param name="dataOffset">Offset into data array.</param>
        /// <param name="length">Number of bytes to program.</param>
        internal void Program(uint address, byte[] data, int dataOffset, int length)
        {
            EnsureFamilyDetected();
            UnlockFlash();

            try
            {
                ClearFlashErrors();

                if (_family == Stm32Family.H7)
                {
                    // H7 requires 256-bit (32-byte) programming
                    ProgramH7(address, data, dataOffset, length);
                }
                else if (_family == Stm32Family.L4 || _family == Stm32Family.G0 ||
                         _family == Stm32Family.G4 || _family == Stm32Family.WB ||
                         _family == Stm32Family.WL || _family == Stm32Family.U5 ||
                         _family == Stm32Family.L5 || _family == Stm32Family.C0 ||
                         _family == Stm32Family.H5)
                {
                    // L4/G0/G4/WB/WL/U5/L5/C0/H5 use double-word (64-bit) programming
                    ProgramDoubleWord(address, data, dataOffset, length);
                }
                else
                {
                    // F0/F1/F4/F7/L0 use word (32-bit) or half-word (16-bit) programming
                    ProgramWord(address, data, dataOffset, length);
                }
            }
            finally
            {
                LockFlash();
            }
        }

        /// <summary>
        /// Programs data and auto-erases the necessary pages/sectors first.
        /// </summary>
        internal void EraseAndProgram(uint address, byte[] data, int dataOffset, int length)
        {
            EraseRange(address, length);
            Program(address, data, dataOffset, length);
        }

        /// <summary>
        /// Reads back flash contents and compares them to the expected data.
        /// </summary>
        /// <param name="address">Flash start address.</param>
        /// <param name="data">Source data buffer.</param>
        /// <param name="dataOffset">Offset into data array.</param>
        /// <param name="length">Number of bytes to verify.</param>
        /// <returns><c>true</c> if read-back data matches; <c>false</c> otherwise.</returns>
        internal bool Verify(uint address, byte[] data, int dataOffset, int length)
        {
            byte[] readBack = _mem.ReadBytes(address, length);

            for (int i = 0; i < length; i++)
            {
                if (readBack[i] != data[dataOffset + i])
                {
                    return false;
                }
            }

            return true;
        }

        #region Private implementation

        private void EnsureFamilyDetected()
        {
            if (_family == Stm32Family.Unknown)
            {
                throw new SwdProtocolException(
                    "STM32 family not detected. Call DetectFamily() first.");
            }
        }

        private bool IsSectorEraseFamily()
        {
            return _family == Stm32Family.F4 || _family == Stm32Family.F7
                || _family == Stm32Family.H7 || _family == Stm32Family.H5;
        }

        private void WaitForFlashReady(int timeoutMs)
        {
            if (!_mem.PollRegister(
                    _regs.FlashBase + _regs.SrOffset,
                    _regs.BsyBit,
                    0, // wait for BSY=0
                    timeoutMs))
            {
                throw new SwdProtocolException(
                    "Flash operation timeout. BSY bit did not clear.");
            }
        }

        private void ClearFlashErrors()
        {
            uint sr = _mem.ReadWord(_regs.FlashBase + _regs.SrOffset);

            // Write 1 to clear error bits (write-1-to-clear pattern)
            if (sr != 0)
            {
                _mem.WriteWord(_regs.FlashBase + _regs.SrOffset, sr);
            }
        }

        private void EraseSectors(uint startAddress, int length, int timeoutMs)
        {
            // Sector-based erase for F4/F7/H7/H5 families
            const uint flashBase = 0x08000000;
            uint endAddress = startAddress + (uint)length;

            if (_family == Stm32Family.H7)
            {
                // H7: uniform 128KB sectors
                int startSector = (int)((startAddress - flashBase) / 0x20000);
                int endSector = (int)((endAddress - flashBase - 1) / 0x20000);

                for (int sector = startSector; sector <= endSector; sector++)
                {
                    uint cr = _regs.SerBit | ((uint)sector << _regs.SectorShift) | _regs.StrtBit;
                    cr |= (3U << 4); // PSIZE = 64-bit for H7
                    _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
                    WaitForFlashReady(timeoutMs);
                }
            }
            else if (_family == Stm32Family.H5)
            {
                // H5: uniform 8KB sectors
                int startSector = (int)((startAddress - flashBase) / 0x2000);
                int endSector = (int)((endAddress - flashBase - 1) / 0x2000);

                for (int sector = startSector; sector <= endSector; sector++)
                {
                    uint cr = _regs.SerBit | ((uint)sector << _regs.SectorShift) | _regs.StrtBit;
                    _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
                    WaitForFlashReady(timeoutMs);
                }
            }
            else
            {
                // F4/F7: variable sector sizes
                // Sectors 0-3: 16KB each (0x4000)
                // Sector 4: 64KB (0x10000)
                // Sectors 5+: 128KB each (0x20000)
                int startSector = GetSectorForAddress(startAddress - flashBase);
                int endSector = GetSectorForAddress(endAddress - flashBase - 1);

                for (int sector = startSector; sector <= endSector; sector++)
                {
                    uint cr = _regs.SerBit | ((uint)sector << _regs.SectorShift) | _regs.StrtBit;
                    cr |= (2U << 8); // PSIZE = x32
                    _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
                    WaitForFlashReady(timeoutMs);
                }
            }

            // Clear SER bit
            uint crVal = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            crVal &= ~_regs.SerBit;
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, crVal);
        }

        private void ErasePages(uint startAddress, int length, int timeoutMs)
        {
            uint pageSize = _regs.PageSize;

            if (pageSize == 0)
            {
                pageSize = 2048; // default page size
            }

            uint endAddress = startAddress + (uint)length;
            uint pageAddress = startAddress & ~(pageSize - 1); // align to page boundary

            while (pageAddress < endAddress)
            {
                // Set PER bit and page address
                if (_family == Stm32Family.L4 || _family == Stm32Family.G0 ||
                    _family == Stm32Family.G4 || _family == Stm32Family.WB ||
                    _family == Stm32Family.WL || _family == Stm32Family.U5)
                {
                    // L4-style: set PER bit, page number in PNB field [10:3], then STRT
                    uint pageNumber = (pageAddress - 0x08000000) / pageSize;
                    uint cr = _regs.SerBit | (pageNumber << 3) | _regs.StrtBit;
                    _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
                }
                else
                {
                    // F0/F1/L0 style: set PER bit, write page address to AR register, then STRT
                    uint arOffset = _regs.CrOffset + 4; // AR is typically at CR + 4
                    _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, _regs.SerBit);
                    _mem.WriteWord(_regs.FlashBase + arOffset, pageAddress);

                    uint cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
                    cr |= _regs.StrtBit;
                    _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
                }

                WaitForFlashReady(timeoutMs);
                pageAddress += pageSize;
            }

            // Clear PER bit
            uint crVal = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            crVal &= ~_regs.SerBit;
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, crVal);
        }

        private void ProgramWord(uint address, byte[] data, int dataOffset, int length)
        {
            // Set PG bit
            uint cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            cr |= _regs.PgBit;

            // For F4/F7, set PSIZE = x32
            if (_family == Stm32Family.F4 || _family == Stm32Family.F7)
            {
                cr |= (2U << 8);
            }

            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);

            // Write data word by word
            int pos = 0;

            while (pos < length)
            {
                uint word = 0xFFFFFFFF;
                int remaining = length - pos;

                for (int b = 0; b < 4 && b < remaining; b++)
                {
                    word &= ~(0xFFU << (b * 8));
                    word |= (uint)data[dataOffset + pos + b] << (b * 8);
                }

                _mem.WriteWord(address + (uint)pos, word);
                WaitForFlashReady(1000);
                pos += 4;
            }

            // Clear PG bit
            cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            cr &= ~_regs.PgBit;
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
        }

        private void ProgramDoubleWord(uint address, byte[] data, int dataOffset, int length)
        {
            // Set PG bit
            uint cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            cr |= _regs.PgBit;
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);

            int pos = 0;

            while (pos < length)
            {
                // Write two 32-bit words (one 64-bit double word)
                uint word0 = 0xFFFFFFFF;
                uint word1 = 0xFFFFFFFF;
                int remaining = length - pos;

                for (int b = 0; b < 4 && b < remaining; b++)
                {
                    word0 &= ~(0xFFU << (b * 8));
                    word0 |= (uint)data[dataOffset + pos + b] << (b * 8);
                }

                for (int b = 0; b < 4 && (b + 4) < remaining; b++)
                {
                    word1 &= ~(0xFFU << (b * 8));
                    word1 |= (uint)data[dataOffset + pos + 4 + b] << (b * 8);
                }

                _mem.WriteWord(address + (uint)pos, word0);
                _mem.WriteWord(address + (uint)pos + 4, word1);

                WaitForFlashReady(1000);
                pos += 8;
            }

            // Clear PG bit
            cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            cr &= ~_regs.PgBit;
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
        }

        private void ProgramH7(uint address, byte[] data, int dataOffset, int length)
        {
            // H7 requires 256-bit (32-byte) flash word programming
            // Set PG bit (bit 1 for H7) with PSIZE = 11 (64-bit)
            uint cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            cr |= _regs.PgBit;
            cr |= (3U << 4); // PSIZE = 64-bit for H7
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);

            int pos = 0;

            while (pos < length)
            {
                // Write 8 x 32-bit words = 256 bits
                for (int w = 0; w < 8; w++)
                {
                    uint word = 0xFFFFFFFF;
                    int bytePos = pos + w * 4;

                    for (int b = 0; b < 4 && (bytePos + b - dataOffset) < length; b++)
                    {
                        int idx = dataOffset + bytePos + b;

                        if (idx < dataOffset + length)
                        {
                            word &= ~(0xFFU << (b * 8));
                            word |= (uint)data[idx] << (b * 8);
                        }
                    }

                    _mem.WriteWord(address + (uint)(pos + w * 4), word);
                }

                WaitForFlashReady(1000);
                pos += 32;
            }

            // Clear PG bit
            cr = _mem.ReadWord(_regs.FlashBase + _regs.CrOffset);
            cr &= ~_regs.PgBit;
            _mem.WriteWord(_regs.FlashBase + _regs.CrOffset, cr);
        }

        private static int GetSectorForAddress(uint offset)
        {
            if (offset < 0x10000) // first 64KB = sectors 0-3 (16KB each)
            {
                return (int)(offset / 0x4000);
            }

            if (offset < 0x20000) // sector 4 (64KB)
            {
                return 4;
            }

            // Sectors 5+ (128KB each)
            return 5 + (int)((offset - 0x20000) / 0x20000);
        }

        private static Stm32Family ClassifyDevice(ushort devId)
        {
            switch (devId)
            {
                // F0 family
                case 0x440: // STM32F030x8
                case 0x442: // STM32F030xC
                case 0x444: // STM32F03xx4/6
                case 0x445: // STM32F04x
                case 0x448: // STM32F07x
                    return Stm32Family.F0;

                // F1 family
                case 0x410: // STM32F10xxx Medium-density
                case 0x412: // STM32F10xxx Low-density
                case 0x414: // STM32F10xxx High-density
                case 0x418: // STM32F105xx/107xx Connectivity
                case 0x420: // STM32F100 Low/Med density Value Line
                case 0x428: // STM32F100 High density Value Line
                    return Stm32Family.F1;

                // F4 family
                case 0x411: // STM32F2xx
                case 0x413: // STM32F40x/41x
                case 0x419: // STM32F42x/43x
                case 0x421: // STM32F446
                case 0x423: // STM32F401xB/C
                case 0x431: // STM32F411
                case 0x433: // STM32F401xD/E
                case 0x434: // STM32F469/479
                case 0x441: // STM32F412
                case 0x458: // STM32F410
                case 0x463: // STM32F413/423
                    return Stm32Family.F4;

                // F7 family
                case 0x449: // STM32F74x/75x
                case 0x451: // STM32F76x/77x
                case 0x452: // STM32F72x/73x
                    return Stm32Family.F7;

                // H7 family
                case 0x450: // STM32H74x/75x
                case 0x480: // STM32H7A3/B3
                case 0x483: // STM32H72x/73x
                    return Stm32Family.H7;

                // L0 family
                case 0x417: // STM32L05x/06x
                case 0x425: // STM32L031/041
                case 0x447: // STM32L07x/08x
                case 0x457: // STM32L011/021
                    return Stm32Family.L0;

                // L4 family
                case 0x415: // STM32L47x/48x
                case 0x435: // STM32L43x/44x
                case 0x461: // STM32L496/4A6
                case 0x462: // STM32L45x/46x
                case 0x464: // STM32L41x/42x
                case 0x470: // STM32L4Rx/4Sx
                case 0x471: // STM32L4P5/Q5
                    return Stm32Family.L4;

                // G0 family
                case 0x456: // STM32G050/051/061
                case 0x460: // STM32G07x/08x
                case 0x466: // STM32G03x/04x
                case 0x467: // STM32G0B0/0B1/0C1
                    return Stm32Family.G0;

                // G4 family
                case 0x468: // STM32G431/441
                case 0x469: // STM32G47x/48x
                case 0x479: // STM32G491/4A1
                    return Stm32Family.G4;

                // WB family
                case 0x495: // STM32WB55
                case 0x496: // STM32WB50
                    return Stm32Family.WB;

                // WL family
                case 0x497: // STM32WLE5/WL55
                    return Stm32Family.WL;

                // L5 family
                case 0x472: // STM32L552/562
                    return Stm32Family.L5;

                // U5 family
                case 0x455: // STM32U575/585
                case 0x476: // STM32U5x5
                case 0x481: // STM32U535/545
                case 0x482: // STM32U595/5A5/5F9/5G9
                    return Stm32Family.U5;

                // C0 family
                case 0x443: // STM32C011/031
                case 0x453: // STM32C071
                    return Stm32Family.C0;

                // H5 family
                case 0x474: // STM32H503
                case 0x478: // STM32H562/563/573
                case 0x484: // STM32H523/533
                    return Stm32Family.H5;

                default:
                    return Stm32Family.Unknown;
            }
        }

        private static FlashRegisters GetFlashRegisters(Stm32Family family)
        {
            switch (family)
            {
                case Stm32Family.F0:
                case Stm32Family.F1:
                case Stm32Family.L0:
                    return new FlashRegisters
                    {
                        FlashBase = 0x40022000,
                        KeyrOffset = 0x04,
                        CrOffset = 0x10,
                        SrOffset = 0x0C,
                        PgBit = 1U << 0,
                        SerBit = 1U << 1,    // PER (page erase)
                        MerBit = 1U << 2,    // MER
                        StrtBit = 1U << 6,   // STRT
                        LockBit = 1U << 7,   // LOCK
                        BsyBit = 1U << 0,    // BSY
                        EopBit = 1U << 5,    // EOP
                        SectorShift = 0,
                        PageSize = family == Stm32Family.L0 ? 128U : 1024U,
                    };

                case Stm32Family.F4:
                case Stm32Family.F7:
                    return new FlashRegisters
                    {
                        FlashBase = 0x40023C00,
                        KeyrOffset = 0x04,
                        CrOffset = 0x10,
                        SrOffset = 0x0C,
                        PgBit = 1U << 0,     // PG
                        SerBit = 1U << 1,     // SER
                        MerBit = 1U << 2,     // MER
                        StrtBit = 1U << 16,   // STRT
                        LockBit = 1U << 31,   // LOCK
                        BsyBit = 1U << 16,    // BSY
                        EopBit = 1U << 0,     // EOP
                        SectorShift = 3,       // SNB bits [6:3]
                        PageSize = 0,          // sector-based
                    };

                case Stm32Family.H7:
                    return new FlashRegisters
                    {
                        FlashBase = 0x52002000,
                        KeyrOffset = 0x04,
                        CrOffset = 0x0C,
                        SrOffset = 0x10,
                        PgBit = 1U << 1,      // PG
                        SerBit = 1U << 2,      // SER
                        MerBit = 1U << 3,      // BER (bank erase)
                        StrtBit = 1U << 7,     // START
                        LockBit = 1U << 0,     // LOCK
                        BsyBit = 1U << 0,      // BSY (in SR)
                        EopBit = 1U << 16,     // EOP
                        SectorShift = 8,        // SNB bits [10:8]
                        PageSize = 0,           // 128KB sectors
                    };

                case Stm32Family.L4:
                case Stm32Family.G0:
                case Stm32Family.G4:
                case Stm32Family.WB:
                case Stm32Family.WL:
                case Stm32Family.L5:
                case Stm32Family.U5:
                case Stm32Family.C0:
                    return new FlashRegisters
                    {
                        FlashBase = 0x40022000,
                        KeyrOffset = 0x08,
                        CrOffset = 0x14,
                        SrOffset = 0x10,
                        PgBit = 1U << 0,      // PG
                        SerBit = 1U << 1,      // PER (page erase)
                        MerBit = 1U << 2,      // MER1
                        StrtBit = 1U << 16,    // STRT
                        LockBit = 1U << 31,    // LOCK
                        BsyBit = 1U << 16,     // BSY
                        EopBit = 1U << 0,      // EOP
                        SectorShift = 0,
                        PageSize = (family == Stm32Family.G0 || family == Stm32Family.C0) ? 2048U : 4096U,
                    };

                case Stm32Family.H5:
                    return new FlashRegisters
                    {
                        FlashBase = 0x40022000,
                        KeyrOffset = 0x04,
                        CrOffset = 0x10,
                        SrOffset = 0x20,
                        PgBit = 1U << 1,      // PG
                        SerBit = 1U << 2,      // SER (sector erase)
                        MerBit = 1U << 3,      // BER (bank erase)
                        StrtBit = 1U << 7,     // START
                        LockBit = 1U << 0,     // LOCK
                        BsyBit = 1U << 0,      // BSY (in SR)
                        EopBit = 1U << 16,     // EOP
                        SectorShift = 8,        // SNB bits
                        PageSize = 0,           // 8KB sectors
                    };

                default:
                    throw new SwdProtocolException(
                        "Unsupported STM32 family for flash programming.");
            }
        }

        #endregion
    }
}
