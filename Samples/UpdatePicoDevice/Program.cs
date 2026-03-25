////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.FirmwareFlasher;
using System;
using System.Threading.Tasks;

namespace UpdatePicoDevice
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // ===== Method 1: UF2 Mass Storage (default, simplest) =====
            Console.WriteLine("=== UF2 Mass Storage Update ===");
            Console.WriteLine("Looking for Raspberry Pi Pico in BOOTSEL mode...");
            Console.WriteLine("(Hold the BOOTSEL button while connecting USB)");
            Console.WriteLine();

            string drivePath = PicoUf2Utility.FindUf2Drive();

            if (drivePath == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Waiting for Pico device...");
                Console.ResetColor();

                drivePath = PicoUf2Utility.WaitForDrive(30_000, VerbosityLevel.Normal);

                if (drivePath == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("No Pico device found. Please connect a device in BOOTSEL mode.");
                    Console.ResetColor();
                    return;
                }
            }

            // detect device info
            PicoDeviceInfo deviceInfo = PicoUf2Utility.DetectDevice(drivePath);

            if (deviceInfo == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to detect device info.");
                Console.ResetColor();
                return;
            }

            // output details of the connected device
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Connected to:");
            Console.WriteLine($"{deviceInfo}");
            Console.ResetColor();
            Console.WriteLine();

            // perform firmware update via UF2
            var exitCode = await PicoOperations.UpdateFirmwareAsync(
                deviceInfo,
                null,     // auto-detect target name
                true,
                null,     // latest version
                false,    // stable release
                null,     // no archive
                null,     // no custom CLR file
                VerbosityLevel.Normal);

            if (exitCode != ExitCodes.OK)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to update Pico firmware: {exitCode}");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Pico device ({deviceInfo.ChipType}) flashed successfully!");
            Console.ResetColor();

            Console.Read();
        }
    }
}
