////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.FirmwareFlasher;
using System;
using System.Threading.Tasks;

namespace UpdateNanoDeviceClr
{
    internal class Program
    {
        // COM port where the nano device is connected
        // replace with the appropriate COM port number for the device you have connected
        private const string ComPort = "COM10";
        private static NanoDeviceOperations _nanoDeviceOperations;

        static async Task Main(string[] args)
        {
            _nanoDeviceOperations = new NanoDeviceOperations();

            try
            {
                await _nanoDeviceOperations.UpdateDeviceClrAsync(
                    ComPort,
                    VerbosityLevel.Normal);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred when updating nano device CLR: {ex.Message}");
            }

            Console.Read();
        }
    }
}
