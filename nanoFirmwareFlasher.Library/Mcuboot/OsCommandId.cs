// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>Command IDs for the <see cref="SmpGroup.Os"/> group.</summary>
    internal enum OsCommandId : byte
    {
        /// <summary>Loopback echo - returns the sent string. Used to verify SMP connectivity.</summary>
        Echo = 0,
        /// <summary>Resets (reboots) the device.</summary>
        Reset = 5,
        /// <summary>Reports the device's MCUmgr transport buffer size and count, used to negotiate the upload chunk size.</summary>
        McumgrParameters = 6,
    }
}
