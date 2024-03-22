using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher.FileDeployment
{
    public class FileDeploymentManager
    {
        private FileDeploymentConfiguration _configuration;
        private VerbosityLevel _verbosity;
        private string _serialPort;

        public FileDeploymentManager(string configFilePath, string originalPort, VerbosityLevel verbosity)
        {
            _configuration = JsonConvert.DeserializeObject<FileDeploymentConfiguration>(File.ReadAllText(configFilePath));
            _serialPort = string.IsNullOrEmpty(_configuration.SerialPort) ? originalPort : _configuration.SerialPort;
            _verbosity = verbosity;
        }

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
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error connecting to nanoDevice on {_serialPort} to deploy files");
                Console.ForegroundColor = ConsoleColor.White;
                return ExitCodes.E2000;
            }

            // check if debugger engine exists
            if (device.DebugEngine == null)
            {
                device.CreateDebugEngine();
                if (_verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine($"Debug engine created.");
                }
            }

            bool deviceIsInInitializeState = false;

        retryDebug:
            bool connectResult = device.DebugEngine.Connect(5000, true, true);
            if (retryCount == 0 && _verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine($"Device connected and ready for file deployment.");
            }
            else if (_verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine($"Device connect result is {connectResult}. Attempt {retryCount}/{_numberOfRetries}");
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
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error connecting to debug engine on nanoDevice on {_serialPort} to deploy files");
                    Console.ForegroundColor = ConsoleColor.White;
                    return ExitCodes.E2000;
                }
            }

            retryCount = 0;

            // initial check 
            if (device.DebugEngine.IsDeviceInInitializeState())
            {
                if (_verbosity >= VerbosityLevel.Normal)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Device status verified as being in initialized state. Requesting to resume execution. Attempt {retryCount}/{_numberOfRetries}.");
                    Console.ForegroundColor = ConsoleColor.White;
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
                        Console.WriteLine($"Device has completed initialization.");
                    }

                    // done here
                    deviceIsInInitializeState = false;
                    break;
                }

                if (_verbosity >= VerbosityLevel.Diagnostic)
                {
                    Console.WriteLine($"Waiting for device to report initialization completed ({retryCount}/{_numberOfRetries}).");
                }

                // provide feedback to user on the 1st pass
                if (retryCount == 0)
                {
                    if (_verbosity >= VerbosityLevel.Diagnostic)
                    {
                        Console.WriteLine($"Waiting for device to initialize.");
                    }
                }

                if (device.DebugEngine.IsConnectedTonanoBooter)
                {
                    if (_verbosity >= VerbosityLevel.Diagnostic)
                    {
                        Console.WriteLine($"Device reported running nanoBooter. Requesting to load nanoCLR.");
                    }

                    // request nanoBooter to load CLR
                    device.DebugEngine.ExecuteMemory(0);
                }
                else if (device.DebugEngine.IsConnectedTonanoCLR)
                {
                    if (_verbosity >= VerbosityLevel.Normal)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Device reported running nanoCLR. Requesting to reboot nanoCLR.");
                        Console.ForegroundColor = ConsoleColor.White;
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
                foreach (var file in _configuration.Files)
                {
                    try
                    {
                        Console.Write($"Deploying file {file.ContentFileName} to {file.FileName}...");
                        var ret = device.DebugEngine.AddStorageFile(file.FileName, File.ReadAllBytes(file.ContentFileName));
                        if (ret != Debugger.WireProtocol.StorageOperationErrorCode.NoError)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine();
                            Console.WriteLine($"Error deploying content file {file.ContentFileName} to {file.FileName}");
                            Console.ForegroundColor = ConsoleColor.White;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"OK");
                            Console.ForegroundColor = ConsoleColor.White;
                        }
                    }
                    catch
                    {
                        if (_verbosity >= VerbosityLevel.Normal)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine();
                            Console.WriteLine($"Exception deploying content file {file.ContentFileName} to {file.FileName}");
                            Console.ForegroundColor = ConsoleColor.White;
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
