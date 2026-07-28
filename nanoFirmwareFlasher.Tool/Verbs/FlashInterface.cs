// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Flashing interface to use with the <c>flash</c> verb.
    /// </summary>
    public enum FlashInterface
    {
        /// <summary>
        /// USB DFU. Uses the native DFU implementation, no external tool required.
        /// </summary>
        Dfu,

        /// <summary>
        /// JTAG/SWD via ST-LINK. Uses the native ST-LINK protocol, no external tool required.
        /// </summary>
        Jtag,

        /// <summary>
        /// SWD via a generic CMSIS-DAP probe.
        /// </summary>
        NativeSwd
    }
}
