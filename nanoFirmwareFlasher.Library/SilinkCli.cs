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
    /// <summary>
    /// SI Link CLI.
    /// </summary>
    public class SilinkCli
    {
        private const int SilinkTelnetPort = 49000;
        private const int SilinkAdminPort = SilinkTelnetPort + 2;
        private const int DefaultBaudRate = 921600;

        /// <summary>
        /// This will set the baud rate of the VCP in a Silabs Jlink board.
        /// Before setting the value the devices is queried and the new setting is applied only if needed.
        /// </summary>
        /// <param name="probeId">Id of the JLink probe to adjust the baud rate.</param>
        /// <param name="baudRate">Value of baud rate to set.</param>
        /// <param name="verbosity">Verbosity level for the operation. <see cref="VerbosityLevel.Quiet"/> will be used if not specified.</param>
        /// <returns></returns>
        /// <remarks>
        /// Currently this operation is only supported in Windows machines.
        /// </remarks>
        public static ExitCodes SetVcpBaudRate(
            string probeId,
            int baudRate,
            VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            // check that we can run on this platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                OutputWriter.WriteLine("");
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                OutputWriter.WriteLine($"Setting VCP baud rate in {OSPlatform.OSX} is not supported.");

                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("");

                return ExitCodes.E8002;
            }

            // store baud rate value
            int targetBaudRate = baudRate == 0 ? DefaultBaudRate : baudRate;

            OutputWriter.ForegroundColor = ConsoleColor.White;

            // launch silink 
            if (verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine("Launching silink...");
            }

            Process silinkCli = RunSilinkCLI(Path.Combine(probeId));

            var silinkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint silinkEndPoint = new(IPAddress.Parse("127.0.0.1"), SilinkAdminPort);

            try
            {
                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine("Connecting to admin console...");
                }

                silinkSocket.Connect(silinkEndPoint);

                Thread.Sleep(250);

                // query current config
                byte[] buffer = Encoding.Default.GetBytes("serial vcom\r");
                silinkSocket.Send(buffer);

                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine("Querying current config...");
                }

                Thread.Sleep(250);

                buffer = new byte[1024];
                int receiveCount = silinkSocket.Receive(buffer, 0, buffer.Length, 0);

                string currentConfig = Encoding.Default.GetString(buffer, 0, receiveCount);

                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine($"{currentConfig}");
                }

                if (!string.IsNullOrEmpty(currentConfig))
                {
                    // interpret reply
                    const string regexPattern = "(?:Stored port speed  : )(?'baudrate'\\d+)";

                    var myRegex1 = new Regex(regexPattern, RegexOptions.Multiline);
                    Match currentVcomConfig = myRegex1.Match(currentConfig);

                    if (currentVcomConfig.Success)
                    {
                        // verify current setting
                        if (int.TryParse(currentVcomConfig.Groups["baud rate"].Value, out int currentBaudRate) && currentBaudRate == targetBaudRate)
                        {
                            if (verbosity >= VerbosityLevel.Detailed)
                            {
                                OutputWriter.WriteLine("VCP baud rate it's correct! Nothing to do here.");
                            }

                            return ExitCodes.OK;
                        }
                    }

                    // need to set baud rate because it's different

                    if (verbosity == VerbosityLevel.Normal)
                    {
                        OutputWriter.Write("Trying to set VCP baud rate...");
                    }
                    else if (verbosity > VerbosityLevel.Normal)
                    {
                        OutputWriter.WriteLine("Trying to set VCP baud rate...");
                    }

                    Thread.Sleep(250);

                    // compose command
                    buffer = Encoding.Default.GetBytes($"serial vcom config speed {targetBaudRate}\r");
                    silinkSocket.Send(buffer);

                    Thread.Sleep(250);

                    buffer = new byte[1024];
                    receiveCount = silinkSocket.Receive(buffer, 0, buffer.Length, 0);

                    string opResult = Encoding.Default.GetString(buffer, 0, receiveCount);

                    if (verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"{opResult}");
                    }

                    if (opResult.Contains($"Baudrate set to {targetBaudRate} bps"))
                    {
                        if (verbosity == VerbosityLevel.Normal)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Green;
                            OutputWriter.WriteLine(" OK");

                            OutputWriter.WriteLine("");

                            OutputWriter.ForegroundColor = ConsoleColor.White;
                        }
                        else if (verbosity > VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine("Success!");
                        }

                        return ExitCodes.OK;
                    }
                    else
                    {
                        if (verbosity == VerbosityLevel.Normal)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Red;
                            OutputWriter.WriteLine("FAILED!");

                            OutputWriter.WriteLine("");
                            OutputWriter.ForegroundColor = ConsoleColor.White;

                            OutputWriter.WriteLine($"{opResult.Replace("PK> ", "")}");

                            OutputWriter.WriteLine("");
                        }
                        else if (verbosity > VerbosityLevel.Normal)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Red;
                            OutputWriter.WriteLine("FAILED!");
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                            OutputWriter.WriteLine("");
                        }

                        return ExitCodes.E8002;
                    }
                }
            }
            catch (Exception ex)
            {
                OutputWriter.WriteLine("");
                OutputWriter.ForegroundColor = ConsoleColor.Red;

                OutputWriter.WriteLine($"Exception occurred: {ex.Message}");

                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("");

                return ExitCodes.E8002;
            }
            finally
            {
                // close socket
                silinkSocket?.Close();

                // kill process tree (to include JLink)
#if NET6_0_OR_GREATER
                silinkCli.Kill(true);
#else
                silinkCli.Kill();
#endif
            }

            return ExitCodes.E8002;
        }

        internal static Process RunSilinkCLI(string probeId)
        {
            // prepare the process start of J-Link
            string appName = "silink.exe";
            string appDir = Path.Combine(Utilities.ExecutingPath, "silink");


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
