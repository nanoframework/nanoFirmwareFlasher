// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class with operations available for .NET nanoFramework devices.
    /// </summary>
    public class NanoDeviceOperations : IDisposable
    {
        private bool _disposedValue;
        private NanoDeviceBase _nanoDevice;
        private readonly PortBase _serialDebuggerPort;

        /// <summary>
        /// Class with operations to perform with .NET nanoFramework devices.
        /// </summary>
        public NanoDeviceOperations()
        {
            // create serial port instance
            // WITHOUT starting device watchers
            _serialDebuggerPort = PortBase.CreateInstanceForSerial(false);
        }

        /// <summary>
        /// List connected .NET nanoFramework devices.
        /// </summary>
        /// <param name="getDeviceDetails">Set to <see langword="true"/> to get details from devices.</param>
        /// <returns>An observable collection of <see cref="NanoDeviceBase"/>. devices</returns>
        public ObservableCollection<NanoDeviceBase> ListDevices(bool getDeviceDetails)
        {
            // start device watchers
            _serialDebuggerPort.StartDeviceWatchers();

            while (!_serialDebuggerPort.IsDevicesEnumerationComplete)
            {
                Thread.Sleep(100);
            }

            if (getDeviceDetails)
            {
                // get device details
                foreach (NanoDeviceBase device in _serialDebuggerPort.NanoFrameworkDevices)
                {
                    _ = device.DebugEngine.Connect(
                        false,
                        false);

                    // check that we are in CLR
                    if (device.DebugEngine.IsConnectedTonanoCLR)
                    {
                        try
                        {
                            // get device info
                            _ = device.GetDeviceInfo(true);
                        }
                        catch
                        {
                            // no need to report this, just move on
                        }
                    }
                    else
                    {
                        // we are in booter, can only get TargetInfo
                        try
                        {
                            // get device info
                            _ = device.DebugEngine?.TargetInfo;
                        }
                        catch
                        {
                            // no need to report this, just move on
                        }
                    }

                    device.Disconnect(true);
                }
            }

            return _serialDebuggerPort.NanoFrameworkDevices;
        }

        /// <summary>
        /// Gets device details of the requested .NET nanoFramework device.
        /// </summary>
        /// <param name="serialPort">Serial port name where the device is connected to.</param>
        /// <param name="nanoDevice"><see cref="NanoDeviceBase"/> object for the requested device.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        /// <exception cref="CantConnectToNanoDeviceException">
        /// <para>
        /// Couldn't connect to specified nano device.
        /// </para>
        /// <para>
        /// --OR-- 
        /// </para>
        /// <para>
        /// Couldn't retrieve device details from the nano device.
        /// </para>
        /// </exception>
        public ExitCodes GetDeviceDetails(
            string serialPort,
            ref NanoDeviceBase nanoDevice)
        {
            if (ReadDetailsFromDevice(
                serialPort,
                ref nanoDevice))
            {
                // check that we are in CLR
                if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                {
                    // we have to have a valid device info
                    if (nanoDevice.DeviceInfo.Valid)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine($"{nanoDevice.DeviceInfo}");

                        OutputWriter.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.OK;
                    }
                    else
                    {
                        // report issue
                        throw new CantConnectToNanoDeviceException("Couldn't retrieve device details from nano device.");
                    }
                }
                else
                {
                    // we are in booter, can only get TargetInfo
                    // we have to have a valid device info
                    if (nanoDevice.DebugEngine.TargetInfo != null)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                        OutputWriter.WriteLine("");
                        OutputWriter.WriteLine($"{nanoDevice.DebugEngine.TargetInfo}");

                        OutputWriter.ForegroundColor = ConsoleColor.White;

                        return ExitCodes.OK;
                    }
                    else
                    {
                        // report issue 
                        throw new CantConnectToNanoDeviceException("Couldn't retrieve device details from nano device.");
                    }
                }
            }
            else
            {
                // report issue 
                throw new CantConnectToNanoDeviceException("Couldn't connect to specified nano device.");
            }
        }

        /// <summary>
        /// Update CLR on specified nano device.
        /// </summary>
        /// <param name="serialPort">Serial port name where the device is connected to.</param>
        /// <param name="fwVersion">Firmware version to update to.</param>
        /// <param name="showFwOnly">Only show which firmware to use; do not deploy anything to the device.</param>
        /// <param name="archiveDirectoryPath">Path to the archive directory where all targets are located. Pass <c>null</c> if there is no archive.
        /// If not <c>null</c>, the package will always be retrieved from the archive and never be downloaded.</param>
        /// <param name="clrFile">Path to CLR file to use for firmware update.</param>
        /// <param name="verbosity">Set verbosity level of progress and error messages.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        /// <exception cref="CantConnectToNanoDeviceException">
        /// <para>
        /// Couldn't connect to specified nano device.
        /// </para>
        /// <para>
        /// --OR-- 
        /// </para>
        /// <para>
        /// Couldn't retrieve device details from the nano device.
        /// </para>
        /// </exception>
        public async Task<ExitCodes> UpdateDeviceClrAsync(
            string serialPort,
            string fwVersion,
            bool showFwOnly,
            string archiveDirectoryPath,
            string clrFile,
            VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            if (serialPort is null)
            {
                throw new ArgumentNullException(nameof(serialPort));
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write($"Getting details from nano device...");
            }

            bool updateCLRfile = !string.IsNullOrEmpty(clrFile);

            // if this is updating with a local CLR file, download the package silently
            if (updateCLRfile)
            {
                // check file
                if (!File.Exists(clrFile))
                {
                    return ExitCodes.E9011;
                }

                // has to be a binary file
                if (Path.GetExtension(clrFile) != ".bin")
                {
                    return ExitCodes.E9012;
                }

                // make sure path is absolute
                clrFile = Utilities.MakePathAbsolute(
                    Environment.CurrentDirectory,
                    clrFile);
            }

            NanoDeviceBase nanoDevice = null;

            _ = ReadDetailsFromDevice(
                serialPort,
                ref nanoDevice);

            // sanity checks
            if (nanoDevice == null
                || nanoDevice.TargetName == null
                || nanoDevice.Platform == null)
            {
                // can't update this device
                throw new NanoDeviceOperationFailedException($"Missing details from {nanoDevice?.TargetName} to perform update operation.");
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("OK");

                OutputWriter.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                OutputWriter.WriteLine("");
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine("");
                OutputWriter.ForegroundColor = ConsoleColor.Cyan;

                OutputWriter.WriteLine($"Connected to nano device: {nanoDevice.Description}");
                OutputWriter.WriteLine("");

                OutputWriter.ForegroundColor = ConsoleColor.White;
            }

            if (showFwOnly)
            {
                OutputWriter.WriteLine("");
                OutputWriter.WriteLine($"Connected nanoDevice uses target '{nanoDevice.TargetName}'; platform is {nanoDevice.Platform}.");
                OutputWriter.WriteLine("");
                return ExitCodes.OK;
            }

            // local file will be flashed straight away
            if (updateCLRfile)
            {
                if (nanoDevice.DebugEngine.Connect(
                    1000,
                    true,
                    true))
                {
                    Version currentClrVersion = null;

                    // try to store CLR version
                    if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                    {
                        if (nanoDevice.DeviceInfo.Valid)
                        {
                            currentClrVersion = nanoDevice.DeviceInfo.SolutionBuildVersion;
                        }
                    }

                    bool booterLaunched = false;

                    if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                    {
                        // any update has to be handled by nanoBooter, so let's have it running
                        try
                        {
                            if (verbosity > VerbosityLevel.Normal)
                            {
                                OutputWriter.WriteLine("");

                                OutputWriter.ForegroundColor = ConsoleColor.White;
                                OutputWriter.Write("Launching nanoBooter...");
                            }

                            booterLaunched = nanoDevice.ConnectToNanoBooter();

                            if (!booterLaunched)
                            {
                                // check for version where the software reboot to nanoBooter was made available
                                if (currentClrVersion != null &&
                                    nanoDevice.DeviceInfo.SolutionBuildVersion < new Version("1.6.0.54"))
                                {
                                    OutputWriter.WriteLine("");

                                    throw new NanoDeviceOperationFailedException("The device is running a version that doesn't support rebooting by software. Please update your device using 'nanoff' tool.");
                                }
                            }

                            if (verbosity > VerbosityLevel.Normal)
                            {
                                OutputWriter.ForegroundColor = ConsoleColor.Green;
                                OutputWriter.WriteLine("OK");
                                OutputWriter.ForegroundColor = ConsoleColor.White;
                            }
                        }
                        catch
                        {
                            // this reboot step can go wrong and there's no big deal with that
                        }
                    }
                    else
                    {
                        booterLaunched = true;
                    }

                    if (booterLaunched &&
                        nanoDevice.Ping() == Debugger.WireProtocol.ConnectionSource.nanoBooter)
                    {
                        // get address for CLR block expected by device
                        int clrAddress = nanoDevice.GetCLRStartAddress();

                        await Task.Yield();

                        if (verbosity >= VerbosityLevel.Normal)
                        {
                            OutputWriter.ForegroundColor = ConsoleColor.White;
                            OutputWriter.Write($"Starting CLR update with local file...");
                        }

                        try
                        {
                            await Task.Yield();

                            if (nanoDevice.DeployBinaryFile(
                                clrFile,
                                (uint)clrAddress,
                                null))
                            {
                                await Task.Yield();

                                if (verbosity >= VerbosityLevel.Normal)
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                                    OutputWriter.WriteLine("OK");
                                    OutputWriter.ForegroundColor = ConsoleColor.White;
                                }
                            }
                            else
                            {
                                if (verbosity >= VerbosityLevel.Normal)
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.Red;
                                    OutputWriter.WriteLine("FAILED!");

                                    return ExitCodes.E2002;
                                }
                            }

                            if (booterLaunched)
                            {
                                // try to reboot target 
                                if (verbosity >= VerbosityLevel.Normal)
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.White;
                                    OutputWriter.Write("Rebooting...");
                                }

                                nanoDevice.DebugEngine.RebootDevice(RebootOptions.NormalReboot);

                                if (verbosity >= VerbosityLevel.Normal)
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.Green;
                                    OutputWriter.WriteLine("OK");
                                    OutputWriter.ForegroundColor = ConsoleColor.White;
                                }

                                return ExitCodes.OK;
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new NanoDeviceOperationFailedException($"Exception occurred when performing update ({ex.Message}).");
                        }
                    }
                    else
                    {
                        if (!booterLaunched)
                        {
                            // only report this as an error if the launch was successful
                            throw new NanoDeviceOperationFailedException("Failed to launch nanoBooter. Quitting update.");
                        }
                    }
                }
                else
                {
                    throw new NanoDeviceOperationFailedException("Can't connect to device. Quitting update.");
                }

                // done here
                return ExitCodes.OK;
            }
            else
            {
                // get firmware package
                FirmwarePackage fwPackage = FirmwarePackageFactory.GetFirmwarePackage(
                    nanoDevice,
                    fwVersion);

                ExitCodes downloadResult = await fwPackage.DownloadAndExtractAsync(archiveDirectoryPath);

                if (downloadResult == ExitCodes.OK)
                {
                    if (nanoDevice.DebugEngine.Connect(
                        1000,
                        true,
                        true))
                    {
                        Version currentClrVersion = null;

                        // try to store CLR version
                        if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                        {
                            if (nanoDevice.DeviceInfo.Valid)
                            {
                                currentClrVersion = nanoDevice.DeviceInfo.SolutionBuildVersion;
                            }
                        }

                        // update conditions:
                        // 1. Running CLR _and_ the new version is higher
                        // 2. Running nanoBooter and there is no version information on the CLR (presumably because there is no CLR installed)
                        if (Version.Parse(fwPackage.Version) > nanoDevice.CLRVersion)
                        {
                            bool attemptToLaunchBooter = false;

                            if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                            {
                                // any update has to be handled by nanoBooter, so let's have it running
                                try
                                {
                                    if (verbosity > VerbosityLevel.Normal)
                                    {
                                        OutputWriter.WriteLine("");

                                        OutputWriter.ForegroundColor = ConsoleColor.Cyan;
                                        OutputWriter.Write("Launching nanoBooter...");
                                        OutputWriter.WriteLine("");

                                        OutputWriter.ForegroundColor = ConsoleColor.White;
                                    }

                                    attemptToLaunchBooter = nanoDevice.ConnectToNanoBooter();

                                    if (!attemptToLaunchBooter)
                                    {
                                        // check for version where the software reboot to nanoBooter was made available
                                        if (currentClrVersion != null &&
                                            nanoDevice.DeviceInfo.SolutionBuildVersion < new Version("1.6.0.54"))
                                        {
                                            OutputWriter.WriteLine("");

                                            throw new NanoDeviceOperationFailedException("The device is running a version that doesn't support rebooting by software. Please update your device using 'nanoff' tool.");
                                        }
                                    }

                                    if (verbosity > VerbosityLevel.Normal)
                                    {
                                        OutputWriter.ForegroundColor = ConsoleColor.Green;
                                        OutputWriter.WriteLine("OK");
                                        OutputWriter.ForegroundColor = ConsoleColor.White;
                                    }
                                }
                                catch
                                {
                                    // this reboot step can go wrong and there's no big deal with that
                                }
                            }
                            else
                            {
                                attemptToLaunchBooter = true;
                            }

                            if (attemptToLaunchBooter &&
                                nanoDevice.Ping() == Debugger.WireProtocol.ConnectionSource.nanoBooter)
                            {
                                // get address for CLR block expected by device
                                int clrAddress = nanoDevice.GetCLRStartAddress();

                                // compare with address on the fw packages
                                if (clrAddress !=
                                    fwPackage.ClrStartAddress)
                                {
                                    // CLR addresses don't match, can't proceed with update
                                    throw new NanoDeviceOperationFailedException("Can't update device. CLR addresses are different. Please update nanoBooter manually.");
                                }

                                await Task.Yield();

                                if (verbosity >= VerbosityLevel.Normal)
                                {
                                    OutputWriter.ForegroundColor = ConsoleColor.White;
                                    OutputWriter.Write($"Starting update to CLR v{fwPackage.Version}...");
                                }

                                try
                                {
                                    await Task.Yield();

                                    if (nanoDevice.DeployBinaryFile(
                                        fwPackage.NanoClrFileBinary,
                                        fwPackage.ClrStartAddress,
                                        null))
                                    {
                                        await Task.Yield();

                                        if (verbosity >= VerbosityLevel.Normal)
                                        {
                                            OutputWriter.ForegroundColor = ConsoleColor.Green;
                                            OutputWriter.WriteLine("OK");
                                            OutputWriter.ForegroundColor = ConsoleColor.White;
                                        }
                                    }

                                    if (attemptToLaunchBooter)
                                    {
                                        // try to reboot target 
                                        if (verbosity > VerbosityLevel.Normal)
                                        {
                                            OutputWriter.ForegroundColor = ConsoleColor.White;
                                            OutputWriter.Write("Rebooting...");
                                        }

                                        nanoDevice.DebugEngine.RebootDevice(RebootOptions.NormalReboot);

                                        if (verbosity > VerbosityLevel.Normal)
                                        {
                                            OutputWriter.ForegroundColor = ConsoleColor.Green;
                                            OutputWriter.WriteLine("OK");
                                            OutputWriter.ForegroundColor = ConsoleColor.White;
                                        }

                                        return ExitCodes.OK;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    throw new NanoDeviceOperationFailedException($"Exception occurred when performing update ({ex.Message}).");
                                }
                            }
                            else
                            {
                                if (attemptToLaunchBooter)
                                {
                                    // only report this as an error if the launch was successful
                                    throw new NanoDeviceOperationFailedException("Failed to launch nanoBooter. Quitting update.");
                                }
                            }
                        }
                        else
                        {
                            if (nanoDevice.DebugEngine.IsConnectedTonanoCLR
                                && (fwPackage.Version == nanoDevice.DeviceInfo.ClrBuildVersion.ToString()))
                            {
                                if (verbosity >= VerbosityLevel.Normal)
                                {
                                    OutputWriter.WriteLine("");
                                    OutputWriter.ForegroundColor = ConsoleColor.Yellow;

                                    OutputWriter.WriteLine("Nothing to update as device is already running the requested version.");
                                    OutputWriter.WriteLine("");

                                    OutputWriter.ForegroundColor = ConsoleColor.White;
                                }

                                // done here
                                return ExitCodes.OK;
                            }
                        }
                    }
                    else
                    {
                        throw new NanoDeviceOperationFailedException("Can't connect to device. Quitting update.");
                    }
                }

                return downloadResult;
            }
        }

        /// <summary>
        /// Deploy application on specified nano device.
        /// </summary>
        /// <param name="serialPort">Serial port name where the device is connected to.</param>
        /// <param name="deploymentPackage">Path to the binary file to deploy in the connected nano device.</param>
        /// <param name="verbosity">Set verbosity level of progress and error messages.</param>
        /// <returns>The <see cref="ExitCodes"/> with the operation result.</returns>
        public ExitCodes DeployApplication(
            string serialPort,
            string deploymentPackage,
            VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            if (serialPort is null)
            {
                return ExitCodes.E2001;
            }

            if (deploymentPackage is null)
            {
                throw new ArgumentNullException("No deployment package provided.");
            }

            if (!File.Exists(deploymentPackage))
            {
                throw new FileNotFoundException("Couldn't find the deployment file at the specified path.");
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.White;
                OutputWriter.Write($"Getting details from nano device...");
            }

            NanoDeviceBase nanoDevice = null;

            _ = ReadDetailsFromDevice(
                serialPort,
                ref nanoDevice);

            // sanity checks
            if (nanoDevice == null
                || nanoDevice.TargetName == null
                || nanoDevice.Platform == null)
            {
                // can't update this device
                throw new NanoDeviceOperationFailedException($"Missing details from {nanoDevice?.TargetName} to perform deployment operation.");
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.ForegroundColor = ConsoleColor.Green;
                OutputWriter.WriteLine("OK");

                OutputWriter.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                OutputWriter.WriteLine("");
            }

            if (nanoDevice.DebugEngine.Connect(
                1000,
                true,
                true))
            {
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.Write($"Deploying managed application...");
                }

                if (!nanoDevice.DeployBinaryFile(
                    deploymentPackage,
                    (uint)nanoDevice.GetDeploymentStartAddress(),
                    null))
                {

                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Red;
                        OutputWriter.WriteLine("FAILED!");

                        return ExitCodes.E2002;
                    }
                }
                else
                {
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Green;
                        OutputWriter.WriteLine("OK");

                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                    else
                    {
                        OutputWriter.WriteLine("");
                    }

                    // try to reboot target 
                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                        OutputWriter.Write("Rebooting...");
                    }

                    // reboot device
                    nanoDevice.DebugEngine.RebootDevice(RebootOptions.NormalReboot);

                    if (verbosity >= VerbosityLevel.Normal)
                    {
                        OutputWriter.ForegroundColor = ConsoleColor.Green;
                        OutputWriter.WriteLine("OK");
                        OutputWriter.ForegroundColor = ConsoleColor.White;
                    }
                }

                // all good here
                return ExitCodes.OK;
            }
            else
            {
                throw new NanoDeviceOperationFailedException("Can't connect to device. Quitting update.");
            }
        }

        /// <inheritdoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // clean-up debugger port instances and devices
                    _serialDebuggerPort?.StopDeviceWatchers();
                }

                _nanoDevice?.Disconnect(true);
                _nanoDevice = null;

                _disposedValue = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private bool ReadDetailsFromDevice(
            string serialPort,
            ref NanoDeviceBase nanoDevice)
        {
            if (serialPort is null)
            {
                throw new ArgumentNullException(nameof(serialPort));
            }

            // connect to specified serial port
            try
            {
                _serialDebuggerPort.AddDevice(serialPort);
            }
#if DEBUG
            catch (Exception ex)
            {
                OutputWriter.WriteLine($"Failed to add device: {ex.Message}");

                return false;
            }
#else
            catch
            {
                return false;

            }
#endif

            // get nano device (there should be only one)
            nanoDevice = _serialDebuggerPort.NanoFrameworkDevices.FirstOrDefault();

            if (nanoDevice != null)
            {
                // check if debugger engine exists
                if (nanoDevice.DebugEngine == null)
                {
                    nanoDevice.CreateDebugEngine();
                }

                // connect to the device
                if (nanoDevice.DebugEngine.Connect(
                    false,
                    true))
                {
                    // check that we are in CLR
                    if (nanoDevice.DebugEngine.IsConnectedTonanoCLR)
                    {
                        try
                        {
                            // get device info
                            INanoFrameworkDeviceInfo deviceInfo = nanoDevice.GetDeviceInfo(true);

                            // we have to have a valid device info
                            if (deviceInfo.Valid)
                            {
                                return true;
                            }
                            else
                            {
                                // report issue
                                throw new CantConnectToNanoDeviceException("Couldn't retrieve device details from nano device.");
                            }
                        }
                        catch
                        {
                            // report issue 
                            throw new CantConnectToNanoDeviceException("Couldn't retrieve device details from nano device.");
                        }
                    }
                    else
                    {
                        // we are in booter, can only get TargetInfo
                        try
                        {
                            // get device info
                            Debugger.WireProtocol.TargetInfo deviceInfo = nanoDevice.DebugEngine?.TargetInfo;

                            // we have to have a valid device info
                            if (deviceInfo != null)
                            {
                                return true;
                            }
                            else
                            {
                                // report issue 
                                throw new CantConnectToNanoDeviceException("Couldn't retrieve device details from nano device.");
                            }
                        }
                        catch
                        {
                            // report issue 
                            throw new CantConnectToNanoDeviceException("Couldn't retrieve device details from nano device.");
                        }
                    }
                }
                else
                {
                    // report issue 
                    throw new CantConnectToNanoDeviceException("Couldn't connect to specified nano device.");
                }
            }

            // default to false
            return false;
        }
    }
}
