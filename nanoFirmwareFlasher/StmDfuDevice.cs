//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher
{
    // STSW-ST7009
    // DFU Development Kit package (Device Firmware Upgrade).


    internal class StmDfuDevice
    {
        /// <summary>
        /// GUID of interface class declared by ST DFU devices
        /// </summary>
        private static Guid s_dfuGuid = new Guid("3FE809AB-FB91-4CB5-A643-69670D52366E");

        private readonly string _deviceId;

        /// <summary>
        /// Property with option for performing mass erase on the connected device.
        /// If <see langword="false"/> only the flash sectors that will programmed are erased.
        /// </summary>
        public bool DoMassErase { get; set; } = false;

        /// <summary>
        /// This property is <see langword="true"/> if a DFU device is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(_deviceId);

        /// <summary>
        /// ID of the connected DFU device.
        /// </summary>
        // the split bellow is to get only the ID part of the USB ID
        // that follows the pattern: USB\\VID_0483&PID_DF11\\3380386D3134
        public string DeviceId => _deviceId?.Split('\\', ' ')[2];

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; internal set; } = VerbosityLevel.Normal;

        /// <summary>
        /// Creates a new <see cref="StmDfuDevice"/>. If a DFU device ID is provided it will try to connect to that device.
        /// </summary>
        /// <param name="deviceId">ID of the device to connect to.</param>
        public StmDfuDevice(string deviceId = null)
        {
            ManagementObjectCollection usbDevicesCollection;

            // build a managed object searcher to find USB devices with the ST DFU VID & PID along with the device description
            using (var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_PnPEntity WHERE DeviceID Like ""USB\\VID_0483&PID_DF11%"" AND Description Like ""STM Device in DFU Mode"" "))
                usbDevicesCollection = searcher.Get();

            // are we to connect to a specific device?
            if (deviceId == null)
            {
                // no, grab the USB device ID of the 1st listed device from the respective property
                deviceId = usbDevicesCollection.OfType<ManagementObject>().Select(mo => mo.Properties["DeviceID"].Value as string).FirstOrDefault();
            }
            else
            {
                // yes, filter the connect devices collection with the requested device ID
                deviceId = usbDevicesCollection.OfType<ManagementObject>().Select(mo => mo.Properties["DeviceID"].Value as string).FirstOrDefault(d => d.Contains(deviceId));
            }

            // sanity check for no device found
            if (deviceId == null)
            {
                // couldn't find any DFU device
                return;
            }

            // ST DFU is expecting a device path with the WQL pattern:
            // "\\?\USB#VID_0483&PID_DF11#3380386D3134#{3FE809AB-FB91-4CB5-A643-69670D52366E}"
            // The GUID there is for the USB interface declared by DFU devices

            // store USB device ID
            _deviceId = @"\\?\" + deviceId.Replace(@"\", "#") + @"#{" + s_dfuGuid.ToString() + "}";
        }

        /// <summary>
        /// Flash the DFU supplied to the connected device.
        /// </summary>
        /// <param name="filePath"></param>
        public void FlashDfuFile(string filePath)
        {
            // check DFU file existence
            if (!File.Exists(filePath))
            {
                throw new DfuFileDoesNotExistException();
            }

            // erase flash
            if (DoMassErase)
            {
                Console.WriteLine("WARNING: mass erase is not currently supported for DFU devices.");
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.Write("Flashing device...");
            }

            // write flash, verify, reboot
            // DFU is picky about the device path: requires it to be lower caps
            if (!RunDfuCommandTool($" -d \"{filePath}\" -D {_deviceId.ToLowerInvariant()} -v -r ", out string messages))
            {
                // something went wrong
                throw new DfuOperationFailedException();
            }

            // check for successfull message
            if (messages.Contains("Successfully left DFU mode !"))
            {
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }
            }
            else
            {
                // something went wrong
                throw new DfuOperationFailedException();
            }
        }

        /// <summary>
        /// Search connected DFU devices.
        /// </summary>
        /// <returns>A collection of connected DFU devices.</returns>
        public static List<string> ListDfuDevices()
        {
            ManagementObjectCollection usbDevicesCollection;

            // build a managed object searcher to find USB devices with the ST DFU VID & PID along with the device description
            using (var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_PnPEntity WHERE DeviceID Like ""USB\\VID_0483&PID_DF11%"" AND Description Like ""STM Device in DFU Mode"" "))
                usbDevicesCollection = searcher.Get();

            // the split bellow is to get only the ID part of the USB ID
            // that follows the pattern: USB\\VID_0483&PID_DF11\\3380386D3134
            return usbDevicesCollection.OfType<ManagementObject>().Select(mo => (mo.Properties["DeviceID"].Value as string).Split('\\', ' ')[2]).ToList();
        }

        /// <summary>
        /// Run the esptool one time
        /// </summary>
        /// <param name="commandWithArguments">the esptool command (e.g. write_flash) incl. all arguments (if needed)</param>
        /// <param name="messages">StandardOutput and StandardError messages that the esptool prints out</param>
        /// <returns>true if the esptool exit code was 0; false otherwise</returns>
        private bool RunDfuCommandTool(
            string commandWithArguments,
            out string messages)
        {
            // create the process start info

            // prepare the process start of the esptool
            Process dfuCommandTool = new Process();
            dfuCommandTool.StartInfo = new ProcessStartInfo(Path.Combine(Program.ExecutingPath, "stdfu", "qmk-dfuse.exe"), commandWithArguments)
            {
                WorkingDirectory = Path.Combine(Program.ExecutingPath, "stdfu"),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            // start esptool and wait for exit
            if (dfuCommandTool.Start())
            {
                // if no progress output needed wait unlimited time until comment exits
                if (Verbosity < VerbosityLevel.Detailed)
                {
                    dfuCommandTool.WaitForExit();
                }
            }
            else
            {
                throw new EspToolExecutionException("Error starting DfuSeCommand!");
            }

            StringBuilder messageBuilder = new StringBuilder();

            // showing progress is a little bit tricky
            if (Verbosity >= VerbosityLevel.Detailed)
            {
                // loop until esptool exit
                while (!dfuCommandTool.HasExited)
                {
                    // loop until there is no next char to read from standard output
                    while (true)
                    {
                        int next = dfuCommandTool.StandardOutput.Read();
                        if (next != -1)
                        {
                            // append the char to the message buffer
                            char nextChar = (char)next;
                            messageBuilder.Append((char)next);
                            // print progress and set the cursor to the beginning of the line (\r)
                            Console.Write(nextChar);
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // collect any last messages
                messageBuilder.AppendLine(dfuCommandTool.StandardOutput.ReadToEnd());
                messageBuilder.Append(dfuCommandTool.StandardError.ReadToEnd());
            }
            else
            {
                // collect all messages
                messageBuilder.AppendLine(dfuCommandTool.StandardOutput.ReadToEnd());
                messageBuilder.Append(dfuCommandTool.StandardError.ReadToEnd());
            }

            messages = messageBuilder.ToString();

            // true if exit code was 0 (success)
            return dfuCommandTool.ExitCode == 0;
        }
    }
}
