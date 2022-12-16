﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class representing a connected J-Link device.
    /// </summary>
    public class JLinkDevice : JLinkCli
    {
        /// <summary>
        /// This property is <see langword="true"/> if a J-Link device is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(ProbeId);

        /// <summary>
        /// ID of the connected J-Link device.
        /// </summary>
        public string ProbeId { get; }

        /// <summary>
        /// ID of the connected device.
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        /// CPU of the connected device.
        /// </summary>
        public string DeviceCPU { get; }

        /// <summary>
        /// Firmware details of the connected J-Link probe.
        /// </summary>
        public string Firmare { get; } = "N.A.";

        /// <summary>
        /// Hardware details of the connected J-Link probe.
        /// </summary>
        public string Hardware { get; } = "N.A.";

        /// <summary>
        /// Creates a new <see cref="JLinkDevice"/>.
        /// </summary>
        /// <remarks>
        /// If a J-Link device ID is provided it will try to connect to that device.
        /// </remarks>
        public JLinkDevice(string probeId = null)
        {
            if (string.IsNullOrEmpty(probeId))
            {
                // no probe id supplied, list available
                var jlinkDevices = ListDevices();

                if (jlinkDevices.Count > 0)
                {
                    // take the 1st one
                    ProbeId = jlinkDevices[0];
                }
                else
                {
                    // no J-Link devices found
                    throw new CantConnectToJLinkDeviceException();
                }
            }
            else
            {
                // J-Link id was supplied
                ProbeId = probeId;
            }

            // try to connect to J-Link ID device to check availability
            var cliOutput = RunJLinkCLI(Path.Combine(CmdFilesDir, "test_connection.jlink"));

            if (cliOutput.Contains("Error"))
            {
                Console.WriteLine("");

                ShowCLIOutput(cliOutput);

                throw new CantConnectToJLinkDeviceException();
            }

            // parse the output to fill in the details
            var match = Regex.Match(cliOutput, @"(Device "")(?<deviceid>\w*)("" selected\.)|(Firmware: )(?<firmware>.*)", RegexOptions.Multiline);
            if (match.Success)
            {
                // grab details
                DeviceId = match.Groups["deviceid"].ToString().Trim();

                if (match.Groups["firmware"] != null)
                {
                    Firmare = match.Groups["firmware"].ToString().Trim();
                }
            }

            match = Regex.Match(cliOutput, @"(Hardware version: )(?<hardware>.*)", RegexOptions.Multiline);
            if (match.Success && match.Groups["hardware"] != null)
            {
                Hardware = match.Groups["hardware"].ToString().Trim();
            }

            match = Regex.Match(cliOutput, $"(Found )(?<devicecpu>.*)(,)");
            if (match.Success)
            {
                // grab details
                DeviceCPU = match.Groups["devicecpu"].ToString().Trim();
            }
        }

        /// <summary>
        /// Perform mass erase on the connected device.
        /// </summary>
        public ExitCodes MassErase()
        {
            return ExecuteMassErase(null);
        }

        /// <summary>
        /// List connected Silabs Giant Gecko devices.
        /// </summary>
        /// <returns>A collection of connected Silabs Giant Gecko devices.</returns>
        public static List<string> ListDevices()
        {
            var cliOutput = ExecuteListDevices();

            // (successful) output from the above is
            //
            //J-Link>ShowEmuList USB
            //J-Link[0]: Connection: USB, Serial number: 440258861, ProductName: J-Link EnergyMicro
            //J-Link>Exit

            // set pattern to serial number
            const string regexPattern = @"(?<=Connection: USB, Serial number:\s)(?<serial>\d+)";

            var myRegex1 = new Regex(regexPattern, RegexOptions.Multiline);
            var jlinkMatches = myRegex1.Matches(cliOutput);

            if (jlinkMatches.Count == 0)
            {
                // no J-Link probe found
                return new List<string>();
            }

            return jlinkMatches.Cast<Match>().Select(i => i.Value).ToList();
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
                ProbeId);
        }
    }
}
