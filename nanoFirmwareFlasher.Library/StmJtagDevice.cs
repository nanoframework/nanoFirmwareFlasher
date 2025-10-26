// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// STM32 JTAG Device.
    /// </summary>
    public class StmJtagDevice : StmDeviceBase
    {
        /// <summary>
        /// This property is <see langword="true"/> if a JTAG device is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(JtagId);

        /// <summary>
        /// ID of the connected JTAG device.
        /// </summary>
        public string JtagId { get; }

        /// <summary>
        /// Name of the connected device.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// ID of the connected device.
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        /// CPU of the connected device.
        /// </summary>
        public string DeviceCPU { get; }

        /// <summary>
        /// Name of the connected deviceboard.
        /// </summary>
        /// <remarks>
        /// This may not be available if it's not an ST board.
        /// </remarks>
        public string BoardName { get; }

        /// <summary>
        /// Creates a new <see cref="StmJtagDevice"/>. If a JTAG device ID is provided it will try to connect to that device.
        /// </summary>
        public StmJtagDevice(string jtagId = null)
        {
            if (string.IsNullOrEmpty(jtagId))
            {
                // no JTAG id supplied, list available
                List<string> jtagDevices = ListDevices();

                if (jtagDevices.Count > 0)
                {
                    // take the 1st one
                    JtagId = jtagDevices[0];
                }
                else
                {
                    // no JTAG devices found
                    throw new CantConnectToJtagDeviceException();
                }
            }
            else
            {
                // JTAG id was supplied
                JtagId = jtagId;
            }

            // try to connect to JTAG ID device to check availability
            // connect to device with RESET
            string cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={JtagId} HOTPLUG");

            if (cliOutput.Contains("Error"))
            {
                OutputWriter.WriteLine("");

                ShowCLIOutput(cliOutput);

                throw new CantConnectToJtagDeviceException();
            }

            // parse the output to fill in the details
            Match match = Regex.Match(cliOutput, $"(Board       :)(?<board>.*)(.*?[\r\n]*)*(Device ID   :)(?<deviceid>.*)(.*?[\r\n]*)*(Device name :)(?<devicename>.*)(.*?[\r\n]*)*(Device CPU  :)(?<devicecpu>.*)");
            if (match.Success)
            {
                // grab details
                BoardName = match.Groups["board"].ToString().Trim();
                DeviceId = match.Groups["deviceid"].ToString().Trim();
                DeviceName = match.Groups["devicename"].ToString().Trim();
                DeviceCPU = match.Groups["devicecpu"].ToString().Trim();
            }
        }

        /// <summary>
        /// Flash the BIN supplied to the connected device.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="addresses"></param>
        public ExitCodes FlashBinFiles(IList<string> files, IList<string> addresses)
        {
            return ExecuteFlashBinFiles(
                files,
                addresses,
                $"port=SWD sn={JtagId}");
        }


        /// <summary>
        /// Flash the HEX supplied to the connected device.
        /// </summary>
        /// <param name="files"></param>
        public ExitCodes FlashHexFiles(IList<string> files)
        {
            return ExecuteFlashHexFiles(
                files,
                $"port=SWD sn={JtagId}");
        }

        /// <summary>
        /// Perform mass erase on the connected device.
        /// </summary>
        public ExitCodes MassErase()
        {
            return ExecuteMassErase($"port=SWD sn={JtagId}");
        }

        /// <summary>
        /// Reset MCU of connected JTAG device.
        /// </summary>
        public ExitCodes ResetMcu()
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Reset MCU on device...");
            }

            // try to connect to device with RESET
            string cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={JtagId} mode=UR -rst");

            if (cliOutput.Contains("Error"))
            {
                OutputWriter.WriteLine("");

                ShowCLIOutput(cliOutput);

                return ExitCodes.E5002;
            }

            if (!cliOutput.Contains("MCU Reset"))
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine("ERROR");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E5010;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine(" OK");
            }
            else
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("");
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        /// <summary>
        /// List connected STM32 JTAG devices.
        /// </summary>
        /// <returns>A collection of connected STM JTAG devices.</returns>
        public static List<string> ListDevices()
        {
            string cliOutput = ExecuteListDevices();

            // (successful) output from the above for JTAG devices is
            //
            //-------- Connected ST-LINK Probes List --------
            //
            //ST-Link Probe 0 :
            //   ST-LINK SN  : 066CFF535752877167012515
            //   ST-LINK FW  : V2J37M27
            //-----------------------------------------------


            // set pattern to serial number
            const string regexPattern = @"(?<=ST-LINK SN  :\s)(?<serial>.{24})";

            var myRegex1 = new Regex(regexPattern, RegexOptions.Multiline);
            MatchCollection jtagMatches = myRegex1.Matches(cliOutput);

            if (jtagMatches.Count == 0)
            {
                // no JTAG found
                return [];
            }

            return jtagMatches.Cast<Match>().Select(i => i.Value).ToList();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder deviceInfo = new();

            if (!string.IsNullOrEmpty(DeviceName))
            {
                deviceInfo.AppendLine($"Device: {DeviceName}");
            }

            if (!string.IsNullOrEmpty(BoardName))
            {
                deviceInfo.AppendLine($"Board: {BoardName}");
            }

            deviceInfo.AppendLine($"CPU: {DeviceCPU}");
            deviceInfo.AppendLine($"Device ID: {DeviceId}");

            return deviceInfo.ToString();
        }
    }
}
