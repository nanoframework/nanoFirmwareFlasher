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
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class the handles all the calls to the esptool.exe.
    /// </summary>
    public partial class EspTool
    {
        private string _esptoolMessage;

        /// <summary>
        /// The serial port over which all the communication goes
        /// </summary>
        private readonly string _serialPort = null;

        /// <summary>
        /// The baud rate for the serial port. The default comming from <see cref="Options.BaudRate"/>.
        /// </summary>
        private int _baudRate = 0;

        /// <summary>
        /// Partition table size, when specified in the options.
        /// </summary>
        private readonly PartitionTableSize? _partitionTableSize = null;

        /// <summary>
        /// The size of the flash in bytes; 4 MB = 0x40000 bytes
        /// </summary>
        private int _flashSize = -1;

        private bool connectPatternFound;

        private DateTime connectTimeStamp;

        private bool connectPromptShown;

        /// <summary>
        /// This property is <see langword="true"/> if the specified COM port is valid.
        /// </summary>
        public bool ComPortAvailable => !string.IsNullOrEmpty(_serialPort);

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; set; }

        /// <summary>
        /// Flag to report if the target couldn't be reset after flashing it.
        /// </summary>
        public bool CouldntResetTarget;

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
        /// <param name="verbosity">The verbosity level of messages</param>
        public EspTool(
            string serialPort,
            int baudRate,
            string flashMode,
            int flashFrequency,
            PartitionTableSize? partitionTableSize,
            VerbosityLevel verbosity)
        {
            Verbosity = verbosity;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // open/close the COM port to check if it is available
                var test = new SerialPort(serialPort, baudRate);

                try
                {
                    test.Open();
                    test.Close();
                }
                catch (Exception ex)
                {
                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;

                        Console.WriteLine("");
                        Console.WriteLine("******************** EXCEPTION ******************");
                        Console.WriteLine($"Exception occurred while trying to open <{serialPort}>:");
                        Console.WriteLine($"{ex.Message}");
                        Console.WriteLine("*************************************************");
                        Console.WriteLine("");

                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    // presume any exception here is caused by the serial not existing or not possible to open
                    throw new EspToolExecutionException();
                }
            }
            else
            {
                if (!File.Exists(serialPort))
                {
                    throw new EspToolExecutionException();
                }
            }

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                Console.WriteLine($"Using {serialPort} @ {baudRate} baud to connect to ESP32.");
            }

            // set properties
            _serialPort = serialPort;
            _baudRate = baudRate;
            _partitionTableSize = partitionTableSize;
        }

        /// <summary>
        /// Tries reading ESP32 device details.
        /// </summary>
        /// <returns>The filled info structure with all the information about the connected ESP32 device or null if an error occured</returns>
        public Esp32DeviceInfo GetDeviceDetails(
            string targetName,
            bool requireFlashSize = true)
        {
            string messages;

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"Reading details from chip...");
            }

            // execute flash_id command and parse the result
            if (!RunEspTool(
                "flash_id",
                false,
                true,
                false,
                null,
                out messages))
            {
                if(messages.Contains("A fatal error occurred: Failed to connect to Espressif device: No serial data received."))
                {
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("");
                    Console.WriteLine("Can't connect to ESP32 bootloader. Try to put the board in bootloader manually.");
                    Console.WriteLine("For troubleshooting steps visit: https://docs.espressif.com/projects/esptool/en/latest/troubleshooting.html.");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;
                }

                throw new EspToolExecutionException(messages);
            }

            // check if we got flash size (in case we need it)
            if (requireFlashSize
                && messages.Contains("Detected flash size: Unknown"))
            {
                // try again now without the stub
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
            }

            var match = Regex.Match(messages, $"(Detecting chip type... )(?<type>[ESP32\\-ICOCH]+)(.*?[\r\n]*)*(Chip is )(?<name>.*)(.*?[\r\n]*)*(Features: )(?<features>.*)(.*?[\r\n]*)*(Crystal is )(?<crystal>.*)(.*?[\r\n]*)*(MAC: )(?<mac>.*)(.*?[\r\n]*)*(Manufacturer: )(?<manufacturer>.*)(.*?[\r\n]*)*(Device: )(?<device>.*)(.*?[\r\n]*)*(Detected flash size: )(?<size>.*)");
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

            // try to find out if PSRAM is present
            PSRamAvailability psramIsAvailable = PSRamAvailability.Undetermined;

            if (name.Contains("PICO"))
            {
                // PICO's don't have PSRAM, so don't even bother
                psramIsAvailable = PSRamAvailability.No;
            }
            else if (name.Contains("ESP32-S2")
                     && targetName == "FEATHER_S2")
            {
                // FEATHER_S2's have PSRAM, so don't even bother
                psramIsAvailable = PSRamAvailability.Yes;
            }
            else if (name.Contains("ESP32-C3")
                     || name.Contains("ESP32-S3"))
            {
                // all ESP32-C3/S3 SDK config have support for PSRAM, so don't even bother
                psramIsAvailable = PSRamAvailability.Undetermined;
            }
            else
            {
                // if a target name wasn't provided, check for PSRAM
                // except for ESP32_C3 and S3
                if (targetName == null
                    && !name.Contains("ESP32-C3")
                    && !name.Contains("ESP32-S3"))
                {
                    psramIsAvailable = FindPSRamAvailable();
                }
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OK");
                Console.ForegroundColor = ConsoleColor.White;
            }

            return new Esp32DeviceInfo(
                chipType,
                name,
                features,
                crystal,
                mac.ToUpperInvariant(),
                byte.Parse(manufacturer, NumberStyles.AllowHexSpecifier),
                short.Parse(device, NumberStyles.HexNumber),
                _flashSize,
                psramIsAvailable);
        }

        /// <summary>
        /// Perform detection of PSRAM availability on connected device.
        /// </summary>
        /// <returns>Information about availability of PSRAM, if that was possible to determine.</returns>
        private PSRamAvailability FindPSRamAvailable()
        {
            PSRamAvailability pSRamAvailability = PSRamAvailability.Undetermined;

            // don't want to output anything from esptool
            // backup current verbosity setting
            var bkpVerbosity = Verbosity;
            Verbosity = VerbosityLevel.Quiet;

            // compose bootloader partition
            var bootloaderPartition = new Dictionary<int, string>
            {
                // bootloader goes to 0x1000, except for ESP32_C3 and ESP32_S3, which goes to 0x0
				{ _chipType == "esp32c3" || _chipType == "esp32s3" ? 0x0 : 0x1000, Path.Combine(Utilities.ExecutingPath, $"{_chipType}bootloader", "bootloader.bin") },

				// nanoCLR goes to 0x10000
				{ 0x10000, Path.Combine(Utilities.ExecutingPath, $"{_chipType}bootloader", "test_startup.bin") },

                // partition table goes to 0x8000; there are partition tables for 2MB, 4MB, 8MB and 16MB flash sizes
				{ 0x8000, Path.Combine(Utilities.ExecutingPath, $"{_chipType}bootloader", $"partitions_{Esp32DeviceInfo.GetFlashSizeAsString(_flashSize).ToLowerInvariant()}.bin") }
            };

            // need to use standard baud rate here because of boards put in download mode
            if (WriteFlash(bootloaderPartition, true) == ExitCodes.OK)
            {
                // check if the
                if (_esptoolMessage.Contains("esptool.py can not exit the download mode over USB"))
                {
                    // this board was put on download mode manually, can't run the test app...

                    return PSRamAvailability.Undetermined;
                }

                try
                {
                    // open COM port and grab output
                    // force baud rate to 115200 (standard baud rate for boootloader)
                    SerialPort espDevice = new SerialPort(_serialPort, 115200);
                    espDevice.Open();

                    if (espDevice.IsOpen)
                    {
                        // wait 2 seconds... 
                        Thread.Sleep(TimeSpan.FromSeconds(2));

                        // ... read output from bootloader
                        var bootloaderOutput = espDevice.ReadExisting();

                        espDevice.Close();

                        // find magic string
                        if (bootloaderOutput.Contains("PSRAM initialized"))
                        {
                            pSRamAvailability = PSRamAvailability.Yes;
                        }
                        else
                        {
                            pSRamAvailability = PSRamAvailability.No;
                        }
                    }
                }
                catch
                {
                    // don't care about any exceptions 
                }
            }

            // restore verbosity setting
            Verbosity = bkpVerbosity;

            return pSRamAvailability;
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
                true,
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
        /// Backup the entire flash into a bin file
        /// </summary>
        /// <param name="backupFilename">Backup file including full path</param>
        /// <param name="address">Start address of the config partition.</param>
        /// <param name="size">Size of the config partition.</param>
        /// <returns>true if successful</returns>
        internal ExitCodes BackupConfigPartition(
            string backupFilename,
            int address,
            int size)
        {
            // execute dump_mem  command and parse the result; progress message can be found be searching for backspaces (ASCII code 8)
            if (!RunEspTool(
                $"read_flash 0x{address:X} 0x{size:X} \"{backupFilename}\"",
                false,
                true,
                false,
                null,
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

            return ExitCodes.OK;
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
                true,
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
        /// <param name="useStandardBaudrate">Use the standard baud rate (default is false).</param>
        /// <returns>true if successful</returns>
        internal ExitCodes WriteFlash(
            Dictionary<int, string> partsToWrite,
            bool useStandardBaudrate = false)
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
                $"write_flash --flash_size {flashSize} {partsArguments.ToString().Trim()}",
                false,
                useStandardBaudrate,
                true,
                '\r',
                out string messages))
            {
                throw new WriteEsp32FlashException(messages);
            }

            // check if there is any mention of not being able to run the app
            CouldntResetTarget = messages.Contains("To run the app, reset the chip manually");

            return ExitCodes.OK;
        }

        /// <summary>
        /// Run the esptool one time
        /// </summary>
        /// <param name="commandWithArguments">the esptool command (e.g. write_flash) incl. all arguments (if needed)</param>
        /// <param name="noStub">if true --no-stub will be added; the chip_id, read_mac and flash_id commands can be quicker executes without uploading the stub program to the chip</param>
        /// <param name="useStandardBaudRate">If <see langword="true"/> the tool will use the standard baud rate to connect to the chip.</param>
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
            // reset message
            _esptoolMessage = string.Empty;

            // create the process start info
            // if we can directly talk to the ROM bootloader without a stub program use the --no-stub option
            // --nostub requires to not change the baudrate (ROM doesn't support changing baud rate. Keeping initial baud rate 115200)
            string noStubParameter = null;
            string baudRateParameter = null;
            string beforeParameter = null;
            string afterParameter = hardResetAfterCommand ? "hard_reset" : "no_reset_stub";

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
            string appName;
            string appDir;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                appName = "esptool.exe";
                appDir = Path.Combine(Utilities.ExecutingPath, "esptool", "esptoolWin");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                appName = "esptool";
                appDir = Path.Combine(Utilities.ExecutingPath, "esptool", "esptoolMac");
            }
            else
            {
                appName = "esptool";
                appDir = Path.Combine(Utilities.ExecutingPath, "esptool", "esptoolLinux");
                Process espToolExex = new Process();
                // Making sure the esptool is executable
                espToolExex.StartInfo = new ProcessStartInfo("chmod", $"+x {Path.Combine(appDir, appName)}")
                {
                    WorkingDirectory = appDir,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                if (!espToolExex.Start())
                {
                    throw new EspToolExecutionException("Error changing permissions for esptool!");
                }

                while (!espToolExex.HasExited)
                {
                    Thread.Sleep(10);
                }
            }

            Process espTool = new Process();
            string parameter = $"--port {_serialPort} {baudRateParameter} --chip {_chipType} {noStubParameter} {beforeParameter} --after {afterParameter} {commandWithArguments}";
            espTool.StartInfo = new ProcessStartInfo(Path.Combine(appDir, appName), parameter)
            {
                WorkingDirectory = appDir,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };


            if (Verbosity == VerbosityLevel.Diagnostic)
            {
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine("");
                Console.WriteLine("Executing esptool with the following parameters:");
                Console.WriteLine($"'{parameter}'");
                Console.WriteLine("");
            }

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
            var progressStarted = false;

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
                                if (progress != null && Verbosity >= VerbosityLevel.Detailed)
                                {
                                    if (!progressStarted)
                                    {
                                        // need to print the first line of the progress message
                                        Console.Write("\r");

                                        progressStarted = true;
                                    }

                                    // print progress... and set the cursor to the beginning of the line (\r)
                                    Console.Write(progress);
                                    Console.Write("\r");
                                }

                                ProcessConnectPattern(messageBuilder);
                            }
                            else
                            {
                                if (Verbosity >= VerbosityLevel.Detailed)
                                {
                                    // need to clear all progress lines
                                    for (int i = 0; i < messageBuilder.Length; i++)
                                    {
                                        Console.Write("\b");
                                    }
                                }

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

            // save output messages
            _esptoolMessage = messages;

            if (espTool.ExitCode == 0)
            {
                // exit code was 0 (success), all good
                return true;
            }
            else
            {
                // need to look for specific error messages to do a safe guess if execution is as expected
                if (messages.Contains("esptool.py can not exit the download mode over USB") ||
                   messages.Contains("Staying in bootloader."))
                {
                    // we are probably good with this as we can't do much about it...
                    return true;
                }
                else
                {
                    return false;
                }
            }
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

                // trim initial message with device features
                int startIndex = progress.LastIndexOf("MAC:");

                // another test char in the message?
                int delimiter = progress.LastIndexOf(progressTestChar, progress.Length - 1);
                if (startIndex > 0
                    && delimiter > 0
                    && delimiter > startIndex)
                {
                    //var nextDelimiter = progress.LastIndexOf(progressTestChar, delimiter);
                    // then we found a progress message; pad the message to 110 chars because no message is longer than 110 chars
                    return progress.Substring(delimiter + 1).PadRight(110);
                }
            }

            // no progress message found
            return null;
        }
    }
}
