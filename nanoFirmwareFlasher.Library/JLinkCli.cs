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
        /// Token to replace with address to flash.
        /// </summary>
        private const string LoadFileListToken = "{LOAD_FILE_LIST}";

        /// <summary>
        /// Template for JLink command file to flash a file.
        /// </summary>
        private const string FlashSingleFileCommandTemplate = $@"
USB
speed auto
Halt
LoadFile {FilePathToken} {FlashAddressToken}
Reset
Go
Exit
";

        /// <summary>
        /// Template for JLink command file to flash multiple files.
        /// </summary>
        private const string FlashMultipleFilesCommandTemplate = $@"
USB
speed auto
Halt
{LoadFileListToken}
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

            ShowCLIOutput(cliOutput);

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
            List<string> shadowFiles = [];

            var processFileResult = ProcessFilePaths(files, shadowFiles);

            if (processFileResult != ExitCodes.OK)
            {
                return processFileResult;
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
            bool warningPromptShown = false;

            foreach (string binFile in shadowFiles)
            {
                if (Verbosity > VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"{Path.GetFileName(binFile)} @ {addresses.ElementAt(index)}");
                }

                // compose JLink command file
                var jlinkCmdContent = FlashSingleFileCommandTemplate.Replace(FilePathToken, binFile).Replace(FlashAddressToken, addresses.ElementAt(index++));
                var jlinkCmdFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jlink");

                // create file
                var jlinkCmdFile = File.CreateText(jlinkCmdFilePath);
                jlinkCmdFile.Write(jlinkCmdContent);
                jlinkCmdFile.Close();

                var cliOutput = RunJLinkCLI(jlinkCmdFilePath);

                // OK to delete the JLink command file
                File.Delete(jlinkCmdFilePath);

                if (Verbosity >= VerbosityLevel.Normal
                    && cliOutput.Contains("Skipped. Contents already match"))
                {
                    warningPromptShown = true;

                    Console.ForegroundColor = ConsoleColor.Yellow;

                    if (Verbosity == VerbosityLevel.Normal)
                    {
                        Console.WriteLine();
                    }

                    Console.WriteLine("");
                    Console.WriteLine("******************* WARNING *********************");
                    Console.WriteLine("Skip flashing. Contents already match the update.");
                    Console.WriteLine("*************************************************");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;
                }
                else if (!(cliOutput.Contains("Flash download: Program & Verify")
                           && cliOutput.Contains("O.K.")))
                {
                    ShowCLIOutput(cliOutput);

                    return ExitCodes.E5006;
                }

                ShowCLIOutput(cliOutput);
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                if (!warningPromptShown)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" OK");
                }
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Flashing completed...");
            }

            Console.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        private ExitCodes ProcessFilePaths(IList<string> files, List<string> shadowFiles)
        {
            // J-Link can't handle diacritc chars
            // developer note: reported to Segger (Case: 60276735) and can be removed if this is fixed/improved
            foreach (string binFile in files)
            {
                // make sure path is absolute
                var binFilePath = Utilities.MakePathAbsolute(
                    Environment.CurrentDirectory,
                    binFile);

                // check file existence
                if (!File.Exists(binFilePath))
                {
                    return ExitCodes.E5004;
                }

                if (!binFilePath.IsNormalized(NormalizationForm.FormD)
                    || binFilePath.Contains(' '))
                {
                    var tempFile = Path.Combine(
                        Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.Machine),
                        Path.GetFileName(binFilePath));

                    // copy file to shadow file
                    File.Copy(
                        binFilePath,
                        tempFile,
                        true);

                    shadowFiles.Add(tempFile);
                }
                else
                {
                    // copy file to shadow list
                    shadowFiles.Add(binFile);
                }
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Executes an operation that flashes a collection of Intel HEX format files to a connected J-Link device.
        /// </summary>
        /// <param name="files">List of files to flash to the device.</param>
        /// <param name="probeId">ID of the J-Link device to execute the operation into. Leave <see langword="null"/> to use the default address.</param>
        /// <returns></returns>
        public ExitCodes ExecuteFlashHexFiles(
            IList<string> files,
            string probeId)
        {
            List<string> shadowFiles = [];

            var processFileResult = ProcessFilePaths(files, shadowFiles);

            if (processFileResult != ExitCodes.OK)
            {
                return processFileResult;
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
            StringBuilder listOfFiles = new StringBuilder();

            foreach (string hexFile in shadowFiles)
            {
                if (Verbosity > VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"{Path.GetFileName(hexFile)}");
                }

                listOfFiles.AppendLine($"LoadFile {hexFile}");
            }

            // compose JLink command file
            var jlinkCmdContent = FlashMultipleFilesCommandTemplate.Replace(
                LoadFileListToken,
                listOfFiles.ToString());

            var jlinkCmdFilePath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}.jlink");

            // create file
            var jlinkCmdFile = File.CreateText(jlinkCmdFilePath);
            jlinkCmdFile.Write(jlinkCmdContent);
            jlinkCmdFile.Close();

            var cliOutput = RunJLinkCLI(jlinkCmdFilePath);

            // OK to delete the JLink command file
            File.Delete(jlinkCmdFilePath);

            bool warningPromptShown = false;

            if (Verbosity >= VerbosityLevel.Normal
                && cliOutput.Contains("Skipped. Contents already match"))
            {
                warningPromptShown = true;

                Console.ForegroundColor = ConsoleColor.Yellow;

                if (Verbosity == VerbosityLevel.Normal)
                {
                    Console.WriteLine();
                }

                Console.WriteLine("");
                Console.WriteLine("******************* WARNING *********************");
                Console.WriteLine("Skip flashing. Contents already match the update.");
                Console.WriteLine("*************************************************");
                Console.WriteLine("");

                Console.ForegroundColor = ConsoleColor.White;
            }
            else if (!(cliOutput.Contains("Flash download: Program & Verify")
                        && cliOutput.Contains("O.K.")))
            {
                ShowCLIOutput(cliOutput);

                return ExitCodes.E5006;
            }

            ShowCLIOutput(cliOutput);

            if (Verbosity == VerbosityLevel.Normal)
            {
                if (!warningPromptShown)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" OK");
                }
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
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
                Console.WriteLine();
                Console.WriteLine(">>>>>>>>");
                Console.WriteLine(cliOutput);
                Console.WriteLine(">>>>>>>>");
                Console.WriteLine();
                Console.WriteLine();

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
