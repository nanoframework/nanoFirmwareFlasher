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
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that performs the interface with the JLink CLI.
    /// </summary>
    public class JLinkCli
    {
        /// <summary>
        /// Token to replace with file path.
        /// </summary>
        private const string FilePathToken = "{FILE_PATH}";

        /// <summary>
        /// Token to replace with address to flash.
        /// </summary>
        private const string FlashAddressToken = "{FLASH_ADDRESS}";

        /// <summary>
        /// Template for JLink command file to flash a file.
        /// </summary>
        private const string FlashFileCommandTemplate = $@"
USB
speed auto
Halt
LoadFile {FilePathToken} {FlashAddressToken}
Reset
Go
Exit
";

        /// <summary>
        /// Property with option for performing mass erase on the connected device.
        /// If <see langword="false"/> only the flash sectors that will programmed are erased.
        /// </summary>
        public bool DoMassErase { get; set; } = false;

        /// <summary>
        /// Path to the J-Link command files.
        /// </summary>
        public static string CmdFilesDir => Path.Combine(Utilities.ExecutingPath, "jlinkCmds");

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Normal;

        /// <summary>
        /// Executes the command to get a list of the connected J-Link device.
        /// </summary>
        /// <returns></returns>
        public static string ExecuteListDevices()
        {
            return RunJLinkCLI(Path.Combine(CmdFilesDir, "list_probes.jlink"));
        }

        /// <summary>
        /// Executes the operation to mass erase on the connected J-Link device.
        /// </summary>
        public ExitCodes ExecuteMassErase(string probeId)
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Mass erase device...");
            }

            var cliOutput = RunJLinkCLI(Path.Combine(CmdFilesDir, "erase_gg11.jlink"));

            if (!cliOutput.Contains("Erasing done."))
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
        /// Executes an operation that flashes a collection of binary files to a connected J-Link device.
        /// </summary>
        /// <param name="files">List of files to flash to the device.</param>
        /// <param name="addresses">List of addresses where to flash the files to.</param>
        /// <param name="probeId">ID of the J-Link device to execute the operation into. Leave <see langword="null"/> to use the default address.</param>
        /// <returns></returns>
        public ExitCodes ExecuteFlashBinFiles(
            IList<string> files,
            IList<string> addresses,
            string probeId)
        {
            // check file existence
            if (files.Any(f => !File.Exists(f)))
            {
                return ExitCodes.E5004;
            }

            // perform check on address(es)
            if (!addresses.Any())
            {
                return ExitCodes.E5007;
            }

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
                if (!int.TryParse(
                    address.Substring(2),
                    System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _))
                {
                    return ExitCodes.E5008;
                }
            }

            // J-Link can't handle diacritc chars
            // developer note: reported to Segger (Case: 60276735) and can be removed if this is fixed/improved
            foreach (string binFile in files)
            {
                if (!binFile.IsNormalized(NormalizationForm.FormD))
                {
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("");
                    Console.WriteLine("********************************* WARNING *********************************");
                    Console.WriteLine("Diacritic chars found in the path to a binary file!");
                    Console.WriteLine("J-Link can't handle those, please use a path with plain simple ASCII chars.");
                    Console.WriteLine("***************************************************************************");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E8003;
                }

                if (binFile.Contains(' '))
                {
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("");
                    Console.WriteLine("************************* WARNING **************************");
                    Console.WriteLine("Binary file path contains spaces!");
                    Console.WriteLine("J-Link can't handle those, please use a path without spaces.");
                    Console.WriteLine("************************************************************");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;

                    return ExitCodes.E8003;
                }
            }

            // erase flash
            if (DoMassErase)
            {
                var eraseResult = ExecuteMassErase(probeId);

                if (eraseResult != ExitCodes.OK)
                {
                    return eraseResult;
                }

                // toggle mass erase so it's only performed before the first file is flashed
                DoMassErase = false;
            }

            if (Verbosity < VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Flashing device...");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Flashing device...");
            }

            // program BIN file(s)
            int index = 0;
            foreach (string binFile in files)
            {
                if (Verbosity > VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"{Path.GetFileName(binFile)} @ {addresses.ElementAt(index)}");
                }

                // compose JLink command file
                var jlinkCmdContent = FlashFileCommandTemplate.Replace(FilePathToken, binFile).Replace(FlashAddressToken, addresses.ElementAt(index++));
                var jlinkCmdFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jlink");

                // create file
                var jlinkCmdFile = File.CreateText(jlinkCmdFilePath);
                jlinkCmdFile.Write(jlinkCmdContent);
                jlinkCmdFile.Close();

                var cliOutput = RunJLinkCLI(jlinkCmdFilePath);

                // OK to delete the JLink command file
                File.Delete(jlinkCmdFilePath);

                if (cliOutput.Contains("Programming failed."))
                {
                    ShowCLIOutput(cliOutput);

                    return ExitCodes.E5006;
                }

                if (Verbosity >= VerbosityLevel.Normal
                    && cliOutput.Contains("Skipped. Contents already match"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;

                    Console.WriteLine("");
                    Console.WriteLine("********************* WARNING **********************");
                    Console.WriteLine("Skipped flashing. Contents already match the update.");
                    Console.WriteLine("****************************************************");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;
                }
            }

            if (Verbosity < VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" OK");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Flashing completed...");
            }

            Console.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        public void ShowCLIOutput(string cliOutput)
        {
            // show CLI output, if verbosity is diagnostic
            if (Verbosity == VerbosityLevel.Diagnostic)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.WriteLine();
                Console.WriteLine(">>>>>>>>");
                Console.WriteLine($"{cliOutput}");
                Console.WriteLine(">>>>>>>>");

                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal static string RunJLinkCLI(string cmdFile, string arguments = null)
        {
            // prepare the process start of J-Link
            string appName;
            string appDir;
            string cmdFilesDir = Path.Combine(Utilities.ExecutingPath, "jlinkCmds", cmdFile);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                appName = "JLink.exe";
                appDir = Path.Combine(Utilities.ExecutingPath, "jlink");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                appName = "JLinkExe";
                appDir = Path.Combine(Utilities.ExecutingPath, "jlinkMac");
            }
            else
            {
                appName = "JLinkExe";
                appDir = Path.Combine(Utilities.ExecutingPath, "jlinkLinux");

                Process jlinkExe_ = new Process();

                // Making sure J-Link is executable
                jlinkExe_.StartInfo = new ProcessStartInfo("chmod", $"+x {Path.Combine(appDir, appName)}")
                {
                    WorkingDirectory = appDir,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                if (!jlinkExe_.Start())
                {
                    throw new InvalidOperationException("Error changing permissions for J-Link executable!");
                }

                if (!jlinkExe_.WaitForExit(250))
                {
                    throw new InvalidOperationException("Error changing permissions for J-Link executable!");
                }
            }

            Process jlinkCli = new();
            string parameter = $" -nogui 1 -device default -si swd -CommandFile {cmdFilesDir}";

            jlinkCli.StartInfo = new ProcessStartInfo(Path.Combine(appDir, appName), parameter)
            {
                WorkingDirectory = appDir,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            // start J-Link Programmer CLI and...
            jlinkCli.Start();

            // ... wait for exit (30secs max!)
            jlinkCli.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);

            return jlinkCli.StandardOutput.ReadToEnd();
        }
    }
}
