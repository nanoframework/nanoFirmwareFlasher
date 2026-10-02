// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>Command IDs for the <see cref="SmpGroup.NanoFramework"/> custom group.</summary>
    internal enum NfCommandId : byte
    {
        /// <summary>Chunked upload of managed assemblies to the deployment partition.</summary>
        DeploymentUpload = 0,
        /// <summary>Query the deployment partition (start address, size, used bytes, assembly list).</summary>
        DeploymentStatus = 1,
        /// <summary>Erase the deployment partition.</summary>
        DeploymentErase = 2,
        /// <summary>Query nanoFramework runtime information (target, CLR version, OEM, MCUboot presence).</summary>
        DeviceInfo = 5,
    }
}
