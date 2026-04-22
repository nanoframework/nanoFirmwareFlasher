// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Ports;
using System.Runtime.InteropServices;
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
    /// 
    /// Sequences match esptool.py's reset.py (ClassicReset, USBJTAGSerialReset).
    /// </summary>
    internal static class Esp32ResetSequence
    {
        /// <summary>Default reset delay (50ms) — time GPIO0 is held low before release.</summary>
        internal const int DefaultResetDelayMs = 50;

        /// <summary>Extra-long reset delay (550ms) for boards needing more settle time.</summary>
        internal const int ExtraResetDelayMs = 550;

        /// <summary>
        /// Perform the classic DTR/RTS reset sequence to enter bootloader mode.
        /// Used for boards with UART bridge (ESP32, ESP32-S2, and most dev kits).
        /// 
        /// Matches esptool.py's ClassicReset:
        ///   D0|R1|W0.1|D1|R0|W{resetDelay}|D0
        /// </summary>
        /// <param name="port">Serial port.</param>
        /// <param name="resetDelayMs">Delay in ms after EN release while GPIO0 is held low.
        /// esptool cycles between 50ms and 550ms.</param>
        internal static void EnterBootloader(SerialPort port, int resetDelayMs = DefaultResetDelayMs)
        {
            // D0: IO0=HIGH
            SetDtr(port, false);
            // R1: EN=LOW, chip in reset
            SetRts(port, true);
            Thread.Sleep(100);

            // D1: IO0=LOW (bootloader mode select)
            SetDtr(port, true);
            // R0: EN=HIGH, chip out of reset — samples GPIO0
            SetRts(port, false);
            Thread.Sleep(resetDelayMs);

            // D0: IO0=HIGH, done — release boot pin
            SetDtr(port, false);
        }

        /// <summary>
        /// Perform USB-JTAG/Serial reset sequence for chips with native USB
        /// (ESP32-S3, ESP32-C3, ESP32-C6, ESP32-H2, ESP32-P4).
        /// 
        /// Matches esptool.py's USBJTAGSerialReset exactly:
        ///   R0|D0|W0.1|D1|R0|W0.1|R1|D0|R1|W0.1|D0|R0
        /// 
        /// Key difference from ClassicReset: transitions through (DTR=1, RTS=1)
        /// state rather than (DTR=0, RTS=0), which the USB-JTAG hardware detects
        /// as a request to enter bootloader.
        /// </summary>
        internal static void EnterBootloaderUsbJtag(SerialPort port)
        {
            // Idle — both lines released
            SetRts(port, false);
            SetDtr(port, false);
            Thread.Sleep(100);

            // Set IO0 (DTR=1 → IO0 low)
            SetDtr(port, true);
            SetRts(port, false);
            Thread.Sleep(100);

            // Reset: assert RTS before releasing DTR → goes through (1,1) state.
            // "Calls inverted to go through (1,1) instead of (0,0)" — esptool comment
            SetRts(port, true);
            SetDtr(port, false);
            // Re-assert RTS: on Windows usbser.sys only propagates DTR on RTS change
            SetRts(port, true);
            Thread.Sleep(100);

            // Chip out of reset — release both
            SetDtr(port, false);
            SetRts(port, false);
        }

        /// <summary>
        /// Perform a hard reset via RTS toggle to run the application.
        /// This exits the bootloader and starts the user firmware.
        /// 
        /// Matches esptool.py's HardReset:
        ///   UART:     R1|W0.1|R0
        ///   USB-JTAG: R1|W0.2|R0|W0.2  (longer delays for USB reconnect)
        /// esptool does NOT set DTR during hard reset.
        /// </summary>
        /// <param name="port">Serial port.</param>
        /// <param name="usesUsb">True if connected via USB-JTAG/Serial (needs longer delays).</param>
        internal static void HardReset(SerialPort port, bool usesUsb = false)
        {
            SetRts(port, true);

            if (usesUsb)
            {
                // Give the chip some time to come out of reset,
                // to be able to handle further DTR/RTS transitions
                Thread.Sleep(200);
                SetRts(port, false);
                Thread.Sleep(200);
            }
            else
            {
                Thread.Sleep(100);
                SetRts(port, false);
            }
        }

        /// <summary>
        /// Set DTR with Windows usbser.sys workaround.
        /// On Windows, after setting DTR we re-read/set RTS to force the
        /// SET_CONTROL_LINE_STATE USB request to be sent with the updated DTR.
        /// Matches esptool.py's _setRTS → self.port.setDTR(self.port.dtr) pattern.
        /// </summary>
        private static void SetDtr(SerialPort port, bool state)
        {
            port.DtrEnable = state;
        }

        /// <summary>
        /// Set RTS with Windows usbser.sys workaround.
        /// On Windows, the usbser.sys CDC driver may not propagate DTR changes
        /// until RTS is also set. After every RTS change, re-set DTR to force
        /// the SET_CONTROL_LINE_STATE USB request.
        /// Matches esptool.py's _setRTS workaround.
        /// </summary>
        private static void SetRts(SerialPort port, bool state)
        {
            port.RtsEnable = state;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Work-around for usbser.sys: generate a dummy DTR change
                // so the set-control-line-state request is sent with the
                // updated RTS state and the same DTR state.
                port.DtrEnable = port.DtrEnable;
            }
        }
    }
}
