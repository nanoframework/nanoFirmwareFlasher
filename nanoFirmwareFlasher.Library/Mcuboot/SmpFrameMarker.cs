// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Boot-serial framing marker bytes written before each base64 line.
    /// First line begins with <see cref="StartByte1"/> + <see cref="StartByte2"/>;
    /// continuation lines begin with <see cref="ContinuationByte1"/> + <see cref="ContinuationByte2"/>.
    /// </summary>
    internal enum SmpFrameMarker : byte
    {
        /// <summary>First byte of the start-of-frame marker (0x06).</summary>
        StartByte1 = 0x06,
        /// <summary>Second byte of the start-of-frame marker (0x09).</summary>
        StartByte2 = 0x09,
        /// <summary>First byte of the continuation-frame marker (0x04).</summary>
        ContinuationByte1 = 0x04,
        /// <summary>Second byte of the continuation-frame marker (0x14).</summary>
        ContinuationByte2 = 0x14,
    }
}
