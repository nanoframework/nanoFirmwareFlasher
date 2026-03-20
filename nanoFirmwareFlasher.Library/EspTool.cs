// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using nanoFramework.Tools.FirmwareFlasher.Esp32Serial;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Native C# implementation of ESP32 serial bootloader communication.
    /// Replaces the former process-wrapper around the external esptool binary.
    /// </summary>
    public partial class EspTool : IDisposable
    {
        /// <summary>
        /// The serial port over which all the communication goes.
        /// </summary>
        private readonly string _serialPort = null;

        /// <summary>
        /// The baud rate for the serial port.
        /// </summary>
        private int _baudRate = 0;

        /// <summary>
        /// Partition table size, when specified in the options.
        /// </summary>
        private readonly PartitionTableSize? _partitionTableSize = null;

        /// <summary>
        /// The size of the flash in bytes; 4 MB = 0x400000 bytes.
        /// </summary>
        private int _flashSize = -1;

        /// <summary>
        /// Flash mode setting (e.g. "dio", "qio", "dout", "qout").
        /// Used to patch the bootloader image header before flashing.
        /// </summary>
        private readonly string _flashMode;

        /// <summary>
        /// Flash frequency in MHz (e.g. 40, 80).
        /// Used to patch the bootloader image header before flashing.
        /// </summary>
        private readonly int _flashFrequency;

        /// <summary>
        /// Native bootloader protocol client.
        /// </summary>
        private Esp32BootloaderClient _client;

        /// <summary>
        /// Chip detector for reading device info from registers.
        /// </summary>
        private Esp32ChipDetector _chipDetector;

        /// <summary>
        /// Flash controller for read/write/erase operations.
        /// </summary>
        private Esp32FlashController _flashController;

        /// <summary>
        /// Whether the client is currently connected to the bootloader.
        /// </summary>
        private bool _isConnected;

        /// <summary>
        /// Whether the stub loader has been uploaded to the chip.
        /// </summary>
        private bool _stubUploaded;

        private bool _disposed;

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
        /// <param name="flashMode">The flash mode (e.g. "dio", "qio"). Used to patch bootloader header.</param>
        /// <param name="flashFrequency">The flash frequency in MHz (e.g. 40, 80). Used to patch bootloader header.</param>
        /// <param name="partitionTableSize">Partition table size to use.</param>
        /// <param name="verbosity">The verbosity level of messages.</param>
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
                        OutputWriter.ForegroundColor = ConsoleColor.DarkRed;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine("******************** EXCEPTION ******************");
                        OutputWriter.WriteLine($"Exception occurred while trying to open <{serialPort}>:");
                        OutputWriter.WriteLine($"{ex.Message}");
                        OutputWriter.WriteLine("*************************************************");
                        OutputWriter.WriteLine("");

                        OutputWriter.ForegroundColor = ConsoleColor.White;
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
                OutputWriter.WriteLine($"Using {serialPort} @ {baudRate} baud to connect to ESP32.");
            }

            // set properties
            _serialPort = serialPort;
            _baudRate = baudRate;
            _flashMode = flashMode;
            _flashFrequency = flashFrequency;
            _partitionTableSize = partitionTableSize;
        }

        /// <summary>
        /// Ensure the bootloader client is connected. Creates and connects if needed.
        /// After connection, attempts to upload the stub loader for faster operations.
        /// </summary>
        /// <param name="useStandardBaudrate">If true, stay at 115200 baud (skip baud rate change).</param>
        private void EnsureConnected(bool useStandardBaudrate = false)
        {
            if (_isConnected && _client != null)
            {
                // If caller needs standard baud rate and we're currently at a higher rate, change back.
                if (useStandardBaudrate && _stubUploaded && _client.CurrentBaudRate != Esp32BootloaderClient.DefaultBaudRate)
                {
                    _client.ChangeBaudRate(Esp32BootloaderClient.DefaultBaudRate);
                }

                return;
            }

            _client = new Esp32BootloaderClient(_serialPort, verbosity: Verbosity);
            _client.Connect();

            _chipDetector = new Esp32ChipDetector(_client);
            _isConnected = true;

            // Try to upload the stub loader for faster operations
            if (_chipType != "auto")
            {
                TryUploadStub(useStandardBaudrate);
            }
        }

        /// <summary>
        /// Disconnect the current bootloader session and release the serial port.
        /// </summary>
        private void Disconnect()
        {
            _flashController = null;
            _chipDetector = null;
            _stubUploaded = false;

            try
            {
                _client?.Dispose();
            }
            finally
            {
                _client = null;
                _isConnected = false;
            }
        }

        /// <summary>
        /// Try to upload the stub loader and optionally change baud rate.
        /// Failures are silently ignored — ROM bootloader continues to work.
        /// </summary>
        private void TryUploadStub(bool useStandardBaudrate)
        {
            try
            {
                _stubUploaded = Esp32StubLoader.UploadStub(_client, _chipType, Verbosity);

                if (_stubUploaded && !useStandardBaudrate && _baudRate != Esp32BootloaderClient.DefaultBaudRate)
                {
                    // Change to user-requested baud rate for faster transfers
                    _client.ChangeBaudRate(_baudRate);

                    if (Verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.WriteLine($"Changed baud rate to {_baudRate}.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Stub upload failed — fall back to ROM mode
                _stubUploaded = false;

                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    OutputWriter.WriteLine($"Stub upload failed ({ex.Message}), using ROM bootloader.");
                }
            }
        }

        /// <summary>
        /// Tries reading ESP32 device details.
        /// </summary>
        /// <returns>The filled info structure with all the information about the connected ESP32 device or null if an error occurred.</returns>
        public Esp32DeviceInfo GetDeviceDetails(
            string targetName,
            bool requireFlashSize = true,
            bool forcePsRamCheck = false,
            bool hardResetAfterCommand = false)
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write($"Reading details from chip...");
            }

            try
            {
                EnsureConnected();

                // Detect chip type via magic register
                string chipType = _chipDetector.DetectChipType();
                _chipType = chipType;

                var config = _chipDetector.Config;

                // Now that we know the chip type, try uploading the stub
                // (was skipped during EnsureConnected if _chipType was "auto")
                if (!_stubUploaded)
                {
                    TryUploadStub(useStandardBaudrate: false);
                }

                // Read chip info
                string chipName = _chipDetector.ReadChipName();
                string features = _chipDetector.ReadFeatures();
                string crystal = _chipDetector.ReadCrystalFrequency();
                string mac = _chipDetector.ReadMacAddress();

                // Read flash ID via SPI
                var (manufacturerId, deviceId) = _chipDetector.ReadFlashId();

                // Detect flash size from JEDEC ID
                int flashSize = Esp32ChipDetector.DetectFlashSizeFromId(deviceId);

                if (flashSize <= 0 && requireFlashSize)
                {
                    throw new EspToolExecutionException("Can't read flash size from device");
                }

                if (flashSize > 0)
                {
                    _flashSize = flashSize;
                }

                // Initialize flash controller now that we know the chip config
                _flashController = new Esp32FlashController(_client, config);

                // Determine PSRAM availability
                PSRamAvailability psramIsAvailable = PSRamAvailability.Undetermined;
                int psRamSize = 0;

                if (_chipType == "esp32c3"
                   || _chipType == "esp32c6"
                   || _chipType == "esp32h2")
                {
                    // these series don't have PSRAM
                    psramIsAvailable = PSRamAvailability.No;
                }
                else if (_chipType == "esp32s3")
                {
                    // check device features for PSRAM mention and size
                    Regex regex = new(@"Embedded PSRAM (?<size>\d+)MB");
                    Match psRamMatch = regex.Match(features);

                    if (psRamMatch.Success)
                    {
                        psRamSize = int.Parse(psRamMatch.Groups["size"].Value);
                        psramIsAvailable = PSRamAvailability.Yes;
                    }
                }
                else if (_chipType == "esp32s2")
                {
                    // these devices usually require boot into bootloader which prevents running the app to get psram details.
                    psramIsAvailable = PSRamAvailability.Undetermined;
                }

                if (psramIsAvailable == PSRamAvailability.Undetermined)
                {
                    // try to find out if PSRAM is present
                    psramIsAvailable = FindPSRamAvailable(out psRamSize, forcePsRamCheck);
                }

                // Hard reset after command if requested (and we don't need the connection for anything else)
                if (hardResetAfterCommand && _client != null)
                {
                    _client.HardReset();
                    Disconnect();
                }

                if (Verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine("OK".PadRight(110));
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // Map chip type to display format with hyphen (e.g. "esp32s3" → "ESP32-S3")
                string displayChipType = chipType switch
                {
                    "esp32" => "ESP32",
                    "esp32s2" => "ESP32-S2",
                    "esp32s3" => "ESP32-S3",
                    "esp32c3" => "ESP32-C3",
                    "esp32c6" => "ESP32-C6",
                    "esp32h2" => "ESP32-H2",
                    _ => chipType.ToUpperInvariant(),
                };

                return new Esp32DeviceInfo(
                    displayChipType,
                    chipName,
                    features,
                    crystal,
                    mac.ToUpperInvariant(),
                    manufacturerId,
                    deviceId,
                    _flashSize,
                    psramIsAvailable,
                    psRamSize);
            }
            catch (Exception ex) when (ex is not EspToolExecutionException)
            {
                if (ex.Message.Contains("Failed to connect") || ex is TimeoutException)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;

                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine("Can't connect to ESP32 bootloader. Try to put the board in bootloader manually.");
                    OutputWriter.WriteLine("For troubleshooting steps visit: https://docs.espressif.com/projects/esptool/en/latest/troubleshooting.html.");
                    OutputWriter.WriteLine("");

                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                throw new EspToolExecutionException(ex.Message);
            }
        }

        /// <summary>
        /// Perform detection of PSRAM availability on connected device.
        /// </summary>
        /// <param name="force">Force the detection of PSRAM availability.</param>
        /// <param name="psRamSize">Size of the PSRAM device, if detection was successful.</param>
        /// <returns>Information about availability of PSRAM, if that was possible to determine.</returns>
        private PSRamAvailability FindPSRamAvailable(
            out int psRamSize,
            bool force = false)
        {
            // backup current verbosity setting
            VerbosityLevel bkpVerbosity = Verbosity;
            Verbosity = VerbosityLevel.Quiet;

            // default to no PSRAM
            psRamSize = 0;

            try
            {
                if (force)
                {
                    // adjust flash size according to the series
                    // default to 2MB for ESP32 series
                    int flashSize = 2 * 1024 * 1024;

                    if (_chipType == "esp32s2"
                       || _chipType == "esp32s3")
                    {
                        flashSize = 4 * 1024 * 1024;
                    }

                    // compose bootloader partition
                    var bootloaderPartition = new Dictionary<int, string>
                    {
                        // bootloader goes to 0x1000, except for ESP32_S3, which goes to 0x0
                        { _chipType == "esp32s3" ? 0x0 : 0x1000, Path.Combine(Utilities.ExecutingPath, $"{_chipType}bootloader", "bootloader.bin") },

                        // nanoCLR goes to 0x10000
                        { 0x10000, Path.Combine(Utilities.ExecutingPath, $"{_chipType}bootloader", "test_startup.bin") },

                        // partition table goes to 0x8000
                        { 0x8000, Path.Combine(Utilities.ExecutingPath, $"{_chipType}bootloader", $"partitions_{Esp32DeviceInfo.GetFlashSizeAsString(flashSize).ToLowerInvariant()}.bin") }
                    };

                    // need to use standard baud rate here because of boards put in download mode
                    if (WriteFlash(bootloaderPartition, true) != ExitCodes.OK)
                    {
                        // something went wrong, can't determine PSRAM availability
                        return PSRamAvailability.Undetermined;
                    }
                }
                else
                {
                    // Soft reset the device to read bootloader output
                    try
                    {
                        EnsureConnected();
                        _client.HardReset();
                        Disconnect();
                    }
                    catch
                    {
                        return PSRamAvailability.Undetermined;
                    }
                }

                try
                {
                    // force baud rate to 115200 (standard baud rate for bootloader)
                    SerialPort espDevice = new(
                        _serialPort,
                        115200);

                    // open COM port and grab output
                    espDevice.Open();

                    if (espDevice.IsOpen)
                    {
                        // wait 2 seconds...
                        Thread.Sleep(TimeSpan.FromSeconds(2));

                        // ... read output from bootloader
                        string bootloaderOutput = espDevice.ReadExisting();

                        espDevice.Close();

                        // look for "magic" string

                        if (bootloaderOutput.Contains("PSRAM initialized"))
                        {
                            // extract PSRAM size
                            Match match = Regex.Match(bootloaderOutput, @"Found (?<size>\d+)MB PSRAM device");
                            if (match.Success)
                            {
                                psRamSize = int.Parse(match.Groups["size"].Value);
                            }

                            return PSRamAvailability.Yes;
                        }
                        else if (bootloaderOutput.Contains("PSRAM ID read error"))
                        {
                            return PSRamAvailability.No;
                        }
                        else
                        {
                            return PSRamAvailability.Undetermined;
                        }
                    }
                }
                catch
                {
                    // don't care about any exceptions
                }
            }
            finally
            {
                // restore verbosity setting
                Verbosity = bkpVerbosity;
            }

            return PSRamAvailability.Undetermined;
        }

        /// <summary>
        /// Backup the entire flash into a bin file.
        /// </summary>
        /// <param name="backupFilename">Backup file including full path.</param>
        /// <param name="flashSize">Flash size in bytes.</param>
        /// <param name="hardResetAfterCommand">If true the chip will execute a hard reset via DTR signal.</param>
        internal void BackupFlash(string backupFilename,
            int flashSize,
            bool hardResetAfterCommand = false)
        {
            EnsureConnected();
            EnsureFlashController();

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"Reading flash ({flashSize} bytes) to {backupFilename}...");
            }

            _flashController.ReadFlashToFile(
                backupFilename,
                0,
                flashSize,
                (bytesRead, totalBytes) =>
                {
                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        int percent = (int)((long)bytesRead * 100 / totalBytes);
                        OutputWriter.Write($"\rReading flash... {percent}%".PadRight(110));
                        OutputWriter.Write("\r");
                    }
                });

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"\rRead {flashSize} bytes from flash.".PadRight(110));
            }

            if (hardResetAfterCommand)
            {
                _client.HardReset();
                Disconnect();
            }
        }

        /// <summary>
        /// Backup the config partition to a file.
        /// </summary>
        /// <param name="backupFilename">Backup file including full path.</param>
        /// <param name="address">Start address of the config partition.</param>
        /// <param name="size">Size of the config partition.</param>
        /// <returns>Exit code indicating success or failure.</returns>
        internal ExitCodes BackupConfigPartition(
            string backupFilename,
            int address,
            int size)
        {
            EnsureConnected();
            EnsureFlashController();

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"Reading config partition (0x{address:X}, {size} bytes) to {backupFilename}...");
            }

            _flashController.ReadFlashToFile(
                backupFilename,
                (uint)address,
                size,
                (bytesRead, totalBytes) =>
                {
                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        int percent = (int)((long)bytesRead * 100 / totalBytes);
                        OutputWriter.Write($"\rBacking up config partition... {percent}%".PadRight(110));
                        OutputWriter.Write("\r");
                    }
                });

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"\rRead {size} bytes from config partition.".PadRight(110));
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Erase the entire flash of the ESP32 chip.
        /// </summary>
        /// <returns>Exit code indicating success or failure.</returns>
        internal ExitCodes EraseFlash()
        {
            EnsureConnected();
            EnsureFlashController();

            _flashController.EraseFlash();

            return ExitCodes.OK;
        }

        /// <summary>
        /// Erase flash segment on the ESP32 chip.
        /// </summary>
        /// <returns>Exit code indicating success or failure.</returns>
        internal ExitCodes EraseFlashSegment(uint startAddress, uint length)
        {
            EnsureConnected();
            EnsureFlashController();

            _flashController.EraseRegion(startAddress, length);

            if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"Erased flash region at 0x{startAddress:X}, length 0x{length:X}.");
            }

            return ExitCodes.OK;
        }

        /// <summary>
        /// Write to the flash.
        /// </summary>
        /// <param name="partsToWrite">Dictionary which keys are the start addresses and the values are the complete filenames (the bin files).</param>
        /// <param name="useStandardBaudrate">Use the standard baud rate (default is false).</param>
        /// <returns>Exit code indicating success or failure.</returns>
        internal ExitCodes WriteFlash(
            Dictionary<int, string> partsToWrite,
            bool useStandardBaudrate = false)
        {
            EnsureConnected(useStandardBaudrate);
            EnsureFlashController();

            CouldntResetTarget = false;

            // Configure flash chip parameters (SPI_SET_PARAMS)
            // This tells the stub/ROM the flash geometry so writes work correctly.
            if (_flashSize > 0)
            {
                _flashController.SendSpiSetParams(_flashSize);
            }

            // Pre-process partitions: patch bootloader image header with flash parameters
            var processedParts = new Dictionary<int, string>(partsToWrite.Count);
            var tempFiles = new List<string>();

            try
            {
                foreach (var part in partsToWrite)
                {
                    uint address = (uint)part.Key;
                    string filePath = part.Value;

                    // Only attempt header patching on the bootloader partition
                    if (_flashSize > 0
                        && address == (uint)_chipDetector.Config.BootloaderAddress
                        && !string.IsNullOrEmpty(_flashMode))
                    {
                        byte[] imageData = File.ReadAllBytes(filePath);
                        byte[] patched = _flashController.PatchBootloaderImageHeader(
                            address, imageData, _flashMode, _flashFrequency, _flashSize);

                        if (patched != imageData)
                        {
                            // Write patched image to a temp file
                            string tempPath = Path.GetTempFileName();
                            File.WriteAllBytes(tempPath, patched);
                            tempFiles.Add(tempPath);
                            processedParts[part.Key] = tempPath;

                            if (Verbosity >= VerbosityLevel.Detailed)
                            {
                                OutputWriter.WriteLine(
                                    $"Flash params set to 0x{patched[2]:X2}{patched[3]:X2}.");
                            }

                            continue;
                        }
                    }

                    processedParts[part.Key] = filePath;
                }

                bool useCompressed = _stubUploaded && _client.IsStubRunning;

                foreach (var part in processedParts)
                {
                    uint partAddress = (uint)part.Key;
                    byte[] fileData = File.ReadAllBytes(part.Value);
                    int uncompressedSize = fileData.Length;
                    byte[] compressedData = null;

                    if (useCompressed)
                    {
                        compressedData = Esp32FlashController.CompressZlib(fileData);

                        if (Verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine(
                                $"Compressed {uncompressedSize} bytes to {compressedData.Length}...");
                        }
                    }

                    var sw = Stopwatch.StartNew();

                    if (useCompressed)
                    {
                        int compTotal = compressedData.Length;

                        _flashController.WriteFlashCompressed(
                            partAddress,
                            fileData,
                            compressedData,
                            (compSent, compLen) =>
                            {
                                if (Verbosity >= VerbosityLevel.Normal)
                                {
                                    long uncompEst = (long)compSent * uncompressedSize / compLen;
                                    uint curAddr = partAddress + (uint)uncompEst;
                                    WriteProgressBar(curAddr, compSent, compLen);
                                }
                            });
                    }
                    else
                    {
                        _flashController.WriteFlash(
                            partAddress,
                            fileData,
                            (bytesSent, total) =>
                            {
                                if (Verbosity >= VerbosityLevel.Normal)
                                {
                                    uint curAddr = partAddress + (uint)bytesSent;
                                    WriteProgressBar(curAddr, bytesSent, total);
                                }
                            });
                    }

                    sw.Stop();

                    if (Verbosity >= VerbosityLevel.Normal)
                    {
                        double secs = sw.Elapsed.TotalSeconds;
                        int written = useCompressed ? uncompressedSize : uncompressedSize;
                        int compSize = compressedData?.Length ?? uncompressedSize;
                        double kbits = secs > 0 ? (compSize * 8.0 / 1000.0) / secs : 0;

                        OutputWriter.Write("\r".PadRight(110));
                        OutputWriter.Write("\r");

                        if (useCompressed)
                        {
                            OutputWriter.WriteLine(
                                $"Wrote {written} bytes ({compSize} compressed) at 0x{partAddress:X8} in {secs:F1} seconds ({kbits:F1} kbit/s).");
                        }
                        else
                        {
                            OutputWriter.WriteLine(
                                $"Wrote {written} bytes at 0x{partAddress:X8} in {secs:F1} seconds ({kbits:F1} kbit/s).");
                        }
                    }
                }
            }
            finally
            {
                // Clean up temp files
                foreach (string tempFile in tempFiles)
                {
                    try { File.Delete(tempFile); }
                    catch { /* ignore cleanup errors */ }
                }
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("\rFlash write complete.".PadRight(110));
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            // Try to reset the target after flashing
            try
            {
                _client.HardReset();
            }
            catch
            {
                CouldntResetTarget = true;
            }

            // Disconnect so next operation starts fresh
            Disconnect();

            return ExitCodes.OK;
        }

        /// <summary>
        /// Ensure the flash controller is initialized.
        /// </summary>
        private void EnsureFlashController()
        {
            if (_flashController != null)
            {
                return;
            }

            // Chip detector must exist (created by EnsureConnected)
            if (_chipDetector == null)
            {
                throw new InvalidOperationException("Cannot create flash controller: not connected to bootloader.");
            }

            if (_chipDetector.Config == null)
            {
                // Need to detect chip type first to get the config
                _chipDetector.DetectChipType();
            }

            _flashController = new Esp32FlashController(_client, _chipDetector.Config);
        }

        /// <summary>
        /// Write an esptool-style progress bar to the console.
        /// Format: Writing at 0xADDR [=====>                        ]  XX.X% sent/total bytes...
        /// </summary>
        private static void WriteProgressBar(uint address, int bytesSent, int totalBytes)
        {
            const int BarWidth = 30;

            double fraction = totalBytes > 0 ? (double)bytesSent / totalBytes : 0;
            if (fraction > 1)
            {
                fraction = 1;
            }

            int filled = (int)(fraction * BarWidth);
            double pct = fraction * 100.0;

            // Build bar: '=' for filled, '>' for tip (unless 100%), ' ' for rest
            var bar = new char[BarWidth];

            for (int i = 0; i < BarWidth; i++)
            {
                if (i < filled)
                {
                    bar[i] = '=';
                }
                else if (i == filled && filled < BarWidth)
                {
                    bar[i] = '>';
                }
                else
                {
                    bar[i] = ' ';
                }
            }

            string line = $"\rWriting at 0x{address:X8} [{new string(bar)}] {pct,5:F1}% {bytesSent}/{totalBytes} bytes...";
            OutputWriter.Write(line.PadRight(110));
            OutputWriter.Write("\r");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
    }
}
