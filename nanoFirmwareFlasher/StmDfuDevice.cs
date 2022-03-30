//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class StmDfuDevice : StmDeviceBase
    {
        // Device ID of the connected DFU device.
        private string _deviceId;

        /// <summary>
        /// Name of the connected device.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// CPU of the connected device.
        /// </summary>
        public string DeviceCPU { get; }


        /// <summary>
        /// ID of the connected DFU device.
        /// </summary>
        public string DfuId { get; }

        /// <summary>
        /// This property is <see langword="true"/> if a DFU device is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(DfuId);

        /// <summary>
        /// Creates a new <see cref="StmDfuDevice"/>. If a DFU device ID is provided it will try to connect to that device.
        /// </summary>
        /// <param name="dfuId">ID of the device to connect to.</param>
        public StmDfuDevice(string dfuId = null)
        {
            if (string.IsNullOrEmpty(dfuId))
            {
                // no DFU id supplied, list available
                var jtagDevices = ListDevices();

                if (jtagDevices.Count > 0)
                {
                    // take the 1st one
                    DfuId = jtagDevices[0].serial;

                    _deviceId = jtagDevices[0].device;
                }
                else
                {
                    // no DFU devices found
                    throw new CantConnectToDfuDeviceException();
                }
            }
            else
            {
                // DFU id was supplied

                // list available to find out the device ID
                var jtagDevices = ListDevices();

                // sanity check
                if (jtagDevices.Any())
                {
                    // find the one we're looking for
                    var dfuDevice = jtagDevices.FirstOrDefault(d => d.serial == dfuId);

                    if (dfuDevice == default)
                    {
                        // couldn't find the requested DFU device
                        throw new CantConnectToDfuDeviceException();
                    }
                    else
                    {
                        // found it!
                        DfuId = dfuId;
                        _deviceId = dfuDevice.device;
                    }
                }
                else
                {
                    // no DFU devices found
                    throw new CantConnectToDfuDeviceException();
                }
            }

            // try to connect to JTAG ID device to check availability
            // connect to device with RESET
            var cliOutput = RunSTM32ProgrammerCLI($"-c port={_deviceId}");

            if (cliOutput.Contains("Error"))
            {
                Console.WriteLine("");

                ShowCLIOutput(cliOutput);

                throw new CantConnectToDfuDeviceException();
            }

            // parse the output to fill in the details
            var match = Regex.Match(cliOutput, $"(Device name :)(?<devicename>.*)(.*?[\r\n]*)*(Device CPU  :)(?<devicecpu>.*)");
            if (match.Success)
            {
                // grab details
                DeviceName = match.Groups["devicename"].ToString().Trim();
                DeviceCPU = match.Groups["devicecpu"].ToString().Trim();
            }
        }

        /// <summary>
        /// Flash the HEX supplied to the connected device.
        /// </summary>
        /// <param name="files"></param>
        public ExitCodes FlashHexFiles(IList<string> files)
        {
            return ExecuteFlashHexFiles(
                files,
                $"port={_deviceId}");
        }

        /// <summary>
        /// Flash the BIN supplied to the connected device.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="addresses"></param>
        public ExitCodes FlashBinFiles(
            IList<string> files,
            IList<string> addresses)
        {
            return ExecuteFlashBinFiles(
                files,
                addresses,
                $"port={_deviceId}");
        }

        /// <summary>
        /// Start execution on connected device.
        /// </summary>
        public ExitCodes StartExecution(string startAddress)
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Starting execution on device...");
            }

            // connect to device and perform command
            var cliOutput = RunSTM32ProgrammerCLI($"-c port={_deviceId} --start 0x{startAddress}");

            if (cliOutput.Contains("Error"))
            {
                Console.WriteLine("");

                ShowCLIOutput(cliOutput);

                return ExitCodes.E1005;
            }

            if (!cliOutput.Contains("Start operation achieved successfully"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR");
                Console.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E1006;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("");
            }

            Console.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        /// <summary>
        /// List connected STM32 DFU devices.
        /// </summary>
        /// <returns>A collection of connected STM DFU devices.</returns>
        public static List<(string serial, string device)> ListDevices()
        {
            var cliOutput = ExecuteListDevices();

            // (successful) output from the above is
            //===== DFU Interface =====
            //
            //Total number of available STM32 device in DFU mode: 1
            //
            //  Device Index           : USB1
            //  USB Bus Number         : 001
            //  USB Address Number: 003
            //  Product ID             : STM32 BOOTLOADER
            //  Serial number          : 358438593337
            //  Firmware version       : 0x011a
            //  Device ID              : 0x0431

            // set pattern to serial number
            const string regexPattern = @"(?>Device Index           : )(?<device>\w+)(.*?[\r\n]*)*(?>Serial number          : )(?<serial>\d+)";

            var myRegex1 = new Regex(regexPattern, RegexOptions.Multiline);
            var dfuMatches = myRegex1.Matches(cliOutput);

            if (dfuMatches.Count == 0)
            {
                // no DFU device found
                return new List<(string serial, string device)>();
            }

            return dfuMatches.Cast<Match>().Select(i => (serial: i.Groups["serial"].Value, device: i.Groups["device"].Value)).ToList();
        }

        /// <summary>
        /// Perform mass erase on the connected device.
        /// </summary>
        public ExitCodes MassErase()
        {
            return ExecuteMassErase($"port={_deviceId}");
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder deviceInfo = new();

            if (!string.IsNullOrEmpty(DeviceName))
            {
                deviceInfo.AppendLine($"Device: { DeviceName }");
            }

            deviceInfo.AppendLine($"CPU: { DeviceCPU }");

            return deviceInfo.ToString();
        }
    }
}
