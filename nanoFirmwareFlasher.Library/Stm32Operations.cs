//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

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
        /// <param name="updateFw">Update firmware to latest version.</param>
        /// <param name="applicationPath">Path to the directory where the files are located.</param>
        /// <param name="deploymentAddress">The start memory address.</param>
        /// <param name="dfuDeviceId">The DFU device ID.</param>
        /// <param name="jtagId">The JTAG ID.</param>
        /// <param name="fitCheck">Checks whether the firmware will fit.</param>
        /// <param name="updateInterface">The connection interface.</param>
        /// <param name="verbosity">The verbosity level to use.</param>
        /// <returns>The outcome.</returns>
        public static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
            string targetName,
            string fwVersion,
            bool preview,
            bool updateFw,
            string applicationPath,
            string deploymentAddress,
            string dfuDeviceId,
            string jtagId,
            bool fitCheck,
            Interface updateInterface,
            VerbosityLevel verbosity)
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
                var operationResult = await firmware.DownloadAndExtractAsync();
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

            var connectedStDfuDevices = StmDfuDevice.ListDevices();
            var connectedStJtagDevices = StmJtagDevice.ListDevices();

            if (updateInterface != Interface.None)
            {
                // check specified interface option
                if (updateInterface == Interface.Dfu
                    && !connectedStDfuDevices.Any())
                {
                    // no DFU device was found to update.
                    return ExitCodes.E1000;
                }

                if (updateInterface == Interface.Jtag
                   && !connectedStJtagDevices.Any())
                {
                    // no JTAG device was found to update.
                    return ExitCodes.E5001;
                }
            }
            else
            {
                // try to make a smart guess on what interface to use
                // prefer JTAG for STM32 devices
                if (dfuDeviceId != null
                    || connectedStDfuDevices.Any())
                {
                    updateInterface = Interface.Dfu;
                }
                else if (jtagId != null
                         || connectedStJtagDevices.Any())
                {
                    updateInterface = Interface.Jtag;
                }
            }

            if (!connectedStDfuDevices.Any()
                && !connectedStJtagDevices.Any())
            {
                // no device was found
                return ExitCodes.E9010;
            }

            // update using DFU
            if (updateInterface == Interface.Dfu)
            {
                // DFU update

                try
                {
                    dfuDeviceId = dfuDeviceId == null ? connectedStDfuDevices[0].serial : dfuDeviceId;
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
                    Console.ForegroundColor = ConsoleColor.Yellow;

                    Console.WriteLine("");
                    Console.WriteLine("It's not possible to perform image fit check for devices connected with DFU");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;

                    Console.WriteLine($"Connected to DFU device with ID {dfuDevice.DfuId}");
                    Console.WriteLine("");
                    Console.WriteLine($"{dfuDevice}");
                    Console.ForegroundColor = ConsoleColor.White;
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
                    Console.WriteLine("");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
                    Console.WriteLine("");
                    Console.WriteLine($"{jtagDevice}");
                    Console.ForegroundColor = ConsoleColor.White;
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
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.WriteLine("");
                Console.WriteLine("******************************************* WARNING ************************ *************");
                Console.WriteLine("It wasn't possible to validate if the firmware image that's about to be used works on the");
                Console.WriteLine($"target connected. But this doesn't necessarily mean that it won't work.");
                Console.WriteLine("******************************************************************************************");
                Console.WriteLine("");

                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                // do some parsing to match our target names
                boardName = jtagDevice.BoardName.Replace("-", "_");

                if (!targetName.Contains(boardName))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;

                    Console.WriteLine("");
                    Console.WriteLine("******************************************* WARNING ***************************************");
                    Console.WriteLine("It seems that the firmware image that's about to be used isn't the appropriate one for the");
                    Console.WriteLine($"target connected. But this doesn't necessarily mean that it won't work.");
                    Console.WriteLine("*******************************************************************************************");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;
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
            // JATG device
            StmJtagDevice jtagDevice = new StmJtagDevice(jtagId);

            if (!jtagDevice.DevicePresent)
            {
                // no JTAG device found

                // done here, this command has no further processing
                return ExitCodes.E5001;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
            }

            // set verbosity
            jtagDevice.Verbosity = verbosity;

            // perform reset
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
            // JATG device
            StmJtagDevice jtagDevice = new StmJtagDevice(jtagId);

            if (!jtagDevice.DevicePresent)
            {
                // no JTAG device found

                // done here, this command has no further processing
                return ExitCodes.E5001;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine($"Connected to JTAG device with ID {jtagDevice.JtagId}");
            }

            // set verbosity
            jtagDevice.Verbosity = verbosity;

            // perform erase operation
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
                Console.WriteLine("No driver installation needed on MacOS");
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
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("OK");
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    return ExitCodes.OK;
                }

                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("Calling installer for STM32 DFU drivers...");
                }

                var infPath = Path.Combine(Utilities.ExecutingPath, "stlink\\DFU_Driver\\Driver\\STM32Bootloader.inf");

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
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK");
                    Console.ForegroundColor = ConsoleColor.White;
                }

                // always true as the drivers will be installed depending on user answering yes to elevate prompt
                // any errors or exceptions will be presented by the installer
                return ExitCodes.OK;
            }
            catch (Exception ex)
            {
                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR");
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
                Console.WriteLine("No driver installation needed on MacOS");
                return ExitCodes.OK;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Driver installation for JTAG no supported on Linux. Please refer to the STM32 website to get the specific drivers.");
                return ExitCodes.OK;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;

            if (verbosityLevel >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Calling installer for STM32 JTAG drivers...");
                Console.ForegroundColor = ConsoleColor.White;
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
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK");
                    Console.ForegroundColor = ConsoleColor.White;
                }

                // always true as the drivers will be installed depending on user answering yes to elevate prompt
                // any errors or exceptions will be presented by the installer
                return ExitCodes.OK;
            }
            catch (Exception ex)
            {
                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR");
                    Console.ForegroundColor = ConsoleColor.White;
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
        Dfu
    }
}
