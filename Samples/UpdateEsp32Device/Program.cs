////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.FirmwareFlasher;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpdateEsp32Device
{
    internal class Program
    {
        // COM port where the ESP32 device is connected
        // replace with the appropriate COM port number for the device you have connected
        private const string ComPort = "COM10";

        static async Task Main(string[] args)
        {
            EspTool espTool;

            // setup esptool
            try
            {
                espTool = new EspTool(
                    ComPort,
                    1500000,
                    "dio",
                    40,
                    null,
                    VerbosityLevel.Diagnostic);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"Exception occurred when invoking esptool:\n{ex.Message}");
                return;
            }

            Esp32DeviceInfo esp32Device;

            // get details from ESP32 device
            esp32Device = espTool.GetDeviceDetails(null, false);

            // output details of the connected device
            Console.WriteLine("");
            Console.WriteLine($"Connected to:");
            Console.WriteLine($"{esp32Device}");
            Console.WriteLine("");

            // perform firmware update
            var exitCode = await Esp32Operations.UpdateFirmwareAsync(
                            espTool,
                            esp32Device,
                            null,
                            true,
                            null,
                            false,
                            null,
                            null,
                            null,
                            false,
                            VerbosityLevel.Quiet,
                            null);

            if (exitCode != ExitCodes.OK)
            {
                Console.WriteLine($"Failed to update ESP32 firmware: {exitCode}");
                return;
            }

            Console.WriteLine($"ESP32 device {esp32Device.ChipName} flashed successfully!");

            Console.Read();
        }
    }
}
