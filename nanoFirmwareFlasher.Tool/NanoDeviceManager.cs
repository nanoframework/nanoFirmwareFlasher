// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.NFDevice;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class to manage different operations specific to the nano devices.
    /// </summary>
    public class NanoDeviceManager : IManager
    {
        private readonly Options _options;
        private readonly VerbosityLevel _verbosityLevel;
        private const int AccessSerialPortTimeout = 3000;

        public NanoDeviceManager(Options options, VerbosityLevel verbosityLevel)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options;
            _verbosityLevel = verbosityLevel;
        }

        /// <inheritdoc />
        public async Task<ExitCodes> ProcessAsync()
        {
            bool failedToDoSomething = true;
            ExitCodes exitCode = ExitCodes.OK;

            // COM port is mandatory for nano device operations
            if (string.IsNullOrEmpty(_options.SerialPort))
            {
                return ExitCodes.E6001;
            }

            NanoDeviceOperations _nanoDeviceOperations = new NanoDeviceOperations();

            using (var access = GlobalExclusiveDeviceAccess.TryGet(_options.SerialPort, AccessSerialPortTimeout))
            {
                if (access is null)
                {
                    return ExitCodes.E6002;
                }

                if (_options.DeviceDetails)
                {
                    NanoDeviceBase nanoDevice = null;
                    return _nanoDeviceOperations.GetDeviceDetails(
                        _options.SerialPort,
                        ref nanoDevice);
                }
                else if (_options.Update)
                {
                    exitCode = await _nanoDeviceOperations.UpdateDeviceClrAsync(
                        _options.SerialPort,
                        _options.FwVersion,
                        _options.IdentifyFirmware,
                        _options.FromFwArchive ? _options.FwArchivePath : null,
                        _options.ClrFile,
                        _verbosityLevel);

                    if (exitCode != ExitCodes.OK)
                    {
                        return exitCode;
                    }

                    // flag operation as done
                    failedToDoSomething = false;
                }

                if (_options.Deploy)
                {
                    exitCode = _nanoDeviceOperations.DeployApplication(
                        _options.SerialPort,
                        _options.DeploymentImage,
                        _verbosityLevel);

                    if (exitCode != ExitCodes.OK)
                    {
                        return exitCode;
                    }

                    // flag operation as done
                    failedToDoSomething = false;
                }
            }

            if (failedToDoSomething)
            {
                throw new NoOperationPerformedException();
            }

            return exitCode;
        }
    }
}
