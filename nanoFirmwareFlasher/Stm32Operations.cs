//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            var connectedStDfuDevices = StmDfuDevice.ListDfuDevices();
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

            if(!connectedStDfuDevices.Any()
                && !connectedStJtagDevices.Any())
            {
                // no device was found
                return ExitCodes.E9010;
            }

            // update using DFU
            if (updateInterface == Interface.Dfu)
            {
                 if(!firmware.HasDfuPackage)
                {
                    // firmware doesn't have a DFU package
                    return ExitCodes.E1004;
                }

                // DFU package
                dfuDevice = new StmDfuDevice(dfuDeviceId);

                if (!dfuDevice.DevicePresent)
                {
                    // no DFU device found

                    // done here, this command has no further processing
                    return ExitCodes.E1000;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine($"Connected to DFU device with ID { dfuDevice.DeviceId }");
                }

                // set verbosity
                dfuDevice.Verbosity = verbosity;

                try
                {
                    dfuDevice.FlashDfuFile(firmware.DfuPackage);

                    // done here, this command has no further processing
                    return ExitCodes.OK;
                }
                catch (Exception)
                {
                    // exception
                    return ExitCodes.E1003;
                }
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
                    Console.WriteLine($"Connected to JTAG device with ID { jtagDevice.DeviceId }");
                }

                // set verbosity
                jtagDevice.Verbosity = verbosity;

                ExitCodes programResult = ExitCodes.OK;

                // write HEX files to flash
                if ( filesToFlash.Any(f => f.EndsWith(".hex")) ) 
                {
                    programResult = jtagDevice.FlashHexFiles(filesToFlash);
                }

                if (programResult == ExitCodes.OK && isApplicationBinFile)
                {
                    // now program the application file
                    programResult = jtagDevice.FlashBinFiles(new [] { applicationPath }, new [] { deploymentAddress });
                }

                if(updateFw)
                {
                    // reset MCU
                    jtagDevice.ResetMcu();
                }

                return programResult;
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
                Console.WriteLine($"Connected to JTAG device with ID { jtagDevice.DeviceId }");
            }

            // set verbosity
            jtagDevice.Verbosity = verbosity;

            // perform reset
            return jtagDevice.ResetMcu();
        }
    }

    public enum Interface
    {
        None = 0,

        Jtag,

        Dfu
    }
}
