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
            bool stable,
            bool updateFw,
            string applicationPath,
            string deploymentAddress,
            string dfuDeviceId,
            string jtagId,
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
                stable)
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
                filesToFlash.Add(firmware.nanoBooterFile);
                filesToFlash.Add(firmware.nanoCLRFile);
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

            // need DFU or JTAG device
            if (firmware.HasDfuPackage)
            {
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
                // JATG device
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
                    programResult = jtagDevice.FlashBinFiles(new List<string>() { applicationPath }, new List<string>() { deploymentAddress });
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
}
