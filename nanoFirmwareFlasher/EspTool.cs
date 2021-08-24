﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class the handles all the calls to the esptool.exe.
    /// </summary>
    internal partial class EspTool
    {
        /// <summary>
        /// The serial port over which all the communication goes
        /// </summary>
        private readonly string _serialPort = null;

        /// <summary>
        /// The baud rate for the serial port. The default comming from <see cref="Options.BaudRate"/> is 9216000.
        /// </summary>
        private readonly int _baudRate = 0;

        /// <summary>
        /// The flash mode for the esptool.
        /// </summary>
        /// <remarks>
        /// See https://github.com/espressif/esptool#flash-modes for more details
        /// </remarks>
        private readonly string _flashMode = null;

        /// <summary>
        /// The flash frequency for the esptool.
        /// </summary>
        /// <remarks>
        /// This value should be in Hz; 40 MHz = 40.000.000 Hz
        /// See https://github.com/espressif/esptool#flash-modes for more details
        /// </remarks>
        private readonly int _flashFrequency = 0;

        /// <summary>
        /// Partition table size, when specified in the options.
        /// </summary>
        private readonly PartitionTableSize? _partitionTableSize = null;

        /// <summary>
        /// The size of the flash in bytes; 4 MB = 0x40000 bytes
        /// </summary>
        private int _flashSize = -1;

        /// <summary>
        /// true if the stub program is already active and we can use the --before no_reset_no_sync parameter 
        /// </summary>
        private bool _isStubActive = false;
        private bool connectPatternFound;
        private DateTime connectTimeStamp;
        private bool connectPromptShown;

        /// <summary>
        /// This property is <see langword="true"/> if the specified COM port is valid.
        /// </summary>
        internal bool ComPortAvailable => !string.IsNullOrEmpty(_serialPort);

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; internal set; } = VerbosityLevel.Normal;

        // ESP32 chip type to connect to.
        // Default is 'auto'. It's replaced with the actual chip type after detection to improve operations.
        internal string _chipType = "auto";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="serialPort">The serial port over which all the communication goes.</param>
        /// <param name="baudRate">The baud rate for the serial port.</param>
        /// <param name="flashMode">The flash mode for the esptool</param>
        /// <param name="flashFrequency">The flash frequency for the esptool</param>
        /// <param name="partitionTableSize">Partition table size to use</param>
        internal EspTool(
            string serialPort, 
            int baudRate,
            string flashMode, 
            int flashFrequency,
            PartitionTableSize? partitionTableSize)
        {
            // open/close the port to see if it is available
            using (var test = new SerialPort(serialPort, baudRate))
            {
                try
                {
                    test.Open();
                    test.Close();
                }
                catch
                {
                    // presume any exception here is caused by the serial not existing or not possible to open
                    throw new EspToolExecutionException();
                }
            }

            if(Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine($"Using {serialPort} @ {baudRate} baud to connect to ESP32.");
            }

            // set properties
            _serialPort = serialPort;
            _baudRate = baudRate;
            _flashMode = flashMode;
            _flashFrequency = flashFrequency;
            _partitionTableSize = partitionTableSize;
        }

        /// <summary>
        /// Tries reading ESP32 device details.
        /// </summary>
        /// <returns>The filled info structure with all the information about the connected ESP32 device or null if an error occured</returns>
        internal Esp32DeviceInfo GetDeviceDetails(bool requireFlashSize = true)
        {
            string messages;

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"Reading details from chip...");
            }

            // execute flash_id command and parse the result
            if (!RunEspTool(
                "flash_id",
                true,
                true,
                false,
                null,
                out messages))
            {
                throw new EspToolExecutionException(messages);
            }

            // check if we got flash size (in case we need it)
            if (requireFlashSize 
                && messages.Contains("Detected flash size: Unknown"))
            {
                // try again now without the stub
                if (!RunEspTool(
                    "flash_id",
                    false,
                    true,
                    false,
                    null,
                    out messages))
                {
                    throw new EspToolExecutionException(messages);
                }
            }
            
            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OK");
                Console.ForegroundColor = ConsoleColor.White;
            }

            var match = Regex.Match(messages, $"(Detecting chip type... )(?<type>.*)(.*?[\r\n]*)*(Chip is )(?<name>.*)(.*?[\r\n]*)*(Features: )(?<features>.*)(.*?[\r\n]*)*(Crystal is )(?<crystal>.*)(.*?[\r\n]*)*(MAC: )(?<mac>.*)(.*?[\r\n]*)*(Manufacturer: )(?<manufacturer>.*)(.*?[\r\n]*)*(Device: )(?<device>.*)(.*?[\r\n]*)*(Detected flash size: )(?<size>.*)");
            if (!match.Success)
            {
                throw new EspToolExecutionException(messages);
            }

            // grab details
            string chipType = match.Groups["type"].ToString().Trim();
            string name = match.Groups["name"].ToString().Trim();
            string features = match.Groups["features"].ToString().Trim();
            string mac = match.Groups["mac"].ToString().Trim();
            string crystal = match.Groups["crystal"].ToString().Trim();
            string manufacturer = match.Groups["manufacturer"].ToString().Trim();
            string device = match.Groups["device"].ToString().Trim();
            string size = match.Groups["size"].ToString().Trim();

            // collect and return all information
            // try to convert the flash size into bytes
            string unit = size.Substring(size.Length - 2).ToUpperInvariant();

            if (int.TryParse(size.Remove(size.Length - 2), out _flashSize))
            {
                _flashSize *= unit switch
                {
                    "MB" => 0x100000,
                    "KB" => 0x400,
                    _ => 1,
                };
            }
            else
            {
                throw new EspToolExecutionException("Can't read flash size from device");
            }

            // update chip type
            // lower case, no hifen
            _chipType = chipType.ToLower().Replace("-", "");

            return new Esp32DeviceInfo(
                chipType,
                name,
                features,
                crystal,
                mac.ToUpperInvariant(),
                byte.Parse(manufacturer, NumberStyles.AllowHexSpecifier),
                short.Parse(device, NumberStyles.HexNumber),
                _flashSize);
        }

        /// <summary>
        /// Backup the entire flash into a bin file
        /// </summary>
        /// <param name="backupFilename">Backup file including full path</param>
        /// <param name="flashSize">Flash size in bytes</param>
        /// <returns>true if successful</returns>
        internal void BackupFlash(string backupFilename,
            int flashSize)
        {
            // execute read_flash command and parse the result; progress message can be found be searching for backspaces (ASCII code 8)
            if (!RunEspTool(
                $"read_flash 0 0x{flashSize:X} \"{backupFilename}\"",
                false,
                false,
                false,
                (char)8,
                out string messages))
            {
                throw new ReadEsp32FlashException(messages);
            }

            var match = Regex.Match(messages, "(?<message>Read .*)(.*?\n)*");
            if (!match.Success)
            {
                throw new ReadEsp32FlashException(messages);
            }

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine(match.Groups["message"].ToString().Trim());
            }
        }

        /// <summary>
        /// Erase the entire flash of the ESP32 chip.
        /// </summary>
        /// <returns>true if successful</returns>
        internal ExitCodes EraseFlash()
        {
            // execute erase_flash command and parse the result
            if (!RunEspTool(
                "erase_flash",
                false,
                false,
                false,
                null,
                out string messages))
            {
                throw new EraseEsp32FlashException(messages);
            }

            var match = Regex.Match(messages, "(?<message>Chip erase completed successfully.*)(.*?\n)*");
            if (!match.Success)
            {
                throw new EraseEsp32FlashException(messages);
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Erase flash segment on the ESP32 chip.
        /// </summary>
        /// <returns>true if successful</returns>
        internal ExitCodes EraseFlashSegment(uint startAddress, uint length)
        {
            // startAddress and length must both be multiples of the SPI flash erase sector size. This is 0x1000 (4096) bytes for supported flash chips.
            // esptool takes care of validating this so no need to perform any sanity check before executing the command

            // execute erase_flash command and parse the result
            if (!RunEspTool(
                $"erase_region 0x{startAddress:X} 0x{length:X}",
                false,
                false,
                false,
                null,
                out string messages))
            {
                throw new EraseEsp32FlashException(messages);
            }

            var match = Regex.Match(messages, "(?<message>Erase completed successfully.*)(.*?\n)*");
            if (!match.Success)
            {
                throw new EraseEsp32FlashException(messages);
            }

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine(match.Groups["message"].ToString().Trim());
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Write to the flash
        /// </summary>
        /// <param name="partsToWrite">dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</param>
        /// <returns>true if successful</returns>
        internal ExitCodes WriteFlash(Dictionary<int, string> partsToWrite)
        {
            // put the parts to flash together and prepare the regex for parsing the output
            var partsArguments = new StringBuilder();
            var regexPattern = new StringBuilder();
            int counter = 1;
            var regexGroupNames = new List<string>();

            foreach (var part in partsToWrite)
            {
                // start address followed by filename
                partsArguments.Append($"0x{part.Key:X} \"{part.Value}\" ");
                // test for message in output
                regexPattern.Append($"(?<wrote{counter}>Wrote.*[\r\n]*Hash of data verified.)(.*?[\r\n]*)*");
                regexGroupNames.Add($"wrote{counter}");
                counter++;
            }

            // if flash size was detected already use it for the --flash_size parameter; otherwise use the default "detect"
            string flashSize = _flashSize switch
            {
                >= 0x100000 => $"{_flashSize / 0x100000}MB",
                > 0 => $"{_flashSize / 0x400}KB",
                _ => "detect",
            };

            // execute write_flash command and parse the result; progress message can be found be searching for linefeed
            if (!RunEspTool(
                $"write_flash --flash_mode {_flashMode} --flash_freq {_flashFrequency}m --flash_size {flashSize} {partsArguments.ToString().Trim()}",
                false,
                false,
                true,
                '\r',
                out string messages))
            {
                throw new WriteEsp32FlashException(messages);
            }

            var match = Regex.Match(messages, regexPattern.ToString());
            if (!match.Success)
            {
                throw new WriteEsp32FlashException(messages);
            }

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                foreach (string groupName in regexGroupNames)
                {
                    Console.WriteLine(match.Groups[groupName].ToString().Trim());
                }
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Run the esptool one time
        /// </summary>
        /// <param name="commandWithArguments">the esptool command (e.g. write_flash) incl. all arguments (if needed)</param>
        /// <param name="noStub">if true --no-stub will be added; the chip_id, read_mac and flash_id commands can be quicker executes without uploading the stub program to the chip</param>
        /// <param name="hardResetAfterCommand">if true the chip will execute a hard reset via DTR signal</param>
        /// <param name="progressTestChar">If not null: After each of this char a progress message will be printed out</param>
        /// <param name="messages">StandardOutput and StandardError messages that the esptool prints out</param>
        /// <returns>true if the esptool exit code was 0; false otherwise</returns>
        private bool RunEspTool(
            string commandWithArguments, 
            bool noStub,
            bool useStandardBaudRate,
            bool hardResetAfterCommand, 
            char? progressTestChar, 
            out string messages)
        {
            // create the process start info
            // if we can directly talk to the ROM bootloader without a stub program use the --no-stub option
            // --nostub requires to not change the baudrate (ROM doesn't support changing baud rate. Keeping initial baud rate 115200)
            string noStubParameter = null;
            string baudRateParameter = null;
            string beforeParameter = null;
            string afterParameter = hardResetAfterCommand ? "hard_reset" : "no_reset";

            if (noStub)
            {
                // using no stub and can't change the baud rate
                noStubParameter = "--no-stub";
            }
            else
            {
                if (!useStandardBaudRate)
                {
                    // using the stub that supports changing the baudrate
                    baudRateParameter = $"--baud {_baudRate}";
                }
            }

            // prepare the process start of the esptool
            Process espTool = new Process();
            string parameter = $"--port {_serialPort} {baudRateParameter} --chip {_chipType} {noStubParameter} {beforeParameter} --after {afterParameter} {commandWithArguments}";
            espTool.StartInfo = new ProcessStartInfo(Path.Combine(Program.ExecutingPath, "esptool", "esptool.exe"), parameter)
            {
                WorkingDirectory = Path.Combine(Program.ExecutingPath, "esptool"),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            // start esptool and wait for exit
            if (!espTool.Start())
            {
                throw new EspToolExecutionException("Error starting esptool!");
            }

            var messageBuilder = new StringBuilder();

            // reset these
            connectPromptShown = false;
            connectPatternFound = false;
            connectTimeStamp = DateTime.UtcNow;

            // showing progress is a little bit tricky
            if (Verbosity > VerbosityLevel.Quiet)
            {
                if (progressTestChar.HasValue)
                {
                    // need to look for progress test char

                    // loop until esptool exit
                    while (!espTool.HasExited)
                    {
                        // loop until there is no next char to read from standard output
                        while (true)
                        {
                            int next = espTool.StandardOutput.Read();
                            if (next != -1)
                            {
                                // append the char to the message buffer
                                messageBuilder.Append((char)next);

                                // try to find a progress message
                                string progress = FindProgress(messageBuilder, progressTestChar.Value);
                                if (progress != null && Verbosity > VerbosityLevel.Quiet)
                                {
                                    // print progress and set the cursor to the beginning of the line (\r)
                                    Console.Write(progress);
                                    Console.Write("\r");
                                }

                                ProcessConnectPattern(messageBuilder);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    // collect the last messages
                    messageBuilder.AppendLine(espTool.StandardOutput.ReadToEnd());
                    messageBuilder.Append(espTool.StandardError.ReadToEnd());
                }
                else
                {
                    // when not looking for progress char, look for connect pattern
                    
                    // loop until esptool exit
                    while (!espTool.HasExited)
                    {
                        // loop until there is no next char to read from standard output
                        while (true)
                        {
                            int next = espTool.StandardOutput.Read();
                            if (next != -1)
                            {
                                // append the char to the message buffer
                                messageBuilder.Append((char)next);

                                ProcessConnectPattern(messageBuilder);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    // collect the last messages
                    messageBuilder.AppendLine(espTool.StandardOutput.ReadToEnd());
                    messageBuilder.Append(espTool.StandardError.ReadToEnd());
                }
            }
            else
            {
                // collect all messages
                messageBuilder.AppendLine(espTool.StandardOutput.ReadToEnd());
                messageBuilder.Append(espTool.StandardError.ReadToEnd());
            }

            messages = messageBuilder.ToString();

            // if the stub program was used then we don't need to transfer ist again
            _isStubActive = !noStub;

            // true if exit code was 0 (success)
            return espTool.ExitCode == 0;
        }

        private void ProcessConnectPattern(StringBuilder messageBuilder)
        {
            // try to find a connect pattern
            connectPatternFound = FindConnectPattern(messageBuilder);

            var timeToConnect = DateTime.UtcNow.Subtract(connectTimeStamp).TotalSeconds;

            // if esptool is struggling to connect for more than 5 seconds

            // prompt user
            if (!connectPromptShown &&
                connectPatternFound && 
                timeToConnect > 5)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;

                Console.WriteLine("*** Hold down the BOOT/FLASH button in ESP32 board ***");

                Console.ForegroundColor = ConsoleColor.White;

                // set flag
                connectPromptShown = true;
            }
        }

        private bool FindConnectPattern(StringBuilder messageBuilder)
        {
            if (messageBuilder.Length > 2)
            {
                var previousChar = messageBuilder[messageBuilder.Length - 2];
                var newChar = messageBuilder[messageBuilder.Length - 1];

                // don't look for double dot (..) sequence so it doesn't mistake it with an ellipsis (...)
                return ((previousChar == '.'
                         && newChar == '_') ||
                        (previousChar == '_'
                         && newChar == '_') ||
                        (previousChar == '_'
                         && newChar == '.'));
            }

            return false;
        }

        /// <summary>
        /// Try to find a progress message in the esptool output
        /// </summary>
        /// <param name="messageBuilder">esptool output</param>
        /// <param name="progressTestChar">search char for the progress message delimiter (backspace or linefeed)</param>
        /// <returns></returns>
        private string FindProgress(
            StringBuilder messageBuilder, 
            char progressTestChar)
        {
            // search for the given char (backspace or linefeed)
            // only if we have 100 chars at minimum and only if the last char is the test char
            if (messageBuilder.Length > 100 && 
                messageBuilder[messageBuilder.Length - 1] == progressTestChar && 
                messageBuilder[messageBuilder.Length - 2] != progressTestChar)
            {
                // trim the test char and convert \r\n into \r
                string progress = messageBuilder.ToString().Trim(progressTestChar).Replace("\r\n", "\r");
                // another test char in the message?
                int delimiter = progress.LastIndexOf(progressTestChar);
                if (delimiter > 0)
                {
                    // then we found a progress message; pad the message to 110 chars because no message is longer than 110 chars
                    return progress.Substring(delimiter + 1).PadRight(110);
                }
            }
 
            // no progress message found
            return null;
        }
    }
}
