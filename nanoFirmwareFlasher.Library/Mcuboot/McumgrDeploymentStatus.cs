// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Status of the nanoFramework deployment partition as reported by the SMP custom group.
    /// </summary>
    public class McumgrDeploymentStatus
    {
        /// <summary>Start address of the deployment region.</summary>
        public int RegionStart { get; set; }

        /// <summary>Total size of the deployment region in bytes.</summary>
        public int RegionSize { get; set; }

        /// <summary>Bytes currently used in the deployment region.</summary>
        public int RegionUsed { get; set; }

        /// <summary>Assemblies present in the deployment region.</summary>
        public List<McumgrAssemblyInfo> Assemblies { get; set; } = new List<McumgrAssemblyInfo>();
    }
}
