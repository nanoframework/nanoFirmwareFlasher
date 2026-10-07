// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// MCUmgr transport parameters reported by the OS group "MCUmgr Parameters" command.
    /// Used to negotiate the upload chunk size so the client matches the device's buffers
    /// instead of assuming the maximum possible frame size.
    /// </summary>
    public record McumgrParameters
    {
        /// <summary>
        /// Size, in bytes, of a single MCUmgr transport buffer (the maximum SMP frame the
        /// device can receive, including the SMP header and CBOR payload). Zero when the
        /// device did not report a value.
        /// </summary>
        public int BufSize { get; set; }

        /// <summary>Number of MCUmgr transport buffers the device has available.</summary>
        public int BufCount { get; set; }

        /// <summary>
        /// <see langword="true"/> when the device implements the command (replied without an error code).
        /// <see langword="false"/> when it replied with an error such as rc=ENOTSUP, which is what MCUboot
        /// serial recovery does when built without <c>MCUBOOT_BOOT_MGMT_MCUMGR_PARAMS</c>.
        /// </summary>
        public bool Supported { get; set; }
    }
}
