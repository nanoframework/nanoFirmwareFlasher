//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// High-level STM32 device that communicates via the UART bootloader (AN3155).
    /// Provides flash operations equivalent to <see cref="StmDfuDevice"/> and <see cref="StmJtagDevice"/>
    /// but without requiring external tools.
    /// </summary>
    public class Stm32UartDevice : IDisposable, IStmFlashableDevice
    {
        private readonly Stm32UartBootloader _bootloader;
        private bool _disposed = false;

        /// <summary>
        /// Name of the serial port this device is connected to.
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// Product ID (chip ID) of the connected STM32 device.
        /// </summary>
        public ushort ChipId { get; private set; }

        /// <summary>
        /// Bootloader version of the connected device.
        /// </summary>
        public byte BootloaderVersion { get; private set; }

        /// <summary>
        /// Gets whether a device was successfully detected.
        /// </summary>
        public bool DevicePresent { get; private set; }

        /// <summary>
        /// Property with option for performing mass erase on the connected device.
        /// If <see langword="false"/> only the flash sectors that will be programmed are erased.
        /// </summary>
        public bool DoMassErase { get; set; }

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Normal;

        /// <summary>
        /// When <see langword="true"/>, read back flash contents after programming
        /// and verify they match the source data.
        /// </summary>
        public bool Verify { get; set; }

        /// <summary>
        /// Creates a new <see cref="Stm32UartDevice"/> and connects to the STM32 bootloader
        /// on the specified serial port.
        /// </summary>
        /// <param name="portName">Serial port name (e.g. COM3).</param>
        /// <param name="baudRate">Baud rate to use. Default is 115200.</param>
        public Stm32UartDevice(string portName, int baudRate = 115200)
        {
            PortName = portName;
            _bootloader = new Stm32UartBootloader();

            try
            {
                _bootloader.Open(portName, baudRate);
                ChipId = _bootloader.GetChipId();
                BootloaderVersion = _bootloader.BootloaderVersion;
                DevicePresent = true;
            }
            catch (Exception)
            {
                DevicePresent = false;
                throw;
            }
        }

        /// <summary>
        /// Flash HEX files to the connected device.
        /// </summary>
        /// <param name="files">List of HEX file paths to flash.</param>
        /// <returns>The operation exit code.</returns>
        public ExitCodes FlashHexFiles(IList<string> files)
        {
            // check file existence
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    return ExitCodes.E5003;
                }
            }

            // erase flash
            if (DoMassErase)
            {
                ExitCodes eraseResult = MassErase();

                if (eraseResult != ExitCodes.OK)
                {
                    return eraseResult;
                }

                DoMassErase = false;
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Flashing device...");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("Flashing device...");
            }

            foreach (string hexFile in files)
            {
                string hexFilePath = Utilities.MakePathAbsolute(
                    Environment.CurrentDirectory,
                    hexFile);

                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine($"{Path.GetFileName(hexFile)}");
                }

                try
                {
                    List<IntelHexParser.MemoryBlock> blocks = IntelHexParser.Parse(hexFilePath);

                    foreach (IntelHexParser.MemoryBlock block in blocks)
                    {
                        if (Verbosity >= VerbosityLevel.Diagnostic)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                            OutputWriter.WriteLine($"  Writing {block.Data.Length} bytes @ 0x{block.Address:X8}");
                        }

                        _bootloader.WriteMemoryBlock(
                            block.Address,
                            block.Data,
                            Verbosity >= VerbosityLevel.Diagnostic
                                ? (written, total) =>
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.DarkGray;
                                    OutputWriter.Write($"\r  Progress: {written}/{total} bytes ({100 * written / total}%)");
                                }
                                : null);

                        if (Verbosity >= VerbosityLevel.Diagnostic)
                        {
                            OutputWriter.WriteLine();
                        }

                        if (Verify)
                        {
                            if (Verbosity >= VerbosityLevel.Diagnostic)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                                OutputWriter.WriteLine($"  Verifying {block.Data.Length} bytes @ 0x{block.Address:X8}");
                            }

                            if (!_bootloader.VerifyMemoryBlock(block.Address, block.Data))
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Red;
                                OutputWriter.WriteLine($"ERROR: Verification failed @ 0x{block.Address:X8}");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                                return ExitCodes.E5022;
                            }
                        }
                    }
                }
                catch (Stm32UartBootloaderException ex)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"ERROR: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E5006;
                }
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine(" OK");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Flashing completed...");
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        /// <summary>
        /// Flash BIN files to the connected device at specified addresses.
        /// </summary>
        /// <param name="files">List of BIN file paths to flash.</param>
        /// <param name="addresses">List of flash addresses (hex format, e.g. "0x08000000").</param>
        /// <returns>The operation exit code.</returns>
        public ExitCodes FlashBinFiles(IList<string> files, IList<string> addresses)
        {
            // check file existence
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    return ExitCodes.E5004;
                }
            }

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

                if (!address.StartsWith("0x"))
                {
                    return ExitCodes.E5008;
                }

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
                ExitCodes eraseResult = MassErase();

                if (eraseResult != ExitCodes.OK)
                {
                    return eraseResult;
                }

                DoMassErase = false;
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Flashing device...");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("Flashing device...");
            }

            for (int i = 0; i < files.Count; i++)
            {
                string binFilePath = Utilities.MakePathAbsolute(
                    Environment.CurrentDirectory,
                    files[i]);

                uint flashAddress = uint.Parse(
                    addresses[i].Substring(2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture);

                if (Verbosity >= VerbosityLevel.Detailed)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.WriteLine($"{Path.GetFileName(binFilePath)} @ 0x{flashAddress:X8}");
                }

                try
                {
                    byte[] data = File.ReadAllBytes(binFilePath);

                    _bootloader.WriteMemoryBlock(
                        flashAddress,
                        data,
                        Verbosity >= VerbosityLevel.Diagnostic
                            ? (written, total) =>
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.DarkGray;
                                OutputWriter.Write($"\r  Progress: {written}/{total} bytes ({100 * written / total}%)");
                            }
                            : null);

                    if (Verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine();
                    }

                    if (Verify)
                    {
                        if (Verbosity >= VerbosityLevel.Diagnostic)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                            OutputWriter.WriteLine($"  Verifying {data.Length} bytes @ 0x{flashAddress:X8}");
                        }

                        if (!_bootloader.VerifyMemoryBlock(flashAddress, data))
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Red;
                            OutputWriter.WriteLine($"ERROR: Verification failed @ 0x{flashAddress:X8}");
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                            return ExitCodes.E5022;
                        }
                    }
                }
                catch (Stm32UartBootloaderException ex)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"ERROR: {ex.Message}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E5006;
                }
            }

            if (Verbosity == VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine(" OK");
            }
            else if (Verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("Flashing completed...");
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        /// <summary>
        /// Perform mass erase on the connected device.
        /// </summary>
        /// <returns>The operation exit code.</returns>
        public ExitCodes MassErase()
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Mass erase device...");
            }

            try
            {
                _bootloader.GlobalErase();
            }
            catch (Stm32UartBootloaderException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"ERROR: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E5005;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine(" OK");
            }
            else
            {
                OutputWriter.WriteLine("");
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        /// <summary>
        /// Start execution at the specified address.
        /// </summary>
        /// <param name="startAddress">Hex string of the address (without 0x prefix).</param>
        /// <returns>The operation exit code.</returns>
        public ExitCodes StartExecution(string startAddress)
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Starting execution on device...");
            }

            try
            {
                uint address = uint.Parse(
                    startAddress,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture);

                _bootloader.Go(address);
            }
            catch (Stm32UartBootloaderException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"ERROR: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E1006;
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine(" OK");
            }
            else
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.WriteLine("");
            }

            OutputWriter.ForegroundColor = ConsoleColor.White;

            return ExitCodes.OK;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder deviceInfo = new();

            deviceInfo.AppendLine($"Chip ID: 0x{ChipId:X4}");
            deviceInfo.AppendLine($"Bootloader version: {BootloaderVersion >> 4}.{BootloaderVersion & 0x0F}");
            deviceInfo.AppendLine($"Port: {PortName}");

            return deviceInfo.ToString();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of managed resources.
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _bootloader?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
