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
        /// <param name="verify">Whether to verify the flash after programming when supported by the selected transport.</param>
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

            if (updateInterface == Interface.NativeDfu)
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
            else if (updateInterface != Interface.None)
            {
                // unknown interface specified (shouldn't happen)
            }
            else
            {
                // Interface.None — auto-detect the best available interface
                // Priority: native ST-LINK → native CMSIS-DAP → native DFU
                bool foundNative = false;
                var nativeStLinkProbes = new List<string>();

                try
                {
                    nativeStLinkProbes = StmStLinkDevice.ListDevices();
                }
                catch
                {
                    // Native ST-LINK enumeration not available on this platform
                }

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

                if (!foundNative)
                {
                    var nativeSwdProbes = new List<string>();

                    try
                    {
                        nativeSwdProbes = StmSwdDevice.ListDevices();
                    }
                    catch
                    {
                        // Native SWD enumeration not available
                    }

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
                            foundNative = true;
                        }
                    }
                    catch
                    {
                        // Native DFU enumeration not available on this platform
                    }
                }
            }

            if (updateInterface == Interface.None)
            {
                // no device was found
                return ExitCodes.E9010;
            }

            // update using DFU
            if (updateInterface == Interface.NativeDfu)
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

            // no matching interface branch (should not happen — updateInterface can only
            // be NativeDfu, NativeSwd or NativeStLink at this point)
            return ExitCodes.E9010;
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

            // no native transport found
            return ExitCodes.E5001;
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

            // no native transport found
            return ExitCodes.E5001;
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
