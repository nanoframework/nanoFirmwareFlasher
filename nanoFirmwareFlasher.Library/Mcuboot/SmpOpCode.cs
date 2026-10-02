// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>SMP (Simple Management Protocol) operation codes encoded in bits 2–0 of nmgr header byte 0.</summary>
    internal enum SmpOpCode : byte
    {
        /// <summary>Read request sent by the manager.</summary>
        Read = 0,
        /// <summary>Read response returned by the device.</summary>
        ReadResponse = 1,
        /// <summary>Write request sent by the manager.</summary>
        Write = 2,
        /// <summary>Write response returned by the device.</summary>
        WriteResponse = 3,
    }
}
