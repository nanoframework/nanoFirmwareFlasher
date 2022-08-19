////
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using nanoFramework.Tools.FirmwareFlasher;

namespace ListJLinkDevicesApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var connecteDevices = JLinkDevice.ListDevices();

            if (connecteDevices.Count == 0)
            {
                Console.WriteLine("No J-Link devices found");
            }
            else
            {
                Console.WriteLine("-- Connected USB J-Link devices --");

                foreach (string deviceId in connecteDevices)
                {
                    Console.WriteLine(deviceId);
                }

                Console.WriteLine("----------------------------------");
            }

            Console.Read();
        }
    }
}
