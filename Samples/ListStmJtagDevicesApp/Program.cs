////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.FirmwareFlasher;
using System;

namespace ListJtagDevicesApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var connecteDevices = StmJtagDevice.ListDevices();

            if (connecteDevices.Count == 0)
            {
                Console.WriteLine("No JTAG devices found");
            }
            else
            {
                Console.WriteLine("-- Connected JTAG devices --");

                foreach (string deviceId in connecteDevices)
                {
                    Console.WriteLine(deviceId);
                }

                Console.WriteLine("---------------------------");
            }

            Console.Read();
        }
    }
}
