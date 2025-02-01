// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.NetworkDeployment
{
    /// <summary>
    /// Represents a wireless access point configuration.
    /// </summary>
    public class WirelessAccessPoint : WirelessConfiguration
    {
        /// <summary>
        /// Gets or sets the maximum number of connections.
        /// </summary>
        public byte MaxConnections { get; set; } = 4;

        /// <summary>
        /// Gets or sets the access point options.
        /// </summary>
        public string AccessPointOptions { get; set; }
    }
}
