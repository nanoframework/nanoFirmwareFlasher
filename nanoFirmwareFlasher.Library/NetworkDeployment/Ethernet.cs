// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher.NetworkDeployment
{
    /// <summary>
    ///  Represents en ethernet configuration.
    /// </summary>
    public class Ethernet
    {
        /// <summary>
        /// Gets or sets a value indicating whether DHCP is enabled.
        /// </summary>
        public bool DhcpEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether automatic DNS is enabled.
        /// </summary>
        public bool AutomaticDNS { get; set; } = true;

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
        /// Gets or setes the MAC address.
        /// </summary>
        public string MacAddress { get; set; }
    }
}
