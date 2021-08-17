//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class StmJtagDevice
    {
        /// <summary>
        /// Error message from ST CLI.
        /// </summary>
        private static string _stCLIErrorMessage;

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
                var cliOutput = RunSTM32ProgrammerCLI($"-c SN={jtagId} HOTPLUG");

                if (!cliOutput.Contains("Connected via SWD."))
                {
                    ShowCLIOutput(cliOutput);

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
        public ExitCodes FlashHexFiles(IList<string> files)
        {
            // check file existence
            if (files.Any(f => !File.Exists(f)))
            {
                return ExitCodes.E5003;
            }

            // try to connect to device with RESET
            var cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={DeviceId} mode=UR");

            if (cliOutput.Contains("Error"))
            {
                Console.WriteLine("");

                ShowCLIOutput(cliOutput);

                return ExitCodes.E5002;
            }

            // erase flash
            if (DoMassErase)
            {
                var eraseResult = MassErase();

                if (eraseResult != ExitCodes.OK)
                {
                    return eraseResult;
                }

                // toggle mass erase so it's only performed before the first file is flashed
                DoMassErase = false;
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Flashing device...");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Flashing device...");
            }

            // program HEX file(s)
            foreach (string hexFile in files)
            {
                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{Path.GetFileName(hexFile)}");
                }

                cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={DeviceId} mode=UR -w \"{hexFile}\"");

                if (!cliOutput.Contains("File download complete"))
                {
                    ShowCLIOutput(cliOutput);

                    return ExitCodes.E5006;
                }
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Flashing completed...");
            }
            Console.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        /// <summary>
        /// Flash the BIN supplied to the connected device.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="addresses"></param>
        public ExitCodes FlashBinFiles(IList<string> files, IList<string> addresses)
        {
            // check file existence
            if (files.Any(f => !File.Exists(f)))
            {
                return ExitCodes.E5003;
            }

            // check address(es)

            // need to match files count
            if(files.Count != addresses.Count)
            {
                return ExitCodes.E5009;
            }

            foreach (string address in addresses)
            {
                if (string.IsNullOrEmpty(address))
                {
                    return ExitCodes.E5007;
                }

                // format too
                if (!address.StartsWith("0x"))
                {
                    return ExitCodes.E5008;
                }

                // try parse
                // need to remove the leading 0x and to specify that hexadecimal values are allowed
                if (!int.TryParse(address.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    return ExitCodes.E5008;
                }
            }

            // need a couple of seconds before issuing next command
            Thread.Sleep(2000);

            // try to connect to device with RESET
            var cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={DeviceId} mode=UR");

            if (!cliOutput.Contains("Connected via SWD."))
            {
                Console.WriteLine("");

                ShowCLIOutput(cliOutput);

                return ExitCodes.E5002;
            }

            // erase flash
            if (DoMassErase)
            {
                var eraseResult = MassErase();

                if (eraseResult != ExitCodes.OK)
                {
                    return eraseResult;
                }

                // toggle mass erase so it's only performed before the first file is flashed
                DoMassErase = false;
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Flashing device...");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Flashing device...");
            }

            // program BIN file(s)
            int index = 0;
            foreach (string binFile in files)
            {
                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"{Path.GetFileName(binFile)} @ {addresses.ElementAt(index)}");
                }

                cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={DeviceId} mode=UR -w \"{binFile}\" {addresses.ElementAt(index++)}");

                if (!cliOutput.Contains("Programming Complete."))
                {
                    ShowCLIOutput(cliOutput);

                    return ExitCodes.E5006;
                }
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
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
            var cliOutput = RunSTM32ProgrammerCLI("--list");

            // (successful) output from the above is
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
            var jtagMatches = myRegex1.Matches(cliOutput);

            if (jtagMatches.Count == 0)
            {
                // no JTAG found
                return new List<string>();
            }

            return jtagMatches.Cast<Match>().Select(i => i.Value).ToList();
        }
    
        /// <summary>
        /// Reset MCU of connected JTAG device.
        /// </summary>
        public ExitCodes ResetMcu()
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Reset MCU on device...");
            }
            
            // try to connect to device with RESET
            var cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={DeviceId} mode=UR -rst");

            if (cliOutput.Contains("Error"))
            {
                Console.WriteLine("");

                ShowCLIOutput(cliOutput);

                return ExitCodes.E5002;
            }

            if (!cliOutput.Contains("MCU Reset"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR");
                Console.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E5010;
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
        /// Reset MCU of connected JTAG device.
        /// </summary>
        public ExitCodes MassErase()
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Mass erase device...");
            }

            var cliOutput = RunSTM32ProgrammerCLI($"-c port=SWD sn={DeviceId} mode=UR -e all");

            if (!cliOutput.Contains("Mass erase successfully achieved"))
            {
                Console.WriteLine("");

                ShowCLIOutput(cliOutput);

                return ExitCodes.E5005;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK");
            }
            else
            {
                Console.WriteLine("");
            }

            Console.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        private void ShowCLIOutput(string cliOutput)
        {
            // show CLI output, if verbosity is diagnostic
            if (Verbosity == VerbosityLevel.Diagnostic)
            {
                Console.WriteLine(">>>>>>>>");
                Console.WriteLine($"{cliOutput}");
                Console.WriteLine(">>>>>>>>");
            }

            // show error message from CLI, if there is one
            if (!string.IsNullOrEmpty(_stCLIErrorMessage))
            {
                // show error detail, if available
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine(_stCLIErrorMessage);

                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private static string RunSTM32ProgrammerCLI(string arguments)
        {
            try
            {
                // reset error message
                _stCLIErrorMessage = string.Empty;

                var stLinkCli = new Process
                {
                    StartInfo = new ProcessStartInfo(Path.Combine(Program.ExecutingPath, "stlink", "bin", "STM32_Programmer_CLI.exe"),
                        arguments)
                    {
                        WorkingDirectory = Path.Combine(Program.ExecutingPath, "stlink", "bin"),
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    }
                };

                // start STM32 Programmer CLI and...
                stLinkCli.Start();

                // ... wait for exit
                stLinkCli.WaitForExit();

                // collect output messages
                string cliOutput = stLinkCli.StandardOutput.ReadToEnd();

                // check and parse any error in the output
                _stCLIErrorMessage = GetErrorMessageFromSTM32CLI(cliOutput);

                return cliOutput;
            }
            catch(Exception ex)
            {
                throw new StLinkCliExecutionException(ex.Message);
            }
        }

        private static string GetErrorMessageFromSTM32CLI(string cliOutput)
        {
            var regEx = new Regex(@"Error: (?<error>.+).", RegexOptions.IgnoreCase);

            var match = regEx.Match(cliOutput);

            if (match.Success)
            {
                return match.Groups["error"].Value;
            }
            else
            {
                // look for DEV_USB_COMM_ERR
                if(cliOutput.Contains("DEV_USB_COMM_ERR"))
                {
                    return "USB communication error. Please unplug and plug again the ST device.";
                }
            }

            return "";
        }
    }
}
