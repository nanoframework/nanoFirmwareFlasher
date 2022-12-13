//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Base class for StmDeviceBase.
    /// </summary>
    public abstract class StmDeviceBase
    {
        private static bool _pathChecked = false;
        private static string _stCLIErrorMessage;

        /// <summary>
        /// Property with option for performing mass erase on the connected device.
        /// If <see langword="false"/> only the flash sectors that will programmed are erased.
        /// </summary>
        public bool DoMassErase { get; set; } = false;

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Normal;

        /// <summary>
        /// Runs the STM32 programmer CLI
        /// </summary>
        /// <param name="arguments">arguments to send</param>
        /// <returns>The returned message</returns>
        /// <exception cref="StLinkCliExecutionException"></exception>
        public static string RunSTM32ProgrammerCLI(string arguments)
        {
            try
            {
                // reset error message
                _stCLIErrorMessage = string.Empty;

                // check execution path for diacritics 
                if (!_pathChecked)
                {
                    if (!Utilities.ExecutingPath.IsNormalized(NormalizationForm.FormD))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine("");
                        Console.WriteLine("**************************** WARNING ****************************");
                        Console.WriteLine("nanoff installation path contains diacritic chars!");
                        Console.WriteLine("There are know issues executing some commands in this situation.");
                        Console.WriteLine("Recommend that the tool be installed in a path without those.");
                        Console.WriteLine("For a detailed explanation please visit https://git.io/JEcpK.");
                        Console.WriteLine("*****************************************************************");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    // done
                    _pathChecked = true;
                }

                string appName = string.Empty;
                string appDir = string.Empty;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    appName = "STM32_Programmer_CLI.exe";
                    appDir = "stlink";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    appName = "STM32_Programmer_CLI";
                    appDir = "stlinkMac";
                }
                else
                {
                    appName = "STM32_Programmer_CLI";
                    appDir = "stlinkLinux";
                }

                var stLinkCli = new Process
                {
                    StartInfo = new ProcessStartInfo(Path.Combine(Utilities.ExecutingPath, appDir, "bin", appName),
                        arguments)
                    {
                        WorkingDirectory = Path.Combine(Utilities.ExecutingPath, appDir, "bin"),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                // start STM32 Programmer CLI and...
                stLinkCli.Start();

                // ... wait for exit (1 min max!)
                stLinkCli.WaitForExit((int)TimeSpan.FromMinutes(1).TotalMilliseconds);

                // collect output messages
                string cliOutput = stLinkCli.StandardOutput.ReadToEnd();

                // check and parse any error in the output
                _stCLIErrorMessage = GetErrorMessageFromSTM32CLI(cliOutput);

                return cliOutput;
            }
            catch (Exception ex)
            {
                throw new StLinkCliExecutionException(ex.Message);
            }
        }

        /// <summary>
        /// Gets the Error Message From STM32CLI.
        /// </summary>
        /// <param name="cliOutput">The retrived input</param>
        /// <returns></returns>
        public static string GetErrorMessageFromSTM32CLI(string cliOutput)
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
                if (cliOutput.Contains("DEV_USB_COMM_ERR"))
                {
                    return "USB communication error. Please unplug and plug again the ST device.";
                }
            }

            return "";
        }

        /// <summary>
        /// Output to CLI
        /// </summary>
        /// <param name="cliOutput">Message to display</param>
        public void ShowCLIOutput(string cliOutput)
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

        /// <summary>
        /// Lists all found devices.
        /// </summary>
        /// <returns></returns>
        public static string ExecuteListDevices()
        {
            return RunSTM32ProgrammerCLI("--list");
        }

        /// <summary>
        /// Wipes the whole flash memory of the device.
        /// </summary>
        /// <param name="connectDetails">The device connection details</param>
        /// <returns></returns>
        public ExitCodes ExecuteMassErase(string connectDetails)
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Mass erase device...");
            }

            var cliOutput = RunSTM32ProgrammerCLI($"-c {connectDetails} mode=UR -e all");

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

        /// <summary>
        /// Flash HEX files to device
        /// </summary>
        /// <param name="files">The HEX files to flash</param>
        /// <param name="connectDetails">The device connection details</param>
        /// <returns></returns>
        public ExitCodes ExecuteFlashHexFiles(
            IList<string> files,
            string connectDetails)
        {
            // check file existence
            if (files.Any(f => !File.Exists(f)))
            {
                return ExitCodes.E5003;
            }

            // erase flash
            if (DoMassErase)
            {
                var eraseResult = ExecuteMassErase(connectDetails);

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

                var cliOutput = RunSTM32ProgrammerCLI($"-c {connectDetails} -w \"{hexFile}\"");

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
        /// Flash BIN files to device.
        /// </summary>
        /// <param name="files">The files to flash</param>
        /// <param name="addresses">The memory locations</param>
        /// <param name="connectDetails">The device connection details</param>
        /// <returns></returns>
        public ExitCodes ExecuteFlashBinFiles(
            IList<string> files,
            IList<string> addresses,
            string connectDetails)
        {
            // check file existence
            if (files.Any(f => !File.Exists(f)))
            {
                return ExitCodes.E5003;
            }

            // check address(es)

            // need to match files count
            if (files.Count != addresses.Count)
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

            // erase flash
            if (DoMassErase)
            {
                var eraseResult = ExecuteMassErase(connectDetails);

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

                var cliOutput = RunSTM32ProgrammerCLI($"-c {connectDetails} mode=UR -w \"{binFile}\" {addresses.ElementAt(index++)}");

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

            Console.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }
    }
}
