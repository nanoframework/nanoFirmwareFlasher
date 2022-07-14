//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class SilinkCli
    {
        private const int SilinkTelnetPort = 49000;
        private const int SilinkAdminPort = SilinkTelnetPort + 2;
        private const int TargetBaudRate = 921600;

        /// <summary>
        /// This will set the baud rate of the VCP in a Silabs Jlink board.
        /// Before setting the value the devices is queried and the new setting is applied only if needed.
        /// </summary>
        /// <param name="probeId">Id of the JLink probe to adjust the baud rate.</param>
        /// <param name="verbosity">Verbosity level for the operation. <see cref="VerbosityLevel.Quiet"/> will be used if not specified.</param>
        /// <returns></returns>
        /// <remarks>
        /// Currently this operation is only supported in Windows machines.
        /// </remarks>
        public static ExitCodes SetVcpBaudRate(
            string probeId,
            VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            // check that we can run on this platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Console.WriteLine("");
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.WriteLine($"Setting VCP baud rate in {OSPlatform.OSX} is not supported.");

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("");

                return ExitCodes.E8002;
            }

            Console.ForegroundColor = ConsoleColor.White;

            // launch silink 
            if (verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine("Launching silink...");
            }

            var silinkCli = RunSilinkCLI(Path.Combine(probeId));

            var silinkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint silinkEndPoint = new(IPAddress.Parse("127.0.0.1"), SilinkAdminPort);

            try
            {
                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    Console.WriteLine("Connecting to admin console...");
                }

                silinkSocket.Connect(silinkEndPoint);

                Thread.Sleep(250);

                // query current config
                byte[] buffer = Encoding.Default.GetBytes("serial vcom\r");
                silinkSocket.Send(buffer);

                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    Console.WriteLine("Querying current config...");
                }

                Thread.Sleep(250);

                buffer = new byte[1024];
                int receiveCount = silinkSocket.Receive(buffer, 0, buffer.Length, 0);

                var currentConfig = Encoding.Default.GetString(buffer, 0, receiveCount);

                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    Console.WriteLine($"{currentConfig}");
                }

                if (!string.IsNullOrEmpty(currentConfig))
                {
                    // interpret reply
                    const string regexPattern = "(?:Stored port speed  : )(?'baudrate'\\d+)";

                    var myRegex1 = new Regex(regexPattern, RegexOptions.Multiline);
                    var currentBaud = myRegex1.Match(currentConfig);

                    if (currentBaud.Success)
                    {
                        // verify current setting
                        if (int.TryParse(currentBaud.Groups["baudrate"].Value, out int baudRate) && baudRate == TargetBaudRate)
                        {
                            if (verbosity >= VerbosityLevel.Detailed)
                            {
                                Console.WriteLine("VCP baud rate it's correct! Nothing to do here.");
                            }

                            return ExitCodes.OK;
                        }
                    }

                    // need to set baud rate because it's different

                    if (verbosity == VerbosityLevel.Normal)
                    {
                        Console.Write("Trying to set VCP baud rate...");
                    }
                    else if (verbosity > VerbosityLevel.Normal)
                    {
                        Console.WriteLine("Trying to set VCP baud rate...");
                    }

                    Thread.Sleep(250);

                    // compose command
                    buffer = Encoding.Default.GetBytes($"serial vcom config speed {TargetBaudRate}\r");
                    silinkSocket.Send(buffer);

                    Thread.Sleep(250);

                    buffer = new byte[1024];
                    receiveCount = silinkSocket.Receive(buffer, 0, buffer.Length, 0);

                    var opResult = Encoding.Default.GetString(buffer, 0, receiveCount);

                    if (verbosity >= VerbosityLevel.Diagnostic)
                    {
                        Console.WriteLine($"{opResult}");
                    }

                    if (opResult.Contains($"Baudrate set to {TargetBaudRate} bps"))
                    {
                        if (verbosity == VerbosityLevel.Normal)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(" OK");

                            Console.WriteLine("");

                            Console.ForegroundColor = ConsoleColor.White;
                        }
                        else if (verbosity > VerbosityLevel.Normal)
                        {
                            Console.WriteLine("Success!");
                        }

                        return ExitCodes.OK;
                    }
                    else
                    {
                        if (verbosity == VerbosityLevel.Normal)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("FAILED!");

                            Console.WriteLine("");
                            Console.ForegroundColor = ConsoleColor.Red;

                            Console.WriteLine($"{opResult.Replace("PK> ", "")}");

                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine("");
                        }
                        else if (verbosity > VerbosityLevel.Normal)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("FAILED!");
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine("");
                        }

                        return ExitCodes.E8002;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("");
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"Exception occurred: {ex.Message}");

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("");

                return ExitCodes.E8002;
            }
            finally
            {
                // close socket
                silinkSocket?.Close();

                // kill process tree (to include JLink) 
                silinkCli.Kill(true);
            }

            return ExitCodes.E8002;
        }

        internal static Process RunSilinkCLI(string probeId)
        {
            // prepare the process start of J-Link
            string appName = "silink.exe";
            string appDir = Path.Combine(Program.ExecutingPath, "silink");


            Process silinkCli = new Process();
            string parameter = $" -sn {probeId} -automap {SilinkTelnetPort}";

            silinkCli.StartInfo = new ProcessStartInfo(Path.Combine(appDir, appName), parameter)
            {
                WorkingDirectory = appDir,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            // start siLink CLI and...
            silinkCli.Start();

            // ... return the process
            return silinkCli;
        }
    }
}
