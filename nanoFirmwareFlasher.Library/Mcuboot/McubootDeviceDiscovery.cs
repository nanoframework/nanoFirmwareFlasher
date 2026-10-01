// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Discovers devices answering SMP (MCUboot serial recovery) on serial ports.
    /// </summary>
    /// <remarks>
    /// MCUboot's boot_serial replies to any well-formed request, at least with rc=ENOTSUP, so the
    /// OS group "MCUmgr parameters" command is used as a cheap probe: a decodable reply (valid marker
    /// and CRC) proves an SMP responder is present, while anything else times out quickly.
    /// </remarks>
    public static class McubootDeviceDiscovery
    {
        /// <summary>Default SMP baud rate for MCUboot serial recovery.</summary>
        public const int DefaultBaudRate = 115_200;

        /// <summary>Default timeout for the discovery probe, in milliseconds.</summary>
        public const int DefaultProbeTimeoutMs = 500;

        /// <summary>
        /// Default timeout for the image list read, in milliseconds. MCUboot validates each slot's
        /// signature before reporting it, which can take several seconds on large slots.
        /// </summary>
        public const int DefaultDetailsTimeoutMs = 5_000;

        /// <summary>
        /// Probes the given serial ports in parallel and returns the ones that answered SMP.
        /// </summary>
        /// <param name="portNames">Serial ports to probe.</param>
        /// <param name="baudRate">Baud rate used for the SMP transport.</param>
        /// <param name="probeTimeoutMs">Timeout for the discovery probe.</param>
        /// <param name="detailsTimeoutMs">Timeout for the image list read.</param>
        /// <param name="readDetails">When <see langword="true"/>, the image list and the device info are read from each responsive device.</param>
        /// <param name="verbosity">Verbosity passed to the underlying <see cref="McumgrClient"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The responsive devices, ordered by port name.</returns>
        public static async Task<List<McubootDiscoveredDevice>> ProbeSerialPortsAsync(
            IEnumerable<string> portNames,
            int baudRate = DefaultBaudRate,
            int probeTimeoutMs = DefaultProbeTimeoutMs,
            int detailsTimeoutMs = DefaultDetailsTimeoutMs,
            bool readDetails = true,
            VerbosityLevel verbosity = VerbosityLevel.Normal,
            CancellationToken ct = default)
        {
            if (portNames is null)
            {
                return new List<McubootDiscoveredDevice>();
            }

            Task<McubootDiscoveredDevice>[] probes = portNames
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => Task.Run(
                    () => ProbePort(p, baudRate, probeTimeoutMs, detailsTimeoutMs, readDetails, verbosity, ct),
                    ct))
                .ToArray();

            McubootDiscoveredDevice[] results = await Task.WhenAll(probes).ConfigureAwait(false);

            return results
                .Where(d => d != null)
                .OrderBy(d => d.PortName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static McubootDiscoveredDevice ProbePort(
            string portName,
            int baudRate,
            int probeTimeoutMs,
            int detailsTimeoutMs,
            bool readDetails,
            VerbosityLevel verbosity,
            CancellationToken ct)
        {
            try
            {
                using var client = new McumgrClient(
                    portName,
                    baudRate,
                    probeTimeoutMs,
                    verbosity: verbosity);

                client.Open();

                McumgrParameters parameters = client.GetParametersAsync(ct).GetAwaiter().GetResult();

                var device = new McubootDiscoveredDevice
                {
                    PortName = portName,
                    Parameters = parameters,
                };

                if (readDetails)
                {
                    try
                    {
                        client.TimeoutMs = detailsTimeoutMs;
                        device.Images = client.GetImageListAsync(ct).GetAwaiter().GetResult();
                    }
                    catch (Exception ex) when (ex is McumgrTimeoutException or McumgrProtocolException or IOException or InvalidOperationException)
                    {
                        device.ImageListError = ex.Message;
                    }
                }

                if (readDetails)
                {
                    try
                    {
                        // older bootloaders reply rc=ENOTSUP, which decodes to an empty info
                        McumgrDeviceInfo info = client.GetDeviceInfoAsync(ct).GetAwaiter().GetResult();

                        if (!string.IsNullOrEmpty(info.TargetName))
                        {
                            device.DeviceInfo = info;
                        }
                    }
                    catch (Exception ex) when (ex is McumgrTimeoutException or McumgrProtocolException or IOException or InvalidOperationException)
                    {
                        // device info is optional
                    }
                }

                client.Close();

                return device;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is McumgrTimeoutException
                                       or UnauthorizedAccessException
                                       or IOException
                                       or ArgumentException
                                       or InvalidOperationException
                                       or TimeoutException)
            {
                // not an SMP device, port busy or unavailable: not a candidate
                return null;
            }
        }
    }
}
