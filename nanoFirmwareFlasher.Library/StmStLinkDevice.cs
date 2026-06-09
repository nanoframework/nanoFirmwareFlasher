//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// STM32 device using a native ST-LINK V2/V3 debug probe (USB bulk protocol).
    /// Provides the same public API as <see cref="StmJtagDevice"/> but requires no
    /// STM32_Programmer_CLI executable.
    /// Cross-platform: uses WinUSB on Windows, libusb-1.0 on Linux/macOS.
    /// </summary>
    public class StmStLinkDevice : IDisposable, IStmFlashableDevice
    {
        private readonly StLinkTransport _stLink;
        private readonly SwdProtocol _swd;
        private readonly ArmMemAp _mem;
        private readonly Stm32FlashProgrammer _flash;
        private bool _disposed;

        /// <summary>
        /// This property is <see langword="true"/> if an ST-LINK probe is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(ProbeId);

        /// <summary>
        /// Serial number of the connected ST-LINK probe.
        /// </summary>
        public string ProbeId { get; private set; }

        /// <summary>
        /// Product name of the connected ST-LINK probe.
        /// </summary>
        public string ProbeName { get; private set; }

        /// <summary>
        /// Name of the connected STM32 device (derived from the chip IDCODE).
        /// </summary>
        public string DeviceName { get; private set; }

        /// <summary>
        /// CPU of the connected device.
        /// </summary>
        public string DeviceCPU { get; private set; }

        /// <summary>
        /// Debug Port IDCODE of the target.
        /// </summary>
        public uint DpIdcode { get; private set; }

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
        /// Creates a new <see cref="StmStLinkDevice"/> and connects via an ST-LINK debug probe.
        /// If a probe serial number is provided it will try to connect to that probe.
        /// Otherwise connects to the first available ST-LINK probe.
        /// </summary>
        /// <param name="probeId">Serial number of the probe to connect to, or null for auto-detect.</param>
        /// <exception cref="CantConnectToJtagDeviceException">No probe or target found.</exception>
        public StmStLinkDevice(string probeId = null)
        {
            var probes = StLinkTransport.Enumerate();

            if (probes.Count == 0)
            {
                throw new CantConnectToJtagDeviceException(
                    "No ST-LINK debug probes found. Make sure a probe is connected and the correct driver is installed. " +
                    "On Windows, install the WinUSB driver using Zadig (https://zadig.akeo.ie). " +
                    "On Linux, add a udev rule: echo 'SUBSYSTEM==\"usb\", ATTR{idVendor}==\"0483\", MODE=\"0666\"' " +
                    "| sudo tee /etc/udev/rules.d/70-st-link.rules && sudo udevadm control --reload-rules. " +
                    "On macOS, no additional drivers are needed.");
            }

            string selectedPath = null;

            if (string.IsNullOrEmpty(probeId))
            {
                ProbeId = probes[0].serialNumber;
                ProbeName = probes[0].productName;
                selectedPath = probes[0].devicePath;
            }
            else
            {
                foreach (var probe in probes)
                {
                    if (probe.serialNumber == probeId)
                    {
                        ProbeId = probeId;
                        ProbeName = probe.productName;
                        selectedPath = probe.devicePath;
                        break;
                    }
                }

                if (selectedPath == null)
                {
                    throw new CantConnectToJtagDeviceException(
                        $"ST-LINK probe with serial '{probeId}' not found.");
                }
            }

            _stLink = new StLinkTransport();
            _swd = new SwdProtocol(_stLink);
            _mem = new ArmMemAp(_swd);
            _flash = new Stm32FlashProgrammer(_mem);

            try
            {
                _stLink.Open(selectedPath);
                _swd.Initialize();

                DpIdcode = _swd.DpIdcodeValue;

                // Halt core before reading IDCODE registers
                _swd.HaltCore();

                // Detect STM32 family
                var family = _flash.DetectFamily();
                DeviceName = family.ToString();
                DeviceCPU = $"STM32 ({family})";
            }
            catch (SwdProtocolException ex)
            {
                Dispose();
                throw new CantConnectToJtagDeviceException(
                    $"Failed to connect to target via ST-LINK SWD: {ex.Message}");
            }
            catch (Exception ex) when (!(ex is CantConnectToJtagDeviceException))
            {
                Dispose();
                throw new CantConnectToJtagDeviceException(
                    $"Failed to initialize ST-LINK connection: {ex.Message}");
            }
        }

        /// <summary>
        /// Lists connected ST-LINK debug probes.
        /// </summary>
        /// <returns>A collection of connected probes as serial number strings.</returns>
        public static List<string> ListDevices()
        {
            var result = new List<string>();
            var probes = StLinkTransport.Enumerate();

            foreach (var probe in probes)
            {
                result.Add(probe.serialNumber);
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
            if (files.Any(f => !File.Exists(f)))
            {
                return ExitCodes.E5003;
            }

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
                    var blocks = IntelHexParser.Parse(hexFilePath);

                    foreach (var block in blocks)
                    {
                        if (Verbosity >= VerbosityLevel.Diagnostic)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                            OutputWriter.WriteLine(
                                $"  Writing {block.Data.Length} bytes @ 0x{block.Address:X8}");
                        }

                        _flash.EraseAndProgram(block.Address, block.Data, 0, block.Data.Length);

                        if (Verify)
                        {
                            if (Verbosity >= VerbosityLevel.Diagnostic)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                                OutputWriter.WriteLine($"  Verifying {block.Data.Length} bytes @ 0x{block.Address:X8}");
                            }

                            if (!_flash.Verify(block.Address, block.Data, 0, block.Data.Length))
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Red;
                                OutputWriter.WriteLine($"ERROR: Verification failed @ 0x{block.Address:X8}");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                                return ExitCodes.E5022;
                            }
                        }
                    }
                }
                catch (SwdProtocolException ex)
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
            if (files.Any(f => !File.Exists(f)))
            {
                return ExitCodes.E5004;
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
                    _flash.EraseAndProgram(flashAddress, data, 0, data.Length);

                    if (Verify)
                    {
                        if (Verbosity >= VerbosityLevel.Diagnostic)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                            OutputWriter.WriteLine($"  Verifying {data.Length} bytes @ 0x{flashAddress:X8}");
                        }

                        if (!_flash.Verify(flashAddress, data, 0, data.Length))
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Red;
                            OutputWriter.WriteLine($"ERROR: Verification failed @ 0x{flashAddress:X8}");
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                            return ExitCodes.E5022;
                        }
                    }
                }
                catch (SwdProtocolException ex)
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
                _flash.MassErase();
            }
            catch (SwdProtocolException ex)
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
        /// Reset MCU of connected device.
        /// </summary>
        /// <returns>The operation exit code.</returns>
        public ExitCodes ResetMcu()
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Reset MCU on device...");
            }

            try
            {
                _swd.SystemReset();
            }
            catch (SwdProtocolException ex)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"ERROR: {ex.Message}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E5010;
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
        /// Start execution on connected device.
        /// </summary>
        /// <returns>The operation exit code.</returns>
        public ExitCodes StartExecution()
        {
            if (Verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write("Starting execution on device...");
            }

            try
            {
                _swd.ResumeCore();
                _swd.SystemReset();
            }
            catch (SwdProtocolException ex)
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

            if (!string.IsNullOrEmpty(ProbeName))
            {
                deviceInfo.AppendLine($"Probe: {ProbeName}");
            }

            deviceInfo.AppendLine($"Probe ID: {ProbeId}");

            if (!string.IsNullOrEmpty(DeviceName))
            {
                deviceInfo.AppendLine($"Device: {DeviceName}");
            }

            if (!string.IsNullOrEmpty(DeviceCPU))
            {
                deviceInfo.AppendLine($"CPU: {DeviceCPU}");
            }

            deviceInfo.AppendLine($"DP IDCODE: 0x{DpIdcode:X8}");
            deviceInfo.AppendLine("Interface: Native ST-LINK SWD");

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
                    _swd?.Dispose();
                    _stLink?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
