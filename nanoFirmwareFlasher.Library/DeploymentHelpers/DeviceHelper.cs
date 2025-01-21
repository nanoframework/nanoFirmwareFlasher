// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;

namespace nanoFramework.Tools.FirmwareFlasher.DeploymentHelpers
{
    /// <summary>
    /// Helper class to manage device operations.
    /// </summary>
    internal static class DeviceHelper
    {
        /// <summary>
        /// Connects to a nanoDevice.
        /// </summary>
        /// <param name="serialPort">The serial port.</param>
        /// <param name="verbosity">Verbositiy level.</param>
        /// <returns>A tupple with the <see cref="NanoDeviceBase"/> and the <see cref="ExitCodes"/> and the initialization state.</returns>
        public static async Task<(NanoDeviceBase device, ExitCodes exitCode, bool deviceIsInInitializeState)> ConnectDevice(string serialPort, VerbosityLevel verbosity)
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
                serialDebugClient.AddDevice(serialPort);

                device = serialDebugClient.NanoFrameworkDevices[0];
            }
            catch (Exception)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Red;
                OutputWriter.WriteLine($"Error connecting to nanoDevice on {serialPort} for deployment.");
                OutputWriter.ForegroundColor = ConsoleColor.White;
                return (device, ExitCodes.E2000, true);
            }

            // check if debugger engine exists
            if (device.DebugEngine == null)
            {
                device.CreateDebugEngine();
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Debug engine created.");
                }
            }

            bool deviceIsInInitializeState = false;

        retryDebug:
            bool connectResult = device.DebugEngine.Connect(5000, true, true);
            if (retryCount == 0 && verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Device connected and ready for deployment.");
            }
            else if (verbosity >= VerbosityLevel.Normal)
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
                    OutputWriter.WriteLine($"Error connecting to debug engine on nanoDevice on {serialPort}.");
                    OutputWriter.ForegroundColor = ConsoleColor.White;
                    return (device ,ExitCodes.E2000, true);
                }
            }

            retryCount = 0;

            // initial check 
            if (device.DebugEngine.IsDeviceInInitializeState())
            {
                if (verbosity >= VerbosityLevel.Normal)
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
                    if (verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"Device has completed initialization.");
                    }

                    // done here
                    deviceIsInInitializeState = false;
                    break;
                }

                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine($"Waiting for device to report initialization completed ({retryCount}/{_numberOfRetries}).");
                }

                // provide feedback to user on the 1st pass
                if (retryCount == 0)
                {
                    if (verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"Waiting for device to initialize.");
                    }
                }

                if (device.DebugEngine.IsConnectedTonanoBooter)
                {
                    if (verbosity >= VerbosityLevel.Diagnostic)
                    {
                        OutputWriter.WriteLine($"Device reported running nanoBooter. Requesting to load nanoCLR.");
                    }

                    // request nanoBooter to load CLR
                    device.DebugEngine.ExecuteMemory(0);
                }
                else if (device.DebugEngine.IsConnectedTonanoCLR)
                {
                    if (verbosity >= VerbosityLevel.Normal)
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

            return (device, ExitCodes.OK, deviceIsInInitializeState);
        }
    }
}
