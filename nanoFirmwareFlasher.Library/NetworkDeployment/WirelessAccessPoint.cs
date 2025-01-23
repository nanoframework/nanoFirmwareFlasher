// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.NetworkDeployment
{
    /// <summary>
    /// Represents a wireless access point configuration.
    /// </summary>
    public class WirelessAccessPoint
    {
        /// <summary>
        /// Gets or sets the SSID of the wireless network.
        /// </summary>
        public string Ssid { get; set; }

        /// <summary>
        /// Gets or sets the password of the wireless network.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the IPv4 address.
        /// </summary>
        public string IPv4Address { get; set; }

        /// <summary>
        /// Gets or sets the IPv4 netmask.
        /// </summary>
        public string IPv4NetMask { get; set; }

        /// <summary>
        /// Gets or sets the IPv4 gateway.
        /// </summary>
        public string IPv4Gateway { get; set; }

        /// <summary>
        /// Gets or sets the primary IPv4 DNS address.
        /// </summary>
        public string IPv4DNSAddress1 { get; set; }

        /// <summary>
        /// Gets or sets the secondary IPv4 DNS address.
        /// </summary>
        public string IPv4DNSAddress2 { get; set; }

        /// <summary>
        /// Gets or sets the authentication type.
        /// </summary>
        public string Authentication { get; set; }

        /// <summary>
        /// Gets or sets the encryption type.
        /// </summary>
        public string Encryption { get; set; }

        /// <summary>
        /// Gets or sets the configuration option.
        /// </summary>
        public string ConfigurationOption { get; set; }

        /// <summary>
        /// Gets or sets the radio type.
        /// </summary>
        public string RadioType { get; set; }

        /// <summary>
        /// Gets or setes the MAC address.
        /// </summary>
        public string MacAddress { get; set; }
    }
}
