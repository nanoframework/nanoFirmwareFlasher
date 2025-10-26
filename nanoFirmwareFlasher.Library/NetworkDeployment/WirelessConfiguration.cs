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
    /// Represents the wireless configuration.
    /// </summary>
    public class WirelessConfiguration: Ethernet
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
    }
}
