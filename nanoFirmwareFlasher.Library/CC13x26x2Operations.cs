//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    public class CC13x26x2Operations
    {
        public static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
            string targetName,
            string fwVersion,
            bool preview,
            bool updateFw,
            string applicationPath,
            string deploymentAddress,
            VerbosityLevel verbosity)
        {
            bool isApplicationBinFile = false;

            // if a target name wasn't specified use the default (and only available) ESP32 target
            if (string.IsNullOrEmpty(targetName))
            {
                return ExitCodes.E1000;
            }

            CC13x26x2Firmware firmware = new CC13x26x2Firmware(
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


            // find Uniflash configuration file
            string configFile;

            if (targetName.Contains("CC1352R"))
            {
                configFile = Path.Combine(Utilities.ExecutingPath, "uniflash", "CC1352R1F3.ccxml");
            }
            else if (targetName.Contains("CC1352P"))
            {
                configFile = Path.Combine(Utilities.ExecutingPath, "uniflash", "CC1352P1F3.ccxml");
            }
            else
            {
                // no other MCUs supported
                return ExitCodes.E7000;
            }

            var ccDevice = new CC13x26x2Device(configFile) { Verbosity = verbosity };

            // set verbosity

            ExitCodes programResult = ExitCodes.OK;
            // write HEX files to flash
            if (filesToFlash.Any(f => f.EndsWith(".hex")))
            {
                programResult = ccDevice.FlashHexFiles(filesToFlash);
            }

            if (programResult == ExitCodes.OK && isApplicationBinFile)
            {
                // now program the application file
                programResult = ccDevice.FlashBinFiles(new[] { applicationPath }, new[] { deploymentAddress });
            }

            if (updateFw)
            {
                // reset MCU
                ccDevice.ResetMcu();
            }

            return programResult;
        }

        public static ExitCodes InstallXds110Drivers(VerbosityLevel verbosityLevel)
        {
            try
            {
                string driversPath = Path.Combine(Utilities.ExecutingPath, "uniflash\\emulation\\windows\\xds110_drivers");

                var uniflashCli = new Process
                {
                    StartInfo = new ProcessStartInfo(
                        Path.Combine(Utilities.ExecutingPath, "uniflash", "dpinst_64_eng.exe"),
                        $"/SE /SW /SA /PATH {driversPath}")
                    {
                        WorkingDirectory = Path.Combine(Utilities.ExecutingPath, "uniflash"),
                        // need to use ShellExecute to show elevate prompt
                        UseShellExecute = true,
                    }
                };

                // execution command and...
                uniflashCli.Start();

                // ... wait for exit
                uniflashCli.WaitForExit();

                // always true as the drivers will be installed depending on user answering yes to elevate prompt
                // any errors or exceptions will be presented by the installer
                return ExitCodes.OK;
            }
            catch (Exception ex)
            {
                throw new UniflashCliExecutionException(ex.Message);
            }
        }
    }
}
