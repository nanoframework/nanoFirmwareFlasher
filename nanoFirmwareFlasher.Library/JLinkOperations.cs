// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class with operations available in J-Link connected devices.
    /// </summary>
    public class JLinkOperations
    {
        /// <summary>
        /// Perform firmware update on a J-Link connected device.
        /// </summary>
        /// <param name="targetName">Name of the target to update.</param>
        /// <param name="fwVersion">Firmware version to update to.</param>
        /// <param name="preview">Set to <see langword="true"/> to use preview version to update.</param>
        /// <param name="archiveDirectoryPath">Path to the archive directory where all targets are located. Pass <c>null</c> if there is no archive.
        /// If not <c>null</c>, the package will always be retrieved from the archive and never be downloaded.</param>
        /// <param name="updateFw">Set to <see langword="true"/> to force download of firmware package.</param>
        /// <param name="applicationPath">Path to application to update along with the firmware update.</param>
        /// <param name="deploymentAddress">Flash address to use when deploying an aplication.</param>
        /// <param name="probeId">ID of the J-Link probe to connect to.</param>
        /// <param name="fitCheck"><see langword="true"/> to perform validation of update package against connected target.</param>
        /// <param name="verbosity">Set verbosity level of progress and error messages.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public static async System.Threading.Tasks.Task<ExitCodes> UpdateFirmwareAsync(
            string targetName,
            string fwVersion,
            bool preview,
            string archiveDirectoryPath,
            bool updateFw,
            string applicationPath,
            string deploymentAddress,
            string probeId,
            bool fitCheck,
            VerbosityLevel verbosity)
        {
            bool isApplicationBinFile = false;
            JLinkDevice jlinkDevice;
            ExitCodes operationResult;

            // if a target name wasn't specified use the default (and only available) ESP32 target
            if (string.IsNullOrEmpty(targetName))
            {
                return ExitCodes.E1000;
            }

            JLinkFirmware firmware = new JLinkFirmware(
                targetName,
                fwVersion,
                preview)
            {
                Verbosity = verbosity
            };

            // need to download update package?
            if (updateFw)
            {
                operationResult = await firmware.DownloadAndExtractAsync(archiveDirectoryPath);
                if (operationResult != ExitCodes.OK)
                {
                    return operationResult;
                }
                // download successful
            }

            // setup files to flash
            List<string> filesToFlash = [];

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

            List<string> connectedSilabsJLinkDevices = JLinkDevice.ListDevices();

            if (!connectedSilabsJLinkDevices.Any())
            {
                // no device was found
                return ExitCodes.E9010;
            }

            // Jlink device
            jlinkDevice = new JLinkDevice(probeId);

            if (!jlinkDevice.DevicePresent)
            {
                // no JTAG device found

                // done here, this command has no further processing
                return ExitCodes.E5001;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine("");
                OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                OutputWriter.WriteLine($"Connected to J-Link device with ID {jlinkDevice.ProbeId}");
                OutputWriter.WriteLine("");
                OutputWriter.WriteLine($"{jlinkDevice}");
                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            if (verbosity == VerbosityLevel.Diagnostic)
            {
                OutputWriter.WriteLine($"Firmware: {jlinkDevice.Firmare}");
                OutputWriter.WriteLine($"Hardware: {jlinkDevice.Hardware}");
            }

            if (fitCheck)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                OutputWriter.WriteLine("");
                OutputWriter.WriteLine("Image fit check for Silabs devices is not supported at this time.");
                OutputWriter.WriteLine("");

                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            operationResult = ExitCodes.OK;

            // set verbosity
            jlinkDevice.Verbosity = verbosity;

            // write HEX files to flash
            if (filesToFlash.Exists(f => f.EndsWith(".hex")))
            {
                operationResult = jlinkDevice.FlashHexFiles(filesToFlash);
            }

            if (operationResult == ExitCodes.OK && isApplicationBinFile)
            {
                // now program the application file
                operationResult = jlinkDevice.FlashBinFiles([applicationPath], [deploymentAddress]);
            }

            return operationResult;
        }

        /// <summary>
        /// Mass erase device.
        /// </summary>
        /// <param name="probeId">The probe ID.</param>
        /// <param name="verbosity">The verbosity level.</param>
        /// <returns></returns>
        public static ExitCodes MassErase(
            string probeId,
            VerbosityLevel verbosity)
        {
            // J-Link device
            JLinkDevice jlinkDevice = new(probeId);

            if (!jlinkDevice.DevicePresent)
            {
                // no J-Link device found

                // done here, this command has no further processing
                return ExitCodes.E5001;
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Connected to J-Link device with ID {jlinkDevice.ProbeId}");
            }

            if (verbosity == VerbosityLevel.Diagnostic)
            {
                OutputWriter.WriteLine($"Firmware: {jlinkDevice.Firmare}");
                OutputWriter.WriteLine($"Hardware: {jlinkDevice.Hardware}");
            }

            // set verbosity
            jlinkDevice.Verbosity = verbosity;

            // perform erase operation
            return jlinkDevice.MassErase();
        }
    }
}
