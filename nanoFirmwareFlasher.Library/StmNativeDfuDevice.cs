//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using nanoFramework.Tools.FirmwareFlasher.UsbDfu;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// STM32 DFU device using native USB instead of the STM32_Programmer_CLI.
    /// Provides the same public API as <see cref="StmDfuDevice"/> but requires no external tools.
    /// Cross-platform: uses WinUSB on Windows, libusb-1.0 on Linux/macOS.
    /// </summary>
    public class StmNativeDfuDevice : IDisposable, IStmFlashableDevice
    {
        private readonly DfuDevice _dfu;
        private bool _disposed;

        /// <summary>
        /// Name of the connected device (USB product string).
        /// </summary>
        public string DeviceName { get; private set; }

        /// <summary>
        /// CPU of the connected device (derived from chip info).
        /// </summary>
        public string DeviceCPU { get; private set; }

        /// <summary>
        /// Serial number of the connected DFU device.
        /// </summary>
        public string DfuId { get; private set; }

        /// <summary>
        /// This property is <see langword="true"/> if a DFU device is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(DfuId);

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
        /// Creates a new <see cref="StmNativeDfuDevice"/>. If a DFU device ID is provided it will try
        /// to connect to that device. Otherwise connects to the first available device.
        /// </summary>
        /// <param name="dfuId">Serial number of the device to connect to, or null for auto-detect.</param>
        /// <exception cref="CantConnectToDfuDeviceException">No DFU device found or couldn't connect.</exception>
        public StmNativeDfuDevice(string dfuId = null)
        {
            List<(string serial, string devicePath)> devices = DfuDevice.Enumerate();

            if (devices.Count == 0)
            {
                throw new CantConnectToDfuDeviceException(
                    "No STM32 DFU devices found. Make sure the device is in DFU mode. " +
                    "On Windows, install the WinUSB driver by running: nanoff --installdfudrivers (or use Zadig: https://zadig.akeo.ie). " +
                    "On Linux, install libusb: sudo apt install libusb-1.0-0, then add a udev rule: " +
                    "echo 'SUBSYSTEM==\"usb\", ATTR{idVendor}==\"0483\", ATTR{idProduct}==\"df11\", MODE=\"0666\"' | sudo tee /etc/udev/rules.d/70-st-dfu.rules && sudo udevadm control --reload-rules. " +
                    "On macOS, install libusb: brew install libusb.");
            }

            string selectedPath = null;

            if (string.IsNullOrEmpty(dfuId))
            {
                // Auto-select first device
                DfuId = devices[0].serial;
                selectedPath = devices[0].devicePath;
            }
            else
            {
                // Find the requested device
                foreach (var device in devices)
                {
                    if (device.serial == dfuId)
                    {
                        DfuId = dfuId;
                        selectedPath = device.devicePath;
                        break;
                    }
                }

                if (selectedPath == null)
                {
                    throw new CantConnectToDfuDeviceException(
                        $"DFU device with serial '{dfuId}' not found. Available device(s): {string.Join(", ", GetDeviceSerials(devices))}");
                }
            }

            _dfu = new DfuDevice();

            try
            {
                _dfu.Open(selectedPath);
                _dfu.EnsureIdle();

                // Read device information from USB descriptors
                ReadDeviceInfo();
            }
            catch (Exception ex) when (!(ex is CantConnectToDfuDeviceException))
            {
                _dfu.Dispose();
                throw new CantConnectToDfuDeviceException(
                    $"Failed to connect to DFU device: {ex.Message}");
            }
        }

        /// <summary>
        /// Lists connected STM32 DFU devices using native USB enumeration.
        /// </summary>
        /// <returns>A collection of connected STM DFU devices as (serial, deviceIndex) tuples,
        /// matching the return format of <see cref="StmDfuDevice.ListDevices"/>.</returns>
        public static List<(string serial, string device)> ListDevices()
        {
            var result = new List<(string serial, string device)>();

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return result;
            }

            var devices = DfuDevice.Enumerate();

            for (int i = 0; i < devices.Count; i++)
            {
                // Use "USB{i+1}" as device index to match STM32_Programmer_CLI format
                result.Add((devices[i].serial, $"USB{i + 1}"));
            }

            return result;
        }

        /// <summary>
        /// Flash the HEX files supplied to the connected device.
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

                        _dfu.WriteFirmware(
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
                    }
                }
                catch (DfuOperationFailedException ex)
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
        /// Flash the BIN files supplied to the connected device.
        /// </summary>
        /// <param name="files">List of BIN file paths.</param>
        /// <param name="addresses">List of flash addresses in hex format (e.g. "0x08000000").</param>
        /// <returns>The operation exit code.</returns>
        public ExitCodes FlashBinFiles(
            IList<string> files,
            IList<string> addresses)
        {
            // check file existence
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    return ExitCodes.E5004;
                }
            }

            // check addresses
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

                    _dfu.WriteFirmware(
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
                }
                catch (DfuOperationFailedException ex)
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
        /// Start execution on connected device at the specified address.
        /// </summary>
        /// <param name="startAddress">Hex string (without 0x prefix) of the address to start execution from.</param>
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

                _dfu.SetAddress(address);
                _dfu.LeaveDfuMode();
            }
            catch (DfuOperationFailedException ex)
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
                _dfu.MassErase();
            }
            catch (DfuOperationFailedException ex)
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

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder deviceInfo = new();

            if (!string.IsNullOrEmpty(DeviceName))
            {
                deviceInfo.AppendLine($"Device: {DeviceName}");
            }

            if (!string.IsNullOrEmpty(DeviceCPU))
            {
                deviceInfo.AppendLine($"CPU: {DeviceCPU}");
            }

            deviceInfo.AppendLine($"DFU ID: {DfuId}");
            deviceInfo.AppendLine("Interface: Native USB (WinUSB)");

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
                    _dfu?.Dispose();
                }

                _disposed = true;
            }
        }

        #region Private helpers

        private void ReadDeviceInfo()
        {
            try
            {
                var descriptor = _dfu.GetDeviceDescriptor();
                DeviceName = _dfu.GetStringDescriptor(descriptor.iProduct);

                // The serial number from the descriptor may be more readable than from the device path
                string usbSerial = _dfu.GetStringDescriptor(descriptor.iSerialNumber);
                if (!string.IsNullOrEmpty(usbSerial) && string.IsNullOrEmpty(DfuId))
                {
                    DfuId = usbSerial;
                }

                // STM32 DFU devices typically report "STM32 BOOTLOADER" as product
                // CPU info is not directly available from DFU descriptors, but we can infer
                // from the VID/PID that it's an STM32 device.
                if (descriptor.vendorId == DfuConst.StmVendorId)
                {
                    DeviceCPU = "STM32";
                }
            }
            catch
            {
                // Non-fatal: device info is informational only
                DeviceName = "STM32 BOOTLOADER";
                DeviceCPU = "STM32";
            }
        }

        private static IEnumerable<string> GetDeviceSerials(List<(string serial, string devicePath)> devices)
        {
            foreach (var device in devices)
            {
                yield return device.serial;
            }
        }

        #endregion
    }
}
