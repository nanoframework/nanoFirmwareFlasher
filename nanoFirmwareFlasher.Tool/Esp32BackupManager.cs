// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger.NFDevice;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Standalone flash backup/restore, independent of firmware-package dispatch.
    /// </summary>
    public class Esp32BackupManager : IManager
    {
        private const int AccessSerialPortTimeout = 3000;
        private readonly Esp32BackupOptionsBase _options;
        private readonly VerbosityLevel _verbosityLevel;

        public Esp32BackupManager(Esp32BackupOptionsBase options, VerbosityLevel verbosityLevel)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (options is not BackupOptions && options is not RestoreOptions)
            {
                throw new ArgumentException("Expected backup or restore options.", nameof(options));
            }

            string error = Esp32BackupOptionsBase.Validate(options);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(options));
            }

            _verbosityLevel = verbosityLevel;
        }

        public Task<ExitCodes> ProcessAsync()
        {
            using (var access = GlobalExclusiveDeviceAccess.TryGet(_options.SerialPort, AccessSerialPortTimeout))
            {
                if (access is null)
                {
                    return Task.FromResult(ExitCodes.E6002);
                }

                EspTool tool;
                try
                {
                    tool = new EspTool(_options.SerialPort, _options.BaudRate, null, 0, null, _verbosityLevel);
                }
                catch (Exception)
                {
                    return Task.FromResult(ExitCodes.E4005);
                }

                using (tool)
                {
                    if (!tool.ComPortAvailable)
                    {
                        return Task.FromResult(ExitCodes.E6000);
                    }

                    Esp32DeviceInfo device = tool.GetDeviceDetails(null, hardResetAfterCommand: false);
                    if (device == null)
                    {
                        return Task.FromResult(ExitCodes.E4000);
                    }

                    ExitCodes result = _options is RestoreOptions
                        ? Esp32Backup.Restore(tool, device, _options.Path, _options.GetPartitions(), _options.WholeFlash, _verbosityLevel)
                        : Esp32Backup.Backup(tool, device, _options.Path, _options.GetPartitions(), _options.WholeFlash, _verbosityLevel);

                    return Task.FromResult(result);
                }
            }
        }
    }
}
