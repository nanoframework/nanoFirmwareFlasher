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
        public WirelessConfiguration WirelessClient { get; set; }

        /// <summary>
        /// Gets or sets the wireless access point configuration.
        /// </summary>
        public WirelessAccessPoint WirelessAccessPoint { get; set; }

        /// <summary>
        /// Gets or sets the ethernet configuration.
        /// </summary>
        public Ethernet Ethernet { get; set; }

        /// <summary>
        /// Gets or sets the device certificates.
        /// </summary>
        public string DeviceCertificates { get; set; }

        /// <summary>
        /// Gets or sets the path to the device certificates.
        /// </summary>
        public string DeviceCertificatesPath { get; set; }

        /// <summary>
        /// Gets or sets the CA certificates.
        /// </summary>
        public string CACertificates { get; set; }

        /// <summary>
        /// Gets or sets the path to the CA certificates.
        /// </summary>
        public string CACertificatesPath { get; set; }
    }
}
