// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>SMP management group identifiers (16-bit group number in the nmgr header).</summary>
    internal enum SmpGroup : ushort
    {
        /// <summary>OS group (0): echo, reset, task/memory statistics.</summary>
        Os = 0,
        /// <summary>Image group (1): firmware image upload, slot listing, test, and confirm.</summary>
        Image = 1,
        /// <summary>nanoFramework custom group (64): deployment partition and device-info commands.</summary>
        NanoFramework = 64,
    }
}
