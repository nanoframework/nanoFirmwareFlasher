// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.FirmwareFlasher.DeploymentHelpers;

namespace nanoFramework.Tools.FirmwareFlasher.FileDeployment
{
    /// <summary>
    /// File Deployment Manager class.
    /// </summary>
    public class FileDeploymentManager
    {
        private readonly FileDeploymentConfiguration _configuration;
        private readonly VerbosityLevel _verbosity;
        private readonly string _serialPort;

        /// <summary>
        /// Creates an instance of FileDeploymentManager.
        /// </summary>
        public FileDeploymentManager(string configFilePath, string originalPort, VerbosityLevel verbosity)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            _configuration = JsonSerializer.Deserialize<FileDeploymentConfiguration>(File.ReadAllText(configFilePath), options);
            _serialPort = string.IsNullOrEmpty(_configuration.SerialPort) ? originalPort : _configuration.SerialPort;
            _verbosity = verbosity;
        }

        /// <summary>
        /// Deploys async the files.
        /// </summary>
        /// <returns>An ExitCode error.</returns>
        public async Task<ExitCodes> DeployAsync()
        {
            var (device, exitCode, deviceIsInInitializeState) = await DeviceHelper.ConnectDevice(_serialPort, _verbosity);

            if (exitCode != ExitCodes.OK)
            {
                return exitCode;
            }

            // check if device is still in initialized state
            if (!deviceIsInInitializeState)
            {
                // Deploy each file
                foreach (DeploymentFile file in _configuration.Files)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(file.SourceFilePath))
                        {
                            // deleting
                            OutputWriter.Write($"Deleting file {file.DestinationFilePath}...");
                            if (device.DebugEngine.DeleteStorageFile(file.DestinationFilePath) != Debugger.WireProtocol.StorageOperationErrorCode.NoError)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                                OutputWriter.WriteLine();
                                OutputWriter.WriteLine($"Error deleting file {file.DestinationFilePath}, it may not exist on the storage.");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                            }
                            else
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Green;
                                OutputWriter.WriteLine($"OK");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                            }
                        }
                        else
                        {
                            OutputWriter.Write($"Deploying file {file.SourceFilePath} to {file.DestinationFilePath}...");
                            Debugger.WireProtocol.StorageOperationErrorCode ret = device.DebugEngine.AddStorageFile(file.DestinationFilePath, File.ReadAllBytes(file.SourceFilePath));
                            if (ret != Debugger.WireProtocol.StorageOperationErrorCode.NoError)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Red;
                                OutputWriter.WriteLine();
                                OutputWriter.WriteLine($"Error deploying content file {file.SourceFilePath} to {file.DestinationFilePath}");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                            }
                            else
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Green;
                                OutputWriter.WriteLine($"OK");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                            }
                        }
                    }
                    catch
                    {
                        if (_verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.Red;
                            OutputWriter.WriteLine();
                            OutputWriter.WriteLine($"Exception deploying content file {file.SourceFilePath} to {file.DestinationFilePath}");
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                        }
                    }
                }
            }
            else
            {
                return ExitCodes.E2002;
            }

            return ExitCodes.OK;
        }
    }
}
