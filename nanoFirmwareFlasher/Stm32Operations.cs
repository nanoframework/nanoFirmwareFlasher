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
    internal class Stm32Operations
    {
        internal static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
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
                dfuDevice = new StmDfuDevice(dfuDeviceId);

                if (!dfuDevice.DevicePresent)
                {
                    // no DFU device found

                    // done here, this command has no further processing
                    return ExitCodes.E1000;
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

                    Console.WriteLine($"Connected to DFU device with ID { dfuDevice.DfuId }");
                    Console.WriteLine("");
                    Console.WriteLine($"{ dfuDevice }");
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
                    operationResult = dfuDevice.FlashBinFiles(new[] { applicationPath }, new[] { deploymentAddress });
                }

                if (
                    updateFw
                    && operationResult == ExitCodes.OK)
                {
                    // start execution on MCU from with bootloader address
                    dfuDevice.StartExecution($"{firmware.GetdBooterStartAddress():X8}");
                }

                return operationResult;
            }
            else
            {
                // JTAG device
                jtagDevice = new StmJtagDevice(jtagId);

                if (!jtagDevice.DevicePresent)
                {
                    // no JTAG device found

                    // done here, this command has no further processing
                    return ExitCodes.E5001;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine("");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Connected to JTAG device with ID { jtagDevice.JtagId }");
                    Console.WriteLine("");
                    Console.WriteLine($"{ jtagDevice }");
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
                    operationResult = jtagDevice.FlashBinFiles(new[] { applicationPath }, new[] { deploymentAddress });
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

        internal static ExitCodes ResetMcu(
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
                Console.WriteLine($"Connected to JTAG device with ID { jtagDevice.JtagId }");
            }

            // set verbosity
            jtagDevice.Verbosity = verbosity;

            // perform reset
            return jtagDevice.ResetMcu();
        }

        internal static ExitCodes MassErase(
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
                Console.WriteLine($"Connected to JTAG device with ID { jtagDevice.JtagId }");
            }

            // set verbosity
            jtagDevice.Verbosity = verbosity;

            // perform erase operation
            return jtagDevice.MassErase();
        }

        internal static ExitCodes InstallDfuDrivers(VerbosityLevel verbosityLevel)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Console.WriteLine("No driver installation needed on MacOS");
                return ExitCodes.OK;
            }

            try
            {
                if (verbosityLevel >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("Calling installer for STM32 DFU drivers...");
                }

                var infPath = Path.Combine(Program.ExecutingPath, "stlink\\DFU_Driver\\Driver\\STM32Bootloader.inf");

                Process installerCli = new Process
                {
                    StartInfo = new ProcessStartInfo("pnputil")
                    {
                        Arguments = $"-i -a {infPath}",
                        WorkingDirectory = Path.Combine(Program.ExecutingPath, "stlink\\DFU_Driver"),
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
                    installerPath = Path.Combine(Program.ExecutingPath, "stlink\\DFU_Driver\\Driver\\installer_x64.exe");
                }
                else
                {
                    installerPath = Path.Combine(Program.ExecutingPath, "stlink\\DFU_Driver\\Driver\\installer_x86.exe");
                }

                installerCli = new Process
                {
                    StartInfo = new ProcessStartInfo(installerPath)
                    {
                        WorkingDirectory = Path.Combine(Program.ExecutingPath, "stlink\\DFU_Driver\\Driver"),
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

        internal static ExitCodes InstallJtagDrivers(VerbosityLevel verbosityLevel)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Console.WriteLine("No driver installation needed on MacOS");
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
                    installerPath = Path.Combine(Program.ExecutingPath, "stlink\\stsw-link009_v3\\dpinst_amd64.exe");
                }
                else
                {
                    installerPath = Path.Combine(Program.ExecutingPath, "stlink\\stsw-link009_v3\\dpinst_x86.exe");
                }

                Process installerCli = new Process
                {
                    StartInfo = new ProcessStartInfo(installerPath)
                    {
                        WorkingDirectory = Path.Combine(Program.ExecutingPath, "stlink\\stsw-link009_v3"),
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

    public enum Interface
    {
        None = 0,

        Jtag,

        Dfu
    }
}
