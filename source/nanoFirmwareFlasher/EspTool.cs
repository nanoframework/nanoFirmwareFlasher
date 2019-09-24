//
// Copyright (c) 2019 The nanoFramework project contributors
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
    internal class EspTool
    {
        /// <summary>
        /// The serial port over which all the communication goes
        /// </summary>
        private readonly string _serialPort = null;

        /// <summary>
        /// The baud rate for the serial port; 921600 baud is the default
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
        /// The size of the flash in bytes; 4 MB = 0x40000 bytes
        /// </summary>
        private int _flashSize = -1;

        /// <summary>
        /// true if the stub program is already active and we can use the --before no_reset_no_sync parameter 
        /// </summary>
        private bool _isStubActive = false;

        /// <summary>
        /// This property is <see langword="true"/> if the specified COM port is valid.
        /// </summary>
        internal bool ComPortAvailable => !string.IsNullOrEmpty(_serialPort);

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; internal set; } = VerbosityLevel.Normal;

        /// <summary>
        /// Structure for holding the information about the connected ESP32 together
        /// </summary>
        internal struct DeviceInfo
        {
            /// <summary>
            /// Version of the esptool.py
            /// </summary>
            internal Version ToolVersion { get; private set; }

            /// <summary>
            /// Name of the ESP32 chip
            /// </summary>
            internal string ChipName { get; private set; }

            /// <summary>
            /// ESP32 chip features
            /// </summary>
            internal string Features { get; private set; }

            /// <summary>
            /// MAC address of the ESP32 chip
            /// </summary>
            internal PhysicalAddress MacAddress { get; private set; }

            /// <summary>
            /// Flash manufacturer ID.
            /// </summary>
            /// <remarks>
            /// See http://code.coreboot.org/p/flashrom/source/tree/HEAD/trunk/flashchips.h for more details.
            /// </remarks>
            internal byte FlashManufacturerId { get; private set; }

            /// <summary>
            /// Flash device type ID.
            /// </summary>
            /// <remarks>
            /// See http://code.coreboot.org/p/flashrom/source/tree/HEAD/trunk/flashchips.h for more details.
            /// </remarks>
            internal short FlashDeviceModelId { get; private set; }

            /// <summary>
            /// The size of the flash in bytes; 4 MB = 0x40000 bytes
            /// </summary>
            internal int FlashSize { get; private set; }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="toolVersion">Version of the esptool.py</param>
            /// <param name="features">ESP32 chip features</param>
            /// <param name="macAddress">MAC address of the ESP32 chip</param>
            /// <param name="flashManufacturerId">Flash manufacturer ID</param>
            /// <param name="flashDeviceModelId">Flash device type ID</param>
            /// <param name="flashSize">The size of the flash in bytes</param>
            internal DeviceInfo(
                Version toolVersion, 
                string chipName, 
                string features, 
                PhysicalAddress macAddress, 
                byte flashManufacturerId, 
                short flashDeviceModelId, 
                int flashSize)
            {
                ToolVersion = toolVersion;
                ChipName = chipName;
                Features = features;
                MacAddress = macAddress;
                FlashManufacturerId = flashManufacturerId;
                FlashDeviceModelId = flashDeviceModelId;
                FlashSize = flashSize;
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="serialPort">The serial port over which all the communication goes.</param>
        /// <param name="baudRate">The baud rate for the serial port.</param>
        /// <param name="flashMode">The flash mode for the esptool</param>
        /// <param name="flashFrequency">The flash frequency for the esptool</param>
        internal EspTool(
            string serialPort, 
            int baudRate,
            string flashMode, 
            int flashFrequency)
        {
            // open/close the port to see if it is available
            using (SerialPort test = new SerialPort(serialPort, baudRate))
            {
                try
                {
                    test.Open();
                    test.Close();
                }
                catch(IOException)
                {
                    // presume any IOException here is caused by the serial not existing or not possible to open
                    return;
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
        }

        /// <summary>
        /// Tests the connection to the ESP32 chip.
        /// </summary>
        /// <returns>The filled info structure with all the information about the connected ESP32 chip or null if an error occured</returns>
        internal DeviceInfo TestChip()
        {
            // execute read_mac command and parse the result
            if (!RunEspTool("read_mac", true, false, null, out string messages))
            {
                throw new EspToolExecutionException(messages);
            }

            Match match = Regex.Match(messages, "(esptool.py v)(?<version>[0-9.]+)(.*?[\r\n]*)*(Chip is )(?<name>.*)(.*?[\r\n]*)*(Features: )(?<features>.*)(.*?[\r\n]*)*(MAC: )(?<mac>.*)");
            if (!match.Success)
            {
                throw new EspToolExecutionException(messages);
            }

            // that gives us the version of the esptool.py, the chip name and the MAC address
            string version = match.Groups["version"].ToString().Trim();
            string name = match.Groups["name"].ToString().Trim();
            string features = match.Groups["features"].ToString().Trim();
            string mac = match.Groups["mac"].ToString().Trim();

            if (Verbosity >= VerbosityLevel.Diagnostic)
            {
                Console.WriteLine($"Executed esptool.py version {version}");
            }

            // execute flash_id command and parse the result
            if (!RunEspTool("flash_id", false, false, null, out messages))
            {
                throw new EspToolExecutionException(messages);
            }

            match = Regex.Match(messages, $"(Manufacturer: )(?<manufacturer>.*)(.*?[\r\n]*)*(Device: )(?<device>.*)(.*?[\r\n]*)*(Detected flash size: )(?<size>.*)");
            if (!match.Success)
            {
                throw new EspToolExecutionException(messages);
            }

            // that gives us the flash manufacturer, flash device type ID and flash size
            string manufacturer = match.Groups["manufacturer"].ToString().Trim();
            string device = match.Groups["device"].ToString().Trim();
            string size = match.Groups["size"].ToString().Trim();

            // collect and return all information
            // convert the flash size into bytes
            string unit = size.Substring(size.Length - 2).ToUpperInvariant();
            _flashSize = int.Parse(size.Remove(size.Length - 2)) * (unit == "MB" ? 0x100000 : unit == "KB" ? 0x400 : 1);

            return new DeviceInfo(
                new Version(version),
                name,
                features,
                PhysicalAddress.Parse(mac.Replace(':', '-').ToUpperInvariant()),
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
        internal ExitCodes BackupFlash(
            string backupFilename,
            int flashSize)
        {
            // execute read_flash command and parse the result; progress message can be found be searching for backspaces (ASCII code 8)
            if (!RunEspTool($"read_flash 0 0x{flashSize:X} \"{backupFilename}\"", false, false, (char)8, out string messages))
            {
                throw new ReadEsp32FlashException(messages);
            }

            Match match = Regex.Match(messages, "(?<message>Read .*)(.*?\n)*");
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
            if (!RunEspTool("erase_flash", false, false, null, out string messages))
            {
                throw new EraseEsp32FlashException(messages);
            }

            Match match = Regex.Match(messages, "(?<message>Chip erase completed successfully.*)(.*?\n)*");
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
            StringBuilder partsArguments = new StringBuilder();
            StringBuilder regexPattern = new StringBuilder();
            int counter = 1;
            List<string> regexGroupNames = new List<string>();

            foreach (KeyValuePair<int, string> part in partsToWrite)
            {
                // start address followed by filename
                partsArguments.Append($"0x{part.Key:X} \"{part.Value}\" ");
                // test for message in output
                regexPattern.Append($"(?<wrote{counter}>Wrote.*[\r\n]*Hash of data verified.)(.*?[\r\n]*)*");
                regexGroupNames.Add($"wrote{counter}");
                counter++;
            }

            // if flash size was detected already use it for the --flash_size parameter; otherwise use the default "detect"
            string flashSize = "detect";
            if (_flashSize >= 0x100000)
            {
                flashSize = $"{_flashSize / 0x100000}MB";
            }
            else if (_flashSize > 0)
            {
                flashSize = $"{_flashSize / 0x400}KB";
            }

            // execute write_flash command and parse the result; progress message can be found be searching for linefeed
            if (!RunEspTool($"write_flash --flash_mode {_flashMode} --flash_freq {_flashFrequency}m --flash_size {flashSize} {partsArguments.ToString().Trim()}", false, true, '\r', out string messages))
            {
                throw new WriteEsp32FlashException(messages);
            }

            Match match = Regex.Match(messages, regexPattern.ToString());
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
                // using the stub that supports changing the baudrate
                baudRateParameter = $"--baud {_baudRate}";
            }

            // prepare the process start of the esptool
            Process espTool = new Process();
            string parameter = $"--port {_serialPort} {baudRateParameter} --chip esp32 {noStubParameter} {beforeParameter} --after {afterParameter} {commandWithArguments}";
            espTool.StartInfo = new ProcessStartInfo(Path.Combine(Program.ExecutingPath, "esptool", "esptool.exe"), parameter)
            {
                WorkingDirectory = Path.Combine(Program.ExecutingPath, "esptool"),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            // start esptool and wait for exit
            if (espTool.Start())
            {
                // if no progress output needed wait unlimited time until esptool exit
                if (Verbosity < VerbosityLevel.Detailed &&
                    !progressTestChar.HasValue)
                {
                    espTool.WaitForExit();
                }
            }
            else
            {
                throw new EspToolExecutionException("Error starting esptool!");
            }

            StringBuilder messageBuilder = new StringBuilder();

            // showing progress is a little bit tricky
            if (progressTestChar.HasValue && Verbosity >= VerbosityLevel.Detailed)
            {
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
                            char nextChar = (char)next;
                            messageBuilder.Append((char)next);
                            // try to find a progress message
                            string progress = FindProgress(messageBuilder, progressTestChar.Value);
                            if (progress != null)
                            {
                                // print progress and set the cursor to the beginning of the line (\r)
                                Console.Write(progress);
                                Console.Write("\r");
                            }
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
            if (messageBuilder.Length > 100 && messageBuilder[messageBuilder.Length - 1] == progressTestChar && messageBuilder[messageBuilder.Length - 2] != progressTestChar)
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
