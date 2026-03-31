// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Runs STM32 specific operations.
    /// </summary>
    public class Stm32Operations
    {
        /// <summary>
        /// Updates the device firmware.
        /// </summary>
        /// <param name="targetName">The name of the target.</param>
        /// <param name="fwVersion">The firmware version to send.</param>
        /// <param name="preview">Whether preview packages should be used.</param>
        /// <param name="archiveDirectoryPath">Path to the archive directory where all targets are located. Pass <c>null</c> if there is no archive.
        /// If not <c>null</c>, the package will always be retrieved from the archive and never be downloaded.</param>
        /// <param name="updateFw">Update firmware to latest version.</param>
        /// <param name="applicationPath">Path to the directory where the files are located.</param>
        /// <param name="deploymentAddress">The start memory address.</param>
        /// <param name="dfuDeviceId">The DFU device ID.</param>
        /// <param name="jtagId">The JTAG ID.</param>
        /// <param name="serialPort">Serial port for UART bootloader connection (e.g. COM3).</param>
        /// <param name="fitCheck">Checks whether the firmware will fit.</param>
        /// <param name="updateInterface">The connection interface.</param>
        /// <param name="verbosity">The verbosity level to use.</param>
        /// <returns>The outcome.</returns>
        public static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
            string targetName,
            string fwVersion,
            bool preview,
            string archiveDirectoryPath,
            bool updateFw,
            string applicationPath,
            string deploymentAddress,
            string dfuDeviceId,
            string jtagId,
            string serialPort,
            bool fitCheck,
            Interface updateInterface,
            VerbosityLevel verbosity,
            bool verify = false)
        {
            bool isApplicationBinFile = false;
            StmDfuDevice dfuDevice;
            StmJtagDevice jtagDevice;

            // if a target name wasn't specified use the default (and only available) ESP32 target
            if (string.IsNullOrEmpty(targetName))
            {
                return ExitCodes.E1000;
            }

            Stm32Firmware firmware = new Stm32Firmware(
                targetName,
                fwVersion,
                preview)
            {
                Verbosity = verbosity
            };

            // need to download update package?
            if (updateFw)
            {
                ExitCodes operationResult = await firmware.DownloadAndExtractAsync(archiveDirectoryPath);
                if (operationResult != ExitCodes.OK)
                {
                    return operationResult;
                }
                // download successful
            }

            // setup files to flash
            var filesToFlash = new List<string>();

            if (updateFw)
            {
                filesToFlash.Add(firmware.NanoBooterFile);
                filesToFlash.Add(firmware.NanoClrFile);
            }

            // need to include application file?
            if (!string.IsNullOrEmpty(applicationPath))
            {
                // check application file
                if (File.Exists(applicationPath))
                {
                    // check if application is BIN or HEX file
                    if (Path.GetExtension(applicationPath) == "hex")
                    {
                        // HEX we are good with adding it to the flash package
                        filesToFlash.Add(new FileInfo(applicationPath).FullName);
                    }
                    else
                    {
                        // BIN app, set flag
                        isApplicationBinFile = true;
                    }
                }
                else
                {
                    return ExitCodes.E9008;
                }
            }

            List<(string serial, string device)> connectedStDfuDevices = new List<(string serial, string device)>();
            List<string> connectedStJtagDevices = new List<string>();

            // Only enumerate CLI-based devices when we might actually use them
            bool needCliEnumeration = updateInterface == Interface.Dfu
                                     || updateInterface == Interface.Jtag
                                     || updateInterface == Interface.None;

            if (needCliEnumeration)
            {
                try
                {
                    connectedStDfuDevices = StmDfuDevice.ListDevices();
                }
                catch
                {
                    // CLI tool not available — that's OK, native paths may work
                }

                try
                {
                    connectedStJtagDevices = StmJtagDevice.ListDevices();
                }
                catch
                {
                    // CLI tool not available — that's OK, native paths may work
                }
            }

            if (updateInterface == Interface.Uart)
            {
                // UART bootloader path — requires serial port
                if (string.IsNullOrEmpty(serialPort))
                {
                    return ExitCodes.E6001;
                }
            }
            else if (updateInterface == Interface.NativeDfu)
            {
                // Native USB DFU — cross-platform, no CLI needed
            }
            else if (updateInterface == Interface.NativeSwd)
            {
                // Native SWD via CMSIS-DAP — cross-platform, no CLI needed
            }
            else if (updateInterface == Interface.NativeStLink)
            {
                // Native SWD via ST-LINK V2/V3 — cross-platform, no CLI needed
            }
            else if (updateInterface == Interface.Jtag)
            {
                // --jtag specified: try native ST-LINK first, then CMSIS-DAP, then CLI
                bool nativeFound = false;

                try
                {
                    var nativeStLinkProbes = StmStLinkDevice.ListDevices();

                    if (nativeStLinkProbes.Count > 0)
                    {
                        if (verbosity >= VerbosityLevel.Detailed)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                            OutputWriter.WriteLine("Found ST-LINK probe — using native ST-LINK transport (no CLI tools needed).");
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                        }

                        updateInterface = Interface.NativeStLink;
                        nativeFound = true;
                    }
                }
                catch
                {
                    // Native ST-LINK enumeration not available
                }

                if (!nativeFound)
                {
                    try
                    {
                        var nativeSwdProbes = StmSwdDevice.ListDevices();

                        if (nativeSwdProbes.Count > 0)
                        {
                            if (verbosity >= VerbosityLevel.Detailed)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                                OutputWriter.WriteLine("Found CMSIS-DAP probe — using native SWD transport (no CLI tools needed).");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                            }

                            updateInterface = Interface.NativeSwd;
                            nativeFound = true;
                        }
                    }
                    catch
                    {
                        // Native SWD enumeration not available
                    }
                }

                if (!nativeFound && !connectedStJtagDevices.Any())
                {
                    // no JTAG device was found via any method
                    return ExitCodes.E5001;
                }
                // else: fall through with Interface.Jtag (CLI) or already set to native
            }
            else if (updateInterface == Interface.Dfu)
            {
                // --dfu specified: try native DFU first, then CLI
                try
                {
                    var nativeDfuDevices = StmNativeDfuDevice.ListDevices();

                    if (nativeDfuDevices.Count > 0)
                    {
                        if (verbosity >= VerbosityLevel.Detailed)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                            OutputWriter.WriteLine("Found DFU device — using native USB DFU (no CLI tools needed).");
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                        }

                        updateInterface = Interface.NativeDfu;
                    }
                    else if (!connectedStDfuDevices.Any())
                    {
                        // no DFU device was found via any method
                        return ExitCodes.E1000;
                    }
                    // else: fall through with Interface.Dfu (CLI)
                }
                catch
                {
                    // Native enumeration failed — check CLI
                    if (!connectedStDfuDevices.Any())
                    {
                        return ExitCodes.E1000;
                    }
                }
            }
            else if (updateInterface != Interface.None)
            {
                // unknown interface specified (shouldn't happen)
            }
            else
            {
                // Interface.None — auto-detect the best available interface
                // Priority: CLI JTAG/DFU (already enumerated) → Native ST-LINK → Native CMSIS-DAP → Native DFU

                // If CLI enumeration already found devices, use them directly without probing native transports
                if (jtagId != null || connectedStJtagDevices.Any())
                {
                    updateInterface = Interface.Jtag;
                }
                else if (dfuDeviceId != null || connectedStDfuDevices.Any())
                {
                    updateInterface = Interface.Dfu;
                }
                else
                {
                    // No CLI devices found — try native transports
                    bool foundNative = false;

                    try
                    {
                        var nativeStLinkProbes = StmStLinkDevice.ListDevices();

                        if (nativeStLinkProbes.Count > 0)
                        {
                            if (verbosity >= VerbosityLevel.Detailed)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                                OutputWriter.WriteLine("Auto-detected ST-LINK probe — using native transport.");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                            }

                            updateInterface = Interface.NativeStLink;
                            foundNative = true;
                        }
                    }
                    catch
                    {
                        // Native ST-LINK enumeration not available
                    }

                    if (!foundNative)
                    {
                        try
                        {
                            var nativeSwdProbes = StmSwdDevice.ListDevices();

                            if (nativeSwdProbes.Count > 0)
                            {
                                if (verbosity >= VerbosityLevel.Detailed)
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                                    OutputWriter.WriteLine("Auto-detected CMSIS-DAP probe — using native SWD transport.");
                                    OutputWriter.ForegroundColor = ConsoleColor.White;
                                }

                                updateInterface = Interface.NativeSwd;
                                foundNative = true;
                            }
                        }
                        catch
                        {
                            // Native SWD enumeration not available
                        }
                    }

                    if (!foundNative)
                    {
                        try
                        {
                            var nativeDfuDevices = StmNativeDfuDevice.ListDevices();

                            if (nativeDfuDevices.Count > 0)
                            {
                                if (verbosity >= VerbosityLevel.Detailed)
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                                    OutputWriter.WriteLine("Auto-detected DFU device — using native USB DFU.");
                                    OutputWriter.ForegroundColor = ConsoleColor.White;
                                }

                                updateInterface = Interface.NativeDfu;
                            }
                        }
                        catch
                        {
                            // Native DFU enumeration not available on this platform
                        }
                    }
                }
            }

            if (updateInterface != Interface.Uart
                && updateInterface != Interface.NativeDfu
                && updateInterface != Interface.NativeSwd
                && updateInterface != Interface.NativeStLink
                && !connectedStDfuDevices.Any()
                && !connectedStJtagDevices.Any())
            {
                // no device was found
                return ExitCodes.E9010;
            }

            // update using DFU
            if (updateInterface == Interface.Uart)
            {
                // UART bootloader update — no external tools required

                try
                {
                    using Stm32UartDevice uartDevice = new Stm32UartDevice(serialPort);

                    if (!uartDevice.DevicePresent)
                    {
                        return ExitCodes.E5020;
                    }

                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                        OutputWriter.WriteLine($"Connected to STM32 via UART bootloader on {serialPort}");
                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine($"{uartDevice}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    if (fitCheck)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine("Image fit check is not supported for UART bootloader connections.");
                        OutputWriter.WriteLine("");

                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    ExitCodes operationResult = ExitCodes.OK;

                    // set verbosity
                    uartDevice.Verbosity = verbosity;

                    // UART bootloader requires flash to be erased before writing.
                    // Mass erase when performing firmware update.
                    uartDevice.DoMassErase = updateFw;

                    uartDevice.Verify = verify;

                    // write HEX files to flash
                    if (filesToFlash.Any(f => f.EndsWith(".hex")))
                    {
                        operationResult = uartDevice.FlashHexFiles(filesToFlash);
                    }

                    if (operationResult == ExitCodes.OK && isApplicationBinFile)
                    {
                        operationResult = uartDevice.FlashBinFiles([applicationPath], [deploymentAddress]);
                    }

                    if (updateFw
                        && operationResult == ExitCodes.OK)
                    {
                        // start execution from bootloader address
                        uartDevice.StartExecution($"{firmware.BooterStartAddress:X8}");
                    }

                    return operationResult;
                }
                catch (Stm32UartBootloaderException)
                {
                    return ExitCodes.E5020;
                }
                catch (Exception)
                {
                    return ExitCodes.E5021;
                }
            }
            else if (updateInterface == Interface.NativeDfu)
            {
                // Native USB DFU update — no external tools required (Windows only)

                try
                {
                    using StmNativeDfuDevice nativeDfuDevice = new StmNativeDfuDevice(dfuDeviceId);

                    if (!nativeDfuDevice.DevicePresent)
                    {
                        return ExitCodes.E1000;
                    }

                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                        OutputWriter.WriteLine($"Connected to DFU device with ID {nativeDfuDevice.DfuId}");
                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine($"{nativeDfuDevice}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    if (fitCheck)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine("It's not possible to perform image fit check for devices connected with DFU");
                        OutputWriter.WriteLine("");

                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    ExitCodes operationResult = ExitCodes.OK;

                    // set verbosity
                    nativeDfuDevice.Verbosity = verbosity;

                    // Native DFU requires mass erase before firmware update.
                    nativeDfuDevice.DoMassErase = updateFw;

                    // write HEX files to flash
                    if (filesToFlash.Any(f => f.EndsWith(".hex")))
                    {
                        operationResult = nativeDfuDevice.FlashHexFiles(filesToFlash);
                    }

                    if (operationResult == ExitCodes.OK && isApplicationBinFile)
                    {
                        operationResult = nativeDfuDevice.FlashBinFiles([applicationPath], [deploymentAddress]);
                    }

                    if (updateFw
                        && operationResult == ExitCodes.OK)
                    {
                        // start execution on MCU from bootloader address
                        nativeDfuDevice.StartExecution($"{firmware.BooterStartAddress:X8}");
                    }

                    return operationResult;
                }
                catch (CantConnectToDfuDeviceException)
                {
                    return ExitCodes.E1005;
                }
                catch (Exception)
                {
                    return ExitCodes.E5031;
                }
            }
            else if (updateInterface == Interface.NativeSwd)
            {
                // Native SWD via CMSIS-DAP — no external tools required

                try
                {
                    using StmSwdDevice swdDevice = new StmSwdDevice(jtagId);

                    if (!swdDevice.DevicePresent)
                    {
                        return ExitCodes.E5001;
                    }

                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                        OutputWriter.WriteLine($"Connected to target via CMSIS-DAP probe {swdDevice.ProbeId}");
                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine($"{swdDevice}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    if (fitCheck)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine("Image fit check is not supported for native SWD connections.");
                        OutputWriter.WriteLine("");

                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    ExitCodes operationResult = ExitCodes.OK;

                    // set verbosity
                    swdDevice.Verbosity = verbosity;

                    // mass erase when performing firmware update
                    swdDevice.DoMassErase = updateFw;

                    swdDevice.Verify = verify;

                    // write HEX files to flash
                    if (filesToFlash.Any(f => f.EndsWith(".hex")))
                    {
                        operationResult = swdDevice.FlashHexFiles(filesToFlash);
                    }

                    if (operationResult == ExitCodes.OK && isApplicationBinFile)
                    {
                        operationResult = swdDevice.FlashBinFiles([applicationPath], [deploymentAddress]);
                    }

                    if (updateFw
                        && operationResult == ExitCodes.OK)
                    {
                        // reset MCU to start running
                        swdDevice.ResetMcu();
                    }

                    return operationResult;
                }
                catch (CantConnectToJtagDeviceException)
                {
                    return ExitCodes.E5002;
                }
                catch (Exception)
                {
                    return ExitCodes.E5041;
                }
            }
            else if (updateInterface == Interface.NativeStLink)
            {
                // Native SWD via ST-LINK V2/V3 — no external tools required

                try
                {
                    using StmStLinkDevice stLinkDevice = new StmStLinkDevice(jtagId);

                    if (!stLinkDevice.DevicePresent)
                    {
                        return ExitCodes.E5001;
                    }

                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                        OutputWriter.WriteLine($"Connected to target via ST-LINK probe {stLinkDevice.ProbeId}");
                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine($"{stLinkDevice}");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    if (fitCheck)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine("Image fit check is not supported for native ST-LINK connections.");
                        OutputWriter.WriteLine("");

                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    ExitCodes operationResult = ExitCodes.OK;

                    // set verbosity
                    stLinkDevice.Verbosity = verbosity;

                    // mass erase when performing firmware update
                    stLinkDevice.DoMassErase = updateFw;

                    stLinkDevice.Verify = verify;

                    // write HEX files to flash
                    if (filesToFlash.Any(f => f.EndsWith(".hex")))
                    {
                        operationResult = stLinkDevice.FlashHexFiles(filesToFlash);
                    }

                    if (operationResult == ExitCodes.OK && isApplicationBinFile)
                    {
                        operationResult = stLinkDevice.FlashBinFiles([applicationPath], [deploymentAddress]);
                    }

                    if (updateFw
                        && operationResult == ExitCodes.OK)
                    {
                        // reset MCU to start running
                        stLinkDevice.ResetMcu();
                    }

                    return operationResult;
                }
                catch (CantConnectToJtagDeviceException)
                {
                    return ExitCodes.E5002;
                }
                catch (Exception)
                {
                    return ExitCodes.E5041;
                }
            }
            else if (updateInterface == Interface.Dfu)
            {
                // DFU update

                try
                {
                    if (dfuDeviceId != null)
                    {
                        // verify the specified ID exists in the list
                        if (!connectedStDfuDevices.Any(d => d.serial == dfuDeviceId))
                        {
                            return ExitCodes.E1005;
                        }
                    }
                    else
                    {
                        // no ID specified — use the first available device
                        dfuDeviceId = connectedStDfuDevices[0].serial;
                    }

                    dfuDevice = new StmDfuDevice(dfuDeviceId);

                    if (!dfuDevice.DevicePresent)
                    {
                        // no DFU device found

                        // done here, this command has no further processing
                        return ExitCodes.E1000;
                    }
                }
                catch (CantConnectToJtagDeviceException)
                {
                    return ExitCodes.E5002;
                }
                catch (Exception)
                {
                    return ExitCodes.E5000;
                }

                if (fitCheck)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine("It's not possible to perform image fit check for devices connected with DFU");
                    OutputWriter.WriteLine("");

                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                    OutputWriter.WriteLine($"Connected to DFU device with ID {dfuDevice.DfuId}");
                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine($"{dfuDevice}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // set verbosity
                dfuDevice.Verbosity = verbosity;

                ExitCodes operationResult = ExitCodes.OK;

                // set verbosity
                dfuDevice.Verbosity = verbosity;

                // write HEX files to flash
                if (filesToFlash.Any(f => f.EndsWith(".hex")))
                {
                    operationResult = dfuDevice.FlashHexFiles(filesToFlash);
                }

                if (operationResult == ExitCodes.OK && isApplicationBinFile)
                {
                    // now program the application file
                    operationResult = dfuDevice.FlashBinFiles([applicationPath], [deploymentAddress]);
                }

                if (
                    updateFw
                    && operationResult == ExitCodes.OK)
                {
                    // start execution on MCU from with bootloader address
                    dfuDevice.StartExecution($"{firmware.BooterStartAddress:X8}");
                }

                return operationResult;
            }
            else
            {
                // JTAG device

                try
                {
                    if (jtagId != null)
                    {
                        // verify the specified ID exists in the list
                        if (!connectedStJtagDevices.Contains(jtagId))
                        {
                            return ExitCodes.E5002;
                        }
                    }
                    else
                    {
                        // no ID specified — use the first available device
                        jtagId = connectedStJtagDevices[0];
                    }

                    jtagDevice = new StmJtagDevice(jtagId);

                    if (!jtagDevice.DevicePresent)
                    {
                        // no JTAG device found

                        // done here, this command has no further processing
                        return ExitCodes.E5001;
                    }
                }
                catch (CantConnectToJtagDeviceException)
                {
                    return ExitCodes.E5002;
                }
                catch (Exception)
                {
                    return ExitCodes.E5000;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine("");
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine($"{jtagDevice}");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                if (fitCheck)
                {
                    PerformTargetCheck(targetName, jtagDevice);
                }

                ExitCodes operationResult = ExitCodes.OK;

                // set verbosity
                jtagDevice.Verbosity = verbosity;

                // write HEX files to flash
                if (filesToFlash.Any(f => f.EndsWith(".hex")))
                {
                    operationResult = jtagDevice.FlashHexFiles(filesToFlash);
                }

                if (operationResult == ExitCodes.OK && isApplicationBinFile)
                {
                    // now program the application file
                    operationResult = jtagDevice.FlashBinFiles([applicationPath], [deploymentAddress]);
                }

                if (
                    updateFw
                    && operationResult == ExitCodes.OK)
                {
                    // reset MCU
                    jtagDevice.ResetMcu();
                }

                return operationResult;
            }
        }

        private static void PerformTargetCheck(string target, StmJtagDevice jtagDevice)
        {
            string boardName;

            // tweak our target name trying to mach ST names
            string targetName = target.ToUpper().Replace("ST_", "").Replace("NUCLEO64", "NUCLEO").Replace("NUCLEO144", "NUCLEO").Replace("STM", "").Replace("-", "_").Replace("_", "");

            // check if there is a board name available
            if (
                string.IsNullOrEmpty(jtagDevice.BoardName)
                || jtagDevice.BoardName == "--")
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                OutputWriter.WriteLine("");
                OutputWriter.WriteLine("******************************************* WARNING ************************ *************");
                OutputWriter.WriteLine("It wasn't possible to validate if the firmware image that's about to be used works on the");
                OutputWriter.WriteLine($"target connected. But this doesn't necessarily mean that it won't work.");
                OutputWriter.WriteLine("******************************************************************************************");
                OutputWriter.WriteLine("");

                OutputWriter.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                // do some parsing to match our target names
                boardName = jtagDevice.BoardName.Replace("-", "_");

                if (!targetName.Contains(boardName))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine("******************************************* WARNING ***************************************");
                    OutputWriter.WriteLine("It seems that the firmware image that's about to be used isn't the appropriate one for the");
                    OutputWriter.WriteLine($"target connected. But this doesn't necessarily mean that it won't work.");
                    OutputWriter.WriteLine("*******************************************************************************************");
                    OutputWriter.WriteLine("");

                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }
            }
        }

        /// <summary>
        /// Resets the device.
        /// </summary>
        /// <param name="jtagId">the JTAG ID.</param>
        /// <param name="verbosity">The verbosity level.</param>
        /// <returns>The outcome.</returns>
        public static ExitCodes ResetMcu(
            string jtagId,
            VerbosityLevel verbosity)
        {
            // Try native ST-LINK first
            try
            {
                var stLinkProbes = StmStLinkDevice.ListDevices();

                if (stLinkProbes.Count > 0)
                {
                    using StmStLinkDevice stLinkDevice = new StmStLinkDevice(jtagId);

                    if (stLinkDevice.DevicePresent)
                    {
                        if (verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine($"Connected to target via ST-LINK probe {stLinkDevice.ProbeId}");
                        }

                        stLinkDevice.Verbosity = verbosity;
                        return stLinkDevice.ResetMcu();
                    }
                }
            }
            catch
            {
                // Native ST-LINK enumeration not available
            }

            // Try native CMSIS-DAP
            try
            {
                var swdProbes = StmSwdDevice.ListDevices();

                if (swdProbes.Count > 0)
                {
                    using StmSwdDevice swdDevice = new StmSwdDevice(jtagId);

                    if (swdDevice.DevicePresent)
                    {
                        if (verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine($"Connected to target via CMSIS-DAP probe {swdDevice.ProbeId}");
                        }

                        swdDevice.Verbosity = verbosity;
                        return swdDevice.ResetMcu();
                    }
                }
            }
            catch
            {
                // Native SWD enumeration not available
            }

            // Fall back to CLI JTAG
            StmJtagDevice jtagDevice = new StmJtagDevice(jtagId);

            if (!jtagDevice.DevicePresent)
            {
                return ExitCodes.E5001;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
            }

            jtagDevice.Verbosity = verbosity;
            return jtagDevice.ResetMcu();
        }

        /// <summary>
        /// Erases the device flash memory.
        /// </summary>
        /// <param name="jtagId">The ID of the JTAG interface.</param>
        /// <param name="verbosity">The verbosity level.</param>
        /// <returns>The outcome.</returns>
        public static ExitCodes MassErase(
            string jtagId,
            VerbosityLevel verbosity)
        {
            // Try native ST-LINK first
            try
            {
                var stLinkProbes = StmStLinkDevice.ListDevices();

                if (stLinkProbes.Count > 0)
                {
                    using StmStLinkDevice stLinkDevice = new StmStLinkDevice(jtagId);

                    if (stLinkDevice.DevicePresent)
                    {
                        if (verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine($"Connected to target via ST-LINK probe {stLinkDevice.ProbeId}");
                        }

                        stLinkDevice.Verbosity = verbosity;
                        return stLinkDevice.MassErase();
                    }
                }
            }
            catch
            {
                // Native ST-LINK enumeration not available
            }

            // Try native CMSIS-DAP
            try
            {
                var swdProbes = StmSwdDevice.ListDevices();

                if (swdProbes.Count > 0)
                {
                    using StmSwdDevice swdDevice = new StmSwdDevice(jtagId);

                    if (swdDevice.DevicePresent)
                    {
                        if (verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine($"Connected to target via CMSIS-DAP probe {swdDevice.ProbeId}");
                        }

                        swdDevice.Verbosity = verbosity;
                        return swdDevice.MassErase();
                    }
                }
            }
            catch
            {
                // Native SWD enumeration not available
            }

            // Try native DFU
            try
            {
                var nativeDfuDevices = StmNativeDfuDevice.ListDevices();

                if (nativeDfuDevices.Count > 0)
                {
                    using StmNativeDfuDevice dfuDevice = new StmNativeDfuDevice(jtagId);

                    if (dfuDevice.DevicePresent)
                    {
                        if (verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.WriteLine($"Connected to DFU device — using native USB DFU for mass erase");
                        }

                        dfuDevice.Verbosity = verbosity;
                        return dfuDevice.MassErase();
                    }
                }
            }
            catch
            {
                // Native DFU enumeration not available
            }

            // Fall back to CLI JTAG
            StmJtagDevice jtagDevice = new StmJtagDevice(jtagId);

            if (!jtagDevice.DevicePresent)
            {
                return ExitCodes.E5001;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
            }

            jtagDevice.Verbosity = verbosity;
            return jtagDevice.MassErase();
        }

        /// <summary>
        /// Installs DFU driver.
        /// </summary>
        /// <param name="verbosityLevel">The verbosity level to display.</param>
        /// <returns>The installation result.</returns>
        /// <exception cref="Exception">The installation failed. Use verbose output to see why.</exception>
        public static ExitCodes InstallDfuDrivers(VerbosityLevel verbosityLevel)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                OutputWriter.WriteLine("No driver installation needed on MacOS");
                return ExitCodes.OK;
            }

            try
            {
                // In case Linux, we just need to copy the rules files
                // It does require elevated privileges
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process installerLinux = new Process
                    {
                        StartInfo = new ProcessStartInfo("sudo")
                        {
                            Arguments = $"cp *.rules /etc/udev/rules.d",
                            WorkingDirectory = Path.Combine(Utilities.ExecutingPath, "stlinkLinux", "Drivers", "rules"),
                            UseShellExecute = true
                        }
                    };

                    // execution command and...
                    installerLinux.Start();

                    // ... wait for exit
                    installerLinux.WaitForExit();

                    if (verbosityLevel >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Green;
                        OutputWriter.WriteLine("OK");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    return ExitCodes.OK;
                }

                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                    OutputWriter.Write("Calling installer for STM32 DFU drivers...");
                }

                string infPath = Path.Combine(Utilities.ExecutingPath, "stlink\\DFU_Driver\\Driver\\STM32Bootloader.inf");

                if (!File.Exists(infPath))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine("DFU driver files not found. The STM32 CLI tools may have been excluded from the package.");
                    OutputWriter.WriteLine("Native transports (--nativedfu, --nativestlink, --nativeswd) work without driver installation.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E5000;
                }

                Process installerCli = new Process
                {
                    StartInfo = new ProcessStartInfo("pnputil")
                    {
                        Arguments = $"-i -a {infPath}",
                        WorkingDirectory = Path.Combine(Utilities.ExecutingPath, "stlink\\DFU_Driver"),
                        UseShellExecute = true
                    }
                };

                // execution command and...
                installerCli.Start();

                // ... wait for exit
                installerCli.WaitForExit();

                string installerPath;

                if (Environment.Is64BitOperatingSystem)
                {
                    installerPath = Path.Combine(Utilities.ExecutingPath, "stlink\\DFU_Driver\\Driver\\installer_x64.exe");
                }
                else
                {
                    installerPath = Path.Combine(Utilities.ExecutingPath, "stlink\\DFU_Driver\\Driver\\installer_x86.exe");
                }

                installerCli = new Process
                {
                    StartInfo = new ProcessStartInfo(installerPath)
                    {
                        WorkingDirectory = Path.Combine(Utilities.ExecutingPath, "stlink\\DFU_Driver\\Driver"),
                        UseShellExecute = true
                    }
                };

                // execution command and...
                installerCli.Start();

                // ... wait for exit
                installerCli.WaitForExit();

                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine("OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // always true as the drivers will be installed depending on user answering yes to elevate prompt
                // any errors or exceptions will be presented by the installer
                return ExitCodes.OK;
            }
            catch (Exception ex)
            {
                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine("ERROR");
                }

                throw new Exception(ex.Message);
            }
        }

        /// <summary>
        /// Installs the JTAG drivers.
        /// </summary>
        /// <param name="verbosityLevel">Message verbosity level.</param>
        /// <returns>Installation result.</returns>
        /// <exception cref="Exception">The installation failed. Use verbose output to see why.</exception>
        public static ExitCodes InstallJtagDrivers(VerbosityLevel verbosityLevel)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                OutputWriter.WriteLine("No driver installation needed on MacOS");
                return ExitCodes.OK;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                OutputWriter.WriteLine("Driver installation for JTAG no supported on Linux. Please refer to the STM32 website to get the specific drivers.");
                return ExitCodes.OK;
            }

            OutputWriter.ForegroundColor = ConsoleColor.Cyan;

            if (verbosityLevel >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                OutputWriter.Write("Calling installer for STM32 JTAG drivers...");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            try
            {
                string installerPath;

                if (Environment.Is64BitOperatingSystem)
                {
                    installerPath = Path.Combine(Utilities.ExecutingPath, "stlink\\stsw-link009_v3\\dpinst_amd64.exe");
                }
                else
                {
                    installerPath = Path.Combine(Utilities.ExecutingPath, "stlink\\stsw-link009_v3\\dpinst_x86.exe");
                }

                if (!File.Exists(installerPath))
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine("");
                    OutputWriter.WriteLine("JTAG driver files not found. The STM32 CLI tools may have been excluded from the package.");
                    OutputWriter.WriteLine("Native transports (--nativestlink, --nativeswd) work without driver installation.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E5000;
                }

                Process installerCli = new Process
                {
                    StartInfo = new ProcessStartInfo(installerPath)
                    {
                        WorkingDirectory = Path.Combine(Utilities.ExecutingPath, "stlink\\stsw-link009_v3"),
                        UseShellExecute = true
                    }
                };

                // execution command and...
                installerCli.Start();

                // ... wait for exit
                installerCli.WaitForExit();

                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                    OutputWriter.WriteLine("OK");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // always true as the drivers will be installed depending on user answering yes to elevate prompt
                // any errors or exceptions will be presented by the installer
                return ExitCodes.OK;
            }
            catch (Exception ex)
            {
                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine("ERROR");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                throw new Exception(ex.Message);
            }
        }

    }

    /// <summary>
    /// The device connection interface.
    /// </summary>
    public enum Interface
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,
        /// <summary>
        /// JTAG.
        /// </summary>
        Jtag,
        /// <summary>
        /// DFU.
        /// </summary>
        Dfu,
        /// <summary>
        /// UART bootloader (native, no external tools required).
        /// </summary>
        Uart,
        /// <summary>
        /// Native USB DFU (WinUSB, no external tools required). Windows only.
        /// </summary>
        NativeDfu,
        /// <summary>
        /// Native SWD via CMSIS-DAP (USB HID, no external tools required).
        /// </summary>
        NativeSwd,
        /// <summary>
        /// Native SWD via ST-LINK V2/V3 (USB bulk, no external tools required).
        /// </summary>
        NativeStLink
    }
}
