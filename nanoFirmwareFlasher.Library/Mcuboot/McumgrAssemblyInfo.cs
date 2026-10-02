// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Describes a managed assembly present in the nanoFramework deployment partition.
    /// </summary>
    public class McumgrAssemblyInfo
    {
        /// <summary>Assembly name.</summary>
        public string Name { get; set; }

        /// <summary>Assembly version string.</summary>
        public string Version { get; set; }

        /// <summary>Assembly size in bytes.</summary>
        public int Size { get; set; }
    }
}
