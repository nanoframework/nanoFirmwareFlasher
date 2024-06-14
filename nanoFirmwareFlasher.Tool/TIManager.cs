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
    /// Class to manage different operations specific to the TI SimpleLink platform.
    /// </summary>
    public class TIManager : IManager
    {
        private readonly Options _options;
        private readonly VerbosityLevel _verbosityLevel;

        public TIManager(Options options, VerbosityLevel verbosityLevel)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Platform != SupportedPlatform.ti_simplelink)
            {
                throw new NotSupportedException($"{nameof(options)} - {options.Platform}");
            }

            _options = options;
            _verbosityLevel = verbosityLevel;
        }

        /// <inheritdoc />
        public async Task<ExitCodes> ProcessAsync()
        {
            if (_options.TIInstallXdsDrivers)
            {
                return CC13x26x2Operations.InstallXds110Drivers(_verbosityLevel);
            }

            bool updateAndDeploy = false;

            if (!string.IsNullOrEmpty(_options.TargetName))
            {
                // update operation requested?
                if (_options.Update)
                {
                    // this to update the device with fw from Cloudsmith

                    // need to take care of flash address
                    string appFlashAddress = null;

                    if (_options.FlashAddress.Any())
                    {
                        // take the first address, it should be the only one valid
                        appFlashAddress = _options.FlashAddress.ElementAt(0);
                    }

                    var exitCode = await CC13x26x2Operations.UpdateFirmwareAsync(
                        _options.TargetName,
                        _options.FwVersion,
                        _options.Preview,
                        true,
                        _options.DeploymentImage,
                        appFlashAddress,
                        _verbosityLevel);

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
                    // this to flash a deployment image without updating the firmware

                    // need to take care of flash address
                    string appFlashAddress = null;

                    if (_options.FlashAddress.Any())
                    {
                        // take the first address, it should be the only one valid
                        appFlashAddress = _options.FlashAddress.ElementAt(0);
                    }
                    else
                    {
                        return ExitCodes.E9009;
                    }

                    var exitCode = await CC13x26x2Operations.UpdateFirmwareAsync(
                                    _options.TargetName,
                                    null,
                                    false,
                                    false,
                                    _options.DeploymentImage,
                                    appFlashAddress,
                                    _verbosityLevel);

                    if (exitCode != ExitCodes.OK)
                    {
                        // done here
                        return exitCode;
                    }

                    updateAndDeploy = true;
                }

                // reset MCU requested?
                if (_options.ResetMcu)
                {
                    // can't reset CC13x2 device without configuration file
                    // would require to specify the exact target name and then had to try parsing that 
                    return ExitCodes.E9000;
                }
            }

            if (!updateAndDeploy)
            {
                throw new NoOperationPerformedException();
            }

            return ExitCodes.OK;
        }
    }
}
