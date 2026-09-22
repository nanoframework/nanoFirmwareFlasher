// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Asks a device running nanoFramework to reboot into the ESP32 ROM download mode, sparing the
    /// operator the BOOT button on boards whose USB has no reset lines to drive.
    /// </summary>
    internal static class Esp32DownloadMode
    {
        /// <summary>
        /// Timeout for the debug engine connection, in milliseconds. A device that is busy running
        /// an application can take its time answering.
        /// </summary>
        private const int ConnectTimeout = 5000;

        /// <summary>
        /// Timeout for the reboot request, in milliseconds. Capped because older debug library
        /// versions wait five times this long for a ping that a device in the ROM never sends.
        /// </summary>
        private const int RebootTimeout = 2000;

        /// <summary>
        /// Requests the ROM download mode from the firmware running on the specified serial port.
        /// </summary>
        /// <param name="serialPort">Serial port where the device is connected.</param>
        /// <param name="verbosity">Verbosity level.</param>
        /// <returns><see langword="true"/> if the request was sent to a device reporting a proprietary bootloader.</returns>
        internal static bool TryRequest(string serialPort, VerbosityLevel verbosity)
        {
            PortBase serialDebugClient = PortBase.CreateInstanceForSerial(false);
            NanoDeviceBase device = null;

            try
            {
                device = serialDebugClient.AddDevice(serialPort);

                if (device is null)
                {
                    return false;
                }

                if (device.DebugEngine is null)
                {
                    device.CreateDebugEngine();
                }

                if (!device.DebugEngine.Connect(ConnectTimeout, true, true))
                {
                    return false;
                }

                if (!device.DebugEngine.HasProprietaryBooter)
                {
                    if (verbosity >= VerbosityLevel.Detailed)
                    {
                        OutputWriter.WriteLine($"Device on {serialPort} doesn't report a proprietary bootloader.");
                    }

                    return false;
                }

                device.DebugEngine.DefaultTimeout = RebootTimeout;
                device.DebugEngine.RebootDevice(RebootOptions.EnterProprietaryBooter);

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Device on {serialPort} asked to reboot into the ROM download mode.");
                }

                return true;
            }
            catch (Exception ex)
            {
                // a device that doesn't answer the wire protocol is not an error here: the caller
                // carries on with the reset sequences
                if (verbosity >= VerbosityLevel.Diagnostic)
                {
                    OutputWriter.WriteLine($"Download mode request failed: {ex.Message}");
                }

                return false;
            }
            finally
            {
                // stopping the engine disconnects the device and releases the serial port
                device?.DebugEngine?.Stop(true);
                serialDebugClient.StopDeviceWatchers();
            }
        }
    }
}
