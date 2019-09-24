using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class StmJtagDevice
    {
        /// <summary>
        /// This property is <see langword="true"/> if a JTAG device is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(DeviceId);

        /// <summary>
        /// ID of the connected JTAG device.
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; internal set; } = VerbosityLevel.Normal;

        /// <summary>
        /// Property with option for performing mass erase on the connected device.
        /// If <see langword="false"/> only the flash sectors that will programmed are erased.
        /// </summary>
        public bool DoMassErase { get; set; } = false;

        /// <summary>
        /// Creates a new <see cref="StmJtagDevice"/>. If a JTAG device ID is provided it will try to connect to that device.
        /// </summary>
        /// <param name="deviceId">ID of the device to connect to.</param>
        public StmJtagDevice(string jtagId = null)
        {
            if (string.IsNullOrEmpty(jtagId))
            {
                // no JTAG id supplied, list available
                var jtagDevices = ListDevices();

                if(jtagDevices.Count > 0)
                {
                    // take the 1st one
                    jtagId = jtagDevices[0];
                }
                else
                {
                    // no JTAG devices found
                    return;
                }
            }
            else
            {
                // JTAG id supplied, try to connect to device to check availability
                var cliOuput = RunStLinkCli($"-c SN={jtagId} HOTPLUG");

                if (!cliOuput.Contains("Connected via SWD."))
                {
                    throw new CantConnectToJtagDeviceException();
                }
            }

            // store JTAG device ID
            DeviceId = jtagId;
        }

        /// <summary>
        /// Flash the HEX supplied to the connected device.
        /// </summary>
        /// <param name="files"></param>
        public ExitCodes FlashHexFiles(IEnumerable<string> files)
        {
            // check file existence
            foreach (string f in files)
            {
                if (!File.Exists(f))
                {
                    return ExitCodes.E5003;
                }
            }

            // try to connect to device with RESET
            var cliOuput = RunStLinkCli($"-c SN={DeviceId} UR");

            if (!cliOuput.Contains("Connected via SWD."))
            {
                return ExitCodes.E5002;
            }

            // erase flash
            if (DoMassErase)
            {
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write("Mass erase device...");
                }

                cliOuput = RunStLinkCli($"-c SN={DeviceId} UR -ME");

                if (!cliOuput.Contains("Flash memory erased."))
                {
                    return ExitCodes.E5005;
                }

                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }

                // toggle mass erase so it's only performed before the first file is flashed
                DoMassErase = false;
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.Write("Flashing device...");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine("Flashing device...");
            }

            // program HEX file(s)
            foreach (string hexFile in files)
            {
                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    Console.WriteLine($"{Path.GetFileName(hexFile)}");
                }

                cliOuput = RunStLinkCli($"-c SN={DeviceId} UR -Q -P {hexFile}");

                if (!cliOuput.Contains("Programming Complete."))
                {
                    return ExitCodes.E5006;
                }
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.WriteLine(" OK");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine("Flashing completed...");
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Flash the BIN supplied to the connected device.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="addresses"></param>
        public ExitCodes FlashBinFiles(IEnumerable<string> files, IEnumerable<string> addresses)
        {
            // check file existence
            foreach (string f in files)
            {
                if (!File.Exists(f))
                {
                    return ExitCodes.E5003;
                }
            }

            // check address(es)

            // need to match files count
            if(files.Count() != addresses.Count())
            {
                return ExitCodes.E5009;
            }

            foreach (string address in addresses)
            {
                if (string.IsNullOrEmpty(address))
                {
                    return ExitCodes.E5007;
                }
                else
                {
                    // format too
                    if (!address.StartsWith("0x"))
                    {
                        return ExitCodes.E5008;
                    }

                    // try parse
                    // need to remove the leading 0x and to specify that hexadecimal values are allowed
                    int dummyAddress;
                    if (!int.TryParse(address.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out dummyAddress))
                    {
                        return ExitCodes.E5008;
                    }
                }
            }

            // try to connect to device with RESET
            var cliOuput = RunStLinkCli($"-c SN={DeviceId} UR");

            if (!cliOuput.Contains("Connected via SWD."))
            {
                return ExitCodes.E5002;
            }

            // erase flash
            if (DoMassErase)
            {
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write("Mass erase device...");
                }

                cliOuput = RunStLinkCli($"-c SN={DeviceId} UR -ME");

                if (!cliOuput.Contains("Flash memory erased."))
                {
                    return ExitCodes.E5005;
                }

                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }

                // toggle mass erase so it's only performed before the first file is flashed
                DoMassErase = false;
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.Write("Flashing device...");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine("Flashing device...");
            }

            // program BIN file(s)
            int index = 0;
            foreach (string binFile in files)
            {
                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    Console.WriteLine($"{Path.GetFileName(binFile)} @ {addresses.ElementAt(index)}");
                }

                cliOuput = RunStLinkCli($"-c SN={DeviceId} UR -Q -P {binFile} {addresses.ElementAt(index++)}");

                if (!cliOuput.Contains("Programming Complete."))
                {
                    return ExitCodes.E5006;
                }
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.WriteLine(" OK");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine("Flashing completed...");
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Search connected STM JTAG devices.
        /// </summary>
        /// <returns>A collection of connected STM JTAG devices.</returns>
        public static List<string> ListDevices()
        {
            var cliOuput = RunStLinkCli("-List");

            // (successful) output from the above is
            //
            //---Available ST - LINK Probes List ---
            //
            // ST - LINK Probe 0:
            //     SN: 066CFF535752877167012515
            //     FW: V2J29M18
            //
            // ST - LINK Probe 1:
            //     SN: 066CDD535752877167010000
            //     FW: V2J29M18
            //
            //----------------------------------

            // set pattern to serial number
            string regexPattern = @"(?<=SN:\s)(?<serial>.{24})";

            var myRegex1 = new Regex(regexPattern, RegexOptions.Multiline);
            var jtagMatches = myRegex1.Matches(cliOuput);

            if(jtagMatches.Count == 0)
            {
                // no JTAG found
                return new List<string>();
            }

            return jtagMatches.Cast<Match>().Select(i => i.Value).ToList();
        }
        
        private static string RunStLinkCli(string arguments)
        {
            try
            {
                Process stLinkCli = new Process();
                stLinkCli.StartInfo = new ProcessStartInfo(Path.Combine(Program.ExecutingPath, "stlink", "ST-LINK_CLI.exe"), arguments)
                {
                    WorkingDirectory = Path.Combine(Program.ExecutingPath, "stlink"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };


                // start ST Link CLI and...
                stLinkCli.Start();

                // ... wait for exit
                stLinkCli.WaitForExit();

                // collect output messages
                return stLinkCli.StandardOutput.ReadToEnd();
            }
            catch(Exception ex)
            {
                throw new StLinkCliExecutionException(ex.Message);
            }
        }
    }
}
