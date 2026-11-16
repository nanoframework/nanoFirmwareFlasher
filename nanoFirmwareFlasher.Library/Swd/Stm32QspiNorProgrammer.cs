// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    internal sealed class Stm32QspiNorProgrammer
    {
        private const uint Qspi = 0xA0001000;
        private const uint Cr = Qspi;
        private const uint Sr = Qspi + 8;
        private const uint Fcr = Qspi + 12;
        private const uint Dlr = Qspi + 16;
        private const uint Ccr = Qspi + 20;
        private const uint Ar = Qspi + 24;
        private const uint Dr = Qspi + 32;
        private const uint Busy = 1 << 5;
        private const int SectorSize = 4096;
        private const int PageSize = 256;
        private readonly Func<uint, uint> _read;
        private readonly Action<uint, uint> _write;
        private readonly Func<uint, int, byte[]> _readBytes;
        private bool _initialized;

        internal Stm32QspiNorProgrammer(ArmMemAp mem)
            : this(mem.ReadWord, mem.WriteWord, mem.ReadBytes)
        {
        }

        internal Stm32QspiNorProgrammer(
            Func<uint, uint> read,
            Action<uint, uint> write,
            Func<uint, int, byte[]> readBytes)
        {
            _read = read;
            _write = write;
            _readBytes = readBytes;
        }

        internal Action<string, int, int> ProgressReport { get; set; }

        internal uint JedecId { get; private set; }

        internal void Initialize(uint chipId)
        {
            // STM32F412 (RM0402 DBGMCU_IDCODE.DEV_ID).
            if ((chipId & 0xFFF) != 0x441)
            {
                throw new SwdProtocolException("MXCHIP external flash requires an STM32F412 target.");
            }

            Modify(0x40023830, 0, 7);
            Modify(0x40023838, 0, 2);
            _initialized = true;
            Modify(0x40023818, 0, 2);
            Modify(0x40023818, 2, 0);

            ConfigurePin(0x40020400, 1, 2, 9);
            ConfigurePin(0x40020800, 11, 2, 9, true);
            ConfigurePin(0x40020000, 6, 2, 10);
            ConfigurePin(0x40020000, 7, 2, 10);
            _write(0x40020818, (1 << 4) | (1 << 5));
            ConfigurePin(0x40020800, 4, 1, 0);
            ConfigurePin(0x40020800, 5, 1, 0);

            _write(Cr, 0);
            _write(Qspi + 4, 0x00140100);
            // Even the maximum supported F412 HCLK (100 MHz) yields only 6.25 MHz.
            _write(Cr, (15U << 24) | (1 << 7) | (1 << 4) | 1);
            _write(Fcr, 0x1B);
            JedecId = ReadCommand(0x9F) & 0xFFFFFF;
            if (JedecId == 0 || JedecId == 0xFFFFFF)
            {
                throw new SwdProtocolException($"No external NOR flash answered (JEDEC 0x{JedecId:X6}).");
            }

            // Whitelist verified 256-byte-page / 4-KB-sector command sets, not just capacity.
            if (JedecId != 0x1520C2 && JedecId != 0x1523C2 &&
                JedecId != 0x15301C && JedecId != 0x1430EF)
            {
                throw new SwdProtocolException(
                    $"Unsupported external NOR JEDEC 0x{JedecId:X6}; refusing to erase it.");
            }
        }

        internal bool Program(byte[] image)
        {
            Stm32WifiImage.Validate(Stm32WifiImage.Address, image);
            if (!_initialized)
            {
                throw new SwdProtocolException("External flash has not been initialized.");
            }

            byte[] header = ReadFlash(0, PageSize);
            if (Stm32WifiImage.HasValidHeader(header) &&
                Stm32WifiImage.ReadUInt32(header, 8) == Stm32WifiImage.ReadUInt32(image, 8) &&
                Stm32WifiImage.ReadUInt32(header, 12) == Stm32WifiImage.ReadUInt32(image, 12) &&
                Matches(image, PageSize))
            {
                ProgressReport?.Invoke("Wi-Fi image unchanged", image.Length, image.Length);
                return false;
            }

            for (int offset = 0; offset < image.Length; offset += SectorSize)
            {
                WriteEnable();
                Command(0x00002520, (uint)offset);
                WaitForNor(5000, $"erase at 0x{offset:X6}");
                ProgressReport?.Invoke("Erasing Wi-Fi", Math.Min(offset + SectorSize, image.Length), image.Length);
            }

            for (int offset = PageSize; offset < image.Length; offset += PageSize)
            {
                ProgramPage(image, offset, Math.Min(PageSize, image.Length - offset));
                ProgressReport?.Invoke("Programming Wi-Fi", offset + Math.Min(PageSize, image.Length - offset), image.Length);
            }

            if (!Matches(image, PageSize))
            {
                throw new SwdProtocolException("External Wi-Fi payload verification failed; header was not committed.");
            }

            ProgramPage(image, 0, PageSize);
            if (!Matches(image, 0))
            {
                throw new SwdProtocolException("External Wi-Fi image verification failed after header commit.");
            }

            ProgressReport?.Invoke("Verified Wi-Fi", image.Length, image.Length);
            return true;
        }

        internal void Cleanup()
        {
            if (!_initialized)
            {
                return;
            }

            bool failed = true;
            try
            {
                Abort();
                failed = false;
            }
            finally
            {
                try
                {
                    Modify(0x40023818, 0, 2);
                    Modify(0x40023818, 2, 0);
                    _write(Cr, 0);
                    _write(Fcr, 0x1B);
                    _initialized = false;
                }
                catch (SwdProtocolException ex) when (failed)
                {
                    OutputWriter.WriteLine($"ERROR: QUADSPI reset during cleanup failed: {ex.Message}");
                }
            }
        }

        private void ConfigurePin(uint gpio, int pin, uint mode, uint alternate, bool pullUp = false)
        {
            int shift = pin * 2;
            Modify(gpio + 4, 1U << pin, 0);
            Modify(gpio + 8, 3U << shift, 3U << shift);
            Modify(gpio + 12, 3U << shift, (pullUp ? 1U : 0U) << shift);
            if (mode == 2)
            {
                uint afr = gpio + (pin < 8 ? 32U : 36U);
                int afShift = (pin % 8) * 4;
                Modify(afr, 15U << afShift, alternate << afShift);
            }

            Modify(gpio, 3U << shift, mode << shift);
        }

        private void Modify(uint address, uint clear, uint set)
            => _write(address, (_read(address) & ~clear) | set);

        private void Wait(uint mask, uint expected, string operation, int timeoutMs = 2000)
        {
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                uint status = _read(Sr);
                if ((status & 1) != 0)
                {
                    throw new SwdProtocolException($"QUADSPI transfer error during {operation} (SR 0x{status:X8}).");
                }

                if ((status & mask) == expected)
                {
                    return;
                }

                Thread.Sleep(1);
            }
            while (timer.ElapsedMilliseconds < timeoutMs);

            throw new SwdProtocolException($"QUADSPI timeout during {operation}.");
        }

        private void Finish()
        {
            Wait(2, 2, "transfer completion");
            _write(Fcr, 2);
            Wait(Busy, 0, "idle");
        }

        private void Command(uint ccr, uint? address = null)
        {
            Wait(Busy, 0, "command");
            _write(Fcr, 0x1B);
            _write(Ccr, ccr);
            if (address.HasValue)
            {
                _write(Ar, address.Value);
            }

            Finish();
        }

        private uint ReadCommand(byte opcode)
        {
            Wait(Busy, 0, "status/ID read");
            _write(Fcr, 0x1B);
            _write(Dlr, 3);
            _write(Ccr, 0x05000100U | opcode);
            Wait(0x3F00, 4 << 8, "status/ID FIFO");
            uint result = _read(Dr);
            Finish();
            return result;
        }

        private void WriteEnable()
        {
            Command(0x106);
            if ((ReadCommand(5) & 2) == 0)
            {
                throw new SwdProtocolException("External flash refused write enable (possibly write protected).");
            }
        }

        private void WaitForNor(int timeoutMs, string operation)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while ((ReadCommand(5) & 1) != 0)
            {
                if (timer.ElapsedMilliseconds >= timeoutMs)
                {
                    throw new SwdProtocolException($"External NOR timeout during {operation}.");
                }

                Thread.Sleep(1);
            }
        }

        private void ProgramPage(byte[] image, int offset, int length)
        {
            WriteEnable();
            Wait(Busy, 0, "page program");
            _write(Fcr, 0x1B);
            int paddedLength = (length + 3) & ~3;
            _write(Dlr, (uint)paddedLength - 1);
            _write(Ccr, 0x01002502);
            _write(Ar, (uint)offset);
            for (int i = 0; i < paddedLength; i += 4)
            {
                // FTF with FTHRES=0 indicates at least one free byte. Require four slots.
                Stopwatch timer = Stopwatch.StartNew();
                while (true)
                {
                    uint status = _read(Sr);
                    if ((status & 1) != 0)
                    {
                        throw new SwdProtocolException($"QUADSPI transfer error at 0x{offset + i:X6}.");
                    }

                    if (((status >> 8) & 0x3F) <= 28)
                    {
                        break;
                    }

                    if (timer.ElapsedMilliseconds >= 2000)
                    {
                        throw new SwdProtocolException($"QUADSPI FIFO timeout at 0x{offset + i:X6}.");
                    }
                }

                uint word = 0xFFFFFFFF;
                for (int b = 0; b < 4 && i + b < length; b++)
                {
                    word = (word & ~(0xFFU << (8 * b))) | (uint)image[offset + i + b] << (8 * b);
                }

                _write(Dr, word);
            }

            Finish();
            WaitForNor(1000, $"program at 0x{offset:X6}");
        }

        private void Abort()
        {
            Modify(Cr, 0, 2);
            Wait(Busy, 0, "abort");
            _write(Fcr, 0x1B);
        }

        private byte[] ReadFlash(int offset, int length)
        {
            Wait(Busy, 0, "memory-mapped read");
            _write(Ccr, 0x0D002503);
            bool failed = true;
            try
            {
                byte[] result = _readBytes(Stm32WifiImage.Address + (uint)offset, length);
                failed = false;
                return result;
            }
            finally
            {
                try
                {
                    Abort();
                }
                catch (SwdProtocolException ex) when (failed)
                {
                    OutputWriter.WriteLine($"ERROR: QUADSPI abort after read failure failed: {ex.Message}");
                }
            }
        }

        private bool Matches(byte[] image, int start)
        {
            for (int offset = start; offset < image.Length; offset += 1024)
            {
                int count = Math.Min(1024, image.Length - offset);
                byte[] actual = ReadFlash(offset, count);
                for (int i = 0; i < count; i++)
                {
                    if (actual[i] != image[offset + i])
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
