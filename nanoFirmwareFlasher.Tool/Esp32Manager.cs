//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Linq;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class to manage different operations specific to the ESP32 platform.
    /// </summary>
    public class Esp32Manager : IManager
    {
        private readonly Options _options;
        private readonly VerbosityLevel _verbosityLevel;

        public Esp32Manager(Options options, VerbosityLevel verbosityLevel)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Platform != SupportedPlatform.esp32)
            {
                throw new NotSupportedException($"{nameof(options)} - {options.Platform}");
            }

            _options = options;
            _verbosityLevel = verbosityLevel;
        }

        /// <inheritdoc />
        public async Task<ExitCodes> ProcessAsync()
        {
            // COM port is mandatory for ESP32
            if (string.IsNullOrEmpty(_options.SerialPort))
            {
                return ExitCodes.E6001;
            }

            EspTool espTool;

            try
            {
                espTool = new EspTool(
                    _options.SerialPort,
                    _options.BaudRate,
                    _options.Esp32FlashMode,
                    _options.Esp32FlashFrequency,
                    _options.Esp32PartitionTableSize,
                    _verbosityLevel);
            }
            catch (Exception)
            {
                return ExitCodes.E4005;
            }

            Esp32DeviceInfo esp32Device;

            if (espTool.ComPortAvailable)
            {
                esp32Device = espTool.GetDeviceDetails(_options.TargetName, _options.Esp32PartitionTableSize == null);
            }
            else
            {
                // couldn't open COM port
                // done here, this command has no further processing
                return ExitCodes.E6000;
            }

            if (_verbosityLevel >= VerbosityLevel.Normal)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;

                Console.WriteLine("");
                Console.WriteLine($"Connected to:");
                Console.WriteLine($"{esp32Device}");

                Console.ForegroundColor = ConsoleColor.White;

                // if this is a PICO and baud rate is not 115200 or 1M5, operations will most likely fail
                // warn user about this
                if (
                    esp32Device.ChipName.Contains("ESP32-PICO")
                    && (_options.BaudRate != 115200
                        && _options.BaudRate != 1500000))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;

                    Console.WriteLine("");
                    Console.WriteLine("****************************** WARNING ******************************");
                    Console.WriteLine("The connected device it's an ESP32 PICO which can be picky about the ");
                    Console.WriteLine("baud rate used. Recommendation is to use --baud 115200 ");
                    Console.WriteLine("*********************************************************************");
                    Console.WriteLine("");

                    Console.ForegroundColor = ConsoleColor.White;
                }
            }

            // set verbosity
            espTool.Verbosity = _verbosityLevel;

            // backup requested
            // Should backup be an unique operation => exit whatever success or not ?
            // In this case, should find how manage a NoOperationPerformedException info
            if (!string.IsNullOrEmpty(_options.BackupPath) ||
               !string.IsNullOrEmpty(_options.BackupFile))
            {
                // backup path specified, backup deployment
                var exitCode = Esp32Operations.BackupFlash(espTool, esp32Device, _options.BackupPath, _options.BackupFile, _verbosityLevel);

                if (exitCode != ExitCodes.OK)
                {
                    // done here
                    return exitCode;
                }
            }

            // show device details
            if (_options.DeviceDetails)
            {
                // device details already output
                return ExitCodes.OK;
            }

            bool updateAndDeploy = false;

            // update operation requested?
            if (_options.Update)
            {
                // write flash
                var exitCode = await Esp32Operations.UpdateFirmwareAsync(
                    espTool,
                    esp32Device,
                    _options.TargetName,
                    true,
                    _options.FwVersion,
                    _options.Preview,
                    _options.DeploymentImage,
                    null,
                    _options.ClrFile,
                    !_options.FitCheck,
                    _options.MassErase,
                    _verbosityLevel,
                    _options.Esp32PartitionTableSize);

                if (exitCode != ExitCodes.OK)
                {
                    // done here
                    return exitCode;
                }

                updateAndDeploy = true;
            }

            // it's OK to deploy after a successful update
            if (_options.Deploy)
            {
                // need to take care of flash address
                string appFlashAddress = string.Empty;

                if (_options.FlashAddress.Any())
                {
                    // take the first address, it should be the only one valid
                    appFlashAddress = _options.FlashAddress.ElementAt(0);
                }

                // this to flash a deployment image without updating the firmware
                // write flash
                var exitCode = await Esp32Operations.UpdateFirmwareAsync(
                    espTool,
                    esp32Device,
                    _options.TargetName,
                    false,
                    null,
                    false,
                    _options.DeploymentImage,
                    appFlashAddress,
                    null,
                    !_options.FitCheck,
                    _options.MassErase,
                    _verbosityLevel,
                    _options.Esp32PartitionTableSize);

                if (exitCode != ExitCodes.OK)
                {
                    // done here
                    return exitCode;
                }

                updateAndDeploy = true;
            }

            if (!updateAndDeploy)
            {
                throw new NoOperationPerformedException();
            }

            return ExitCodes.OK;
        }
    }
}
