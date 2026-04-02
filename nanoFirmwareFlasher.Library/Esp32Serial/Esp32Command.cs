// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// ESP32 ROM bootloader command opcodes.
    /// See: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html
    /// </summary>
    internal enum Esp32Command : byte
    {
        /// <summary>Begin flash write operation.</summary>
        FlashBegin = 0x02,

        /// <summary>Write a block of data to flash.</summary>
        FlashData = 0x03,

        /// <summary>End flash write operation, optionally reboot.</summary>
        FlashEnd = 0x04,

        /// <summary>Begin memory write operation (for stub upload).</summary>
        MemBegin = 0x05,

        /// <summary>End memory write operation, execute entry point.</summary>
        MemEnd = 0x06,

        /// <summary>Write a block of data to memory.</summary>
        MemData = 0x07,

        /// <summary>Synchronize with bootloader.</summary>
        Sync = 0x08,

        /// <summary>Write a 32-bit hardware register.</summary>
        WriteReg = 0x09,

        /// <summary>Read a 32-bit hardware register.</summary>
        ReadReg = 0x0A,

        /// <summary>Set SPI flash parameters.</summary>
        SpiSetParams = 0x0B,

        /// <summary>Attach SPI flash.</summary>
        SpiAttach = 0x0D,

        /// <summary>Change UART baud rate.</summary>
        ChangeBaudrate = 0x0F,

        /// <summary>Begin compressed flash write (requires stub).</summary>
        FlashDeflBegin = 0x10,

        /// <summary>Write compressed data block to flash (requires stub).</summary>
        FlashDeflData = 0x11,

        /// <summary>End compressed flash write (requires stub).</summary>
        FlashDeflEnd = 0x12,

        /// <summary>Calculate MD5 hash of flash region (requires stub).</summary>
        SpiFlashMd5 = 0x13,

        /// <summary>Erase entire flash memory.</summary>
        EraseFlash = 0xD0,

        /// <summary>Erase specific flash region.</summary>
        EraseRegion = 0xD1,

        /// <summary>Read flash contents.</summary>
        ReadFlash = 0xD2,

        /// <summary>Run user code (soft reset).</summary>
        RunUserCode = 0xD3,
    }
}
