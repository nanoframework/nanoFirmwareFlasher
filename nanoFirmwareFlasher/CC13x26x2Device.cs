//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class CC13x26x2Device
    {
        /// <summary>
        /// Configuration file for the device to connect to.
        /// </summary>
        public string ConfigurationFile { get; }

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
        /// Creates a new <see cref="CC13x26x2Device"/>.
        /// </summary>
        public CC13x26x2Device(string configurationFile)
        {
            ConfigurationFile = configurationFile;
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

            string cliOuput;

            // TODO
            // use the -d switch
            //// erase flash
            //if (DoMassErase)
            //{
            //    if (Verbosity >= VerbosityLevel.Normal)
            //    {
            //        Console.Write("Mass erase device...");
            //    }

            //    cliOuput = RunUniflashCli($"-c SN={DeviceId} UR -ME");

            //    if (!cliOuput.Contains("Flash memory erased."))
            //    {
            //        return ExitCodes.E5005;
            //    }

            //    if (Verbosity >= VerbosityLevel.Normal)
            //    {
            //        Console.WriteLine(" OK");
            //    }
            //    else
            //    {
            //        Console.WriteLine("");
            //    }

            //    // toggle mass erase so it's only performed before the first file is flashed
            //    DoMassErase = false;
            //}

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

                cliOuput = RunUniflashCli($" flash -c {ConfigurationFile} -f -v {hexFile}");

                if (!cliOuput.Contains("Program verification successful"))
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

            string cliOuput;

            // TODO
            // use the -d switch
            //// erase flash
            //if (DoMassErase)
            //{
            //    if (Verbosity >= VerbosityLevel.Normal)
            //    {
            //        Console.Write("Mass erase device...");
            //    }

            //    cliOuput = RunUniflashCli($"-b");

            //    if (!cliOuput.Contains("Flash memory erased."))
            //    {
            //        Console.WriteLine("");
            //        return ExitCodes.E5005;
            //    }

            //    if (Verbosity >= VerbosityLevel.Normal)
            //    {
            //        Console.WriteLine(" OK");
            //    }
            //    else
            //    {
            //        Console.WriteLine("");
            //    }

            //    // toggle mass erase so it's only performed before the first file is flashed
            //    DoMassErase = false;
            //}

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

                cliOuput = RunUniflashCli($" flash -c {ConfigurationFile} -f -v {binFile},{addresses.ElementAt(index++)}");

                if (!cliOuput.Contains("Program verification successful"))
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
        /// Reset MCU of connected CC13x2 device.
        /// </summary>
        public ExitCodes ResetMcu()
        {
            // try to connect to device with RESET
            var cliOuput = RunUniflashCli($" flash -c {ConfigurationFile} -r 0");

            if (!cliOuput.Contains("CPU Reset is issued"))
            {
                Console.WriteLine("");
                return ExitCodes.E5010;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine(" OK");
            }
            else
            {
                Console.WriteLine("");
            }

            return ExitCodes.OK;
        }

        private static string RunUniflashCli(string arguments)
        {
            try
            {
                Process uniflashCli = new Process();
                uniflashCli.StartInfo = new ProcessStartInfo(Path.Combine(Program.ExecutingPath, "uniflash\\DebugServer\\bin", "DSLite.exe"), arguments)
                {
                    WorkingDirectory = Path.Combine(Program.ExecutingPath, "uniflash"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };


                // start Uniflash CLI and...
                uniflashCli.Start();

                // ... wait for exit
                uniflashCli.WaitForExit();

                // collect output messages
                return uniflashCli.StandardOutput.ReadToEnd();
            }
            catch(Exception ex)
            {
                throw new UniflashCliExecutionException(ex.Message);
            }
        }
    }
}
