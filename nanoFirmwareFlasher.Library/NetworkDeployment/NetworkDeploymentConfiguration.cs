// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.NetworkDeployment
{
    /// <summary>
    /// Represents the configuration for network deployment.
    /// </summary>
    internal class NetworkDeploymentConfiguration
    {
        /// <summary>
        /// Gets or sets the serial port used for network deployment.
        /// </summary>
        public string SerialPort { get; set; }

        /// <summary>
        /// Gets or sets the wireless client configuration.
        /// </summary>
        public WirelessClient WirelessClient { get; set; }
    }
}
