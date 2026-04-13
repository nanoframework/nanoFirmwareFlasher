// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Ports;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// DTR/RTS reset sequences to enter the ESP32 ROM bootloader or hard-reset the chip.
    /// 
    /// On typical ESP32 boards the USB-to-UART bridge maps:
    ///   DTR → GPIO0 (active-low boot mode select)
    ///   RTS → EN/CHIP_PU (active-low chip enable / reset)
    /// 
    /// Note: SerialPort DTR/RTS true = line asserted = voltage LOW on the pin
    /// (active-low through transistor inverter on most dev boards).
    /// </summary>
    internal static class Esp32ResetSequence
    {
        /// <summary>
        /// Perform the classic DTR/RTS reset sequence to enter bootloader mode.
        /// Used for boards with UART bridge (ESP32, ESP32-S2, and most dev kits).
        /// 
        /// Sequence:
        ///   1. Assert RTS (EN low = chip reset), de-assert DTR (GPIO0 high)
        ///   2. Wait 100ms for reset to take effect
        ///   3. Assert DTR (GPIO0 low = boot mode), de-assert RTS (EN high = chip starts)
        ///   4. Wait 50ms for the chip to sample GPIO0
        ///   5. De-assert both (release control lines)
        /// </summary>
        internal static void EnterBootloader(SerialPort port)
        {
            // Step 1: Reset the chip (EN low), GPIO0 high (normal boot)
            port.DtrEnable = false;
            port.RtsEnable = true;
            Thread.Sleep(100);

            // Step 2: Release reset (EN high), pull GPIO0 low (bootloader mode)
            port.DtrEnable = true;
            port.RtsEnable = false;
            Thread.Sleep(50);

            // Step 3: Release both lines
            port.DtrEnable = false;
            port.RtsEnable = false;
        }

        /// <summary>
        /// Perform USB-JTAG/Serial reset sequence for chips with native USB
        /// (ESP32-S3, ESP32-C3, ESP32-C6, ESP32-H2, ESP32-P4).
        /// 
        /// This sequence is required for the built-in USB Serial/JTAG peripheral.
        /// It differs from the classic UART reset by the order of DTR/RTS transitions:
        /// it goes through the (1,1) state (both asserted) rather than (0,0),
        /// which is how the USB-JTAG hardware detects the reset-to-bootloader request.
        /// 
        /// Matches esptool.py's USBJTAGSerialReset exactly.
        /// </summary>
        internal static void EnterBootloaderUsbJtag(SerialPort port)
        {
            // Step 1: Idle — both lines released
            port.RtsEnable = false;
            port.DtrEnable = false;
            Thread.Sleep(100);

            // Step 2: Assert DTR (IO0 low) while keeping EN high — prepare for bootloader
            port.DtrEnable = true;
            port.RtsEnable = false;
            Thread.Sleep(100);

            // Step 3: Assert RTS (EN low = reset) BEFORE releasing DTR.
            // This transitions through (DTR=1, RTS=1) rather than (DTR=0, RTS=0),
            // which is the specific pattern the USB-JTAG hardware looks for.
            port.RtsEnable = true;    // EN low — chip resets (via DTR=1,RTS=1 state)
            port.DtrEnable = false;   // IO0 high — release GPIO0
            // Windows workaround: the usbser.sys CDC driver may not propagate
            // DTR changes until RTS is also set. Re-assert RTS to ensure the
            // SET_CONTROL_LINE_STATE USB request is sent with the updated DTR.
            port.RtsEnable = true;
            Thread.Sleep(100);

            // Step 4: Release both — chip comes out of reset into bootloader
            port.DtrEnable = false;
            port.RtsEnable = false;
        }

        /// <summary>
        /// Perform a hard reset via RTS toggle to run the application.
        /// This exits the bootloader and starts the user firmware.
        /// </summary>
        internal static void HardReset(SerialPort port)
        {
            // Pulse EN low briefly to reset the chip, with GPIO0 high (normal boot)
            port.DtrEnable = false;  // GPIO0 high → normal boot
            port.RtsEnable = true;   // EN low → reset
            Thread.Sleep(100);

            port.RtsEnable = false;  // EN high → chip starts
            Thread.Sleep(50);
        }
    }
}
