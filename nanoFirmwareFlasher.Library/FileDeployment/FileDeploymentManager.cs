// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using Newtonsoft.Json;

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
            _configuration = JsonConvert.DeserializeObject<FileDeploymentConfiguration>(File.ReadAllText(configFilePath));
            _serialPort = string.IsNullOrEmpty(_configuration.SerialPort) ? originalPort : _configuration.SerialPort;
            _verbosity = verbosity;
        }

        /// <summary>
        /// Deploys async the files.
        /// </summary>
        /// <returns>An ExitCode error.</returns>
        public async Task<ExitCodes> DeployAsync()
        {
            // number of retries when performing a deploy operation
            const int _numberOfRetries = 5;
            // timeout when performing a deploy operation
            const int _timeoutMiliseconds = 1000;

            NanoDeviceBase device = null;
            PortBase serialDebugClient;
            int retryCount = 0;

            serialDebugClient = PortBase.CreateInstanceForSerial(false);

            try
            {
                serialDebugClient.AddDevice(_serialPort);

                device = serialDebugClient.NanoFrameworkDevices[0];
            }
            catch (Exception)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Error connecting to nanoDevice on {_serialPort} to deploy files");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E2000;
            }

            // check if debugger engine exists
            if (device.DebugEngine == null)
            {
                device.CreateDebugEngine();
                if (_verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Debug engine created.");
                }
            }

            bool deviceIsInInitializeState = false;

        retryDebug:
            bool connectResult = device.DebugEngine.Connect(5000, true, true);
            if (retryCount == 0 && _verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Device connected and ready for file deployment.");
            }
            else if (_verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Device connect result is {connectResult}. Attempt {retryCount}/{_numberOfRetries}");
            }

            if (!connectResult)
            {
                if (retryCount < _numberOfRetries)
                {
                    // Give it a bit of time
                    await Task.Delay(100);
                    retryCount++;

                    goto retryDebug;
                }
                else
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                    OutputWriter.WriteLine($"Error connecting to debug engine on nanoDevice on {_serialPort} to deploy files");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E2000;
                }
            }

            retryCount = 0;

            // initial check 
            if (device.DebugEngine.IsDeviceInInitializeState())
            {
                if (_verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                    OutputWriter.WriteLine($"Device status verified as being in initialized state. Requesting to resume execution. Attempt {retryCount}/{_numberOfRetries}.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                }

                // set flag
                deviceIsInInitializeState = true;

                // device is still in initialization state, try resume execution
                device.DebugEngine.ResumeExecution();
            }

            // handle the workflow required to try resuming the execution on the device
            // only required if device is not already there
            // retry 5 times with a 500ms interval between retries
            while (retryCount++ < _numberOfRetries && deviceIsInInitializeState)
            {
                if (!device.DebugEngine.IsDeviceInInitializeState())
                {
                    if (_verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"Device has completed initialization.");
                    }

                    // done here
                    deviceIsInInitializeState = false;
                    break;
                }

                if (_verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine($"Waiting for device to report initialization completed ({retryCount}/{_numberOfRetries}).");
                }

                // provide feedback to user on the 1st pass
                if (retryCount == 0)
                {
                    if (_verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"Waiting for device to initialize.");
                    }
                }

                if (device.DebugEngine.IsConnectedTonanoBooter)
                {
                    if (_verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"Device reported running nanoBooter. Requesting to load nanoCLR.");
                    }

                    // request nanoBooter to load CLR
                    device.DebugEngine.ExecuteMemory(0);
                }
                else if (device.DebugEngine.IsConnectedTonanoCLR)
                {
                    if (_verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Yellow;
                        OutputWriter.WriteLine($"Device reported running nanoCLR. Requesting to reboot nanoCLR.");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }

                    await Task.Run(delegate
                    {
                        // already running nanoCLR try rebooting the CLR
                        device.DebugEngine.RebootDevice(RebootOptions.ClrOnly);
                    });
                }

                // wait before next pass
                // use a back-off strategy of increasing the wait time to accommodate slower or less responsive targets (such as networked ones)
                await Task.Delay(TimeSpan.FromMilliseconds(_timeoutMiliseconds * (retryCount + 1)));

                await Task.Yield();
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
