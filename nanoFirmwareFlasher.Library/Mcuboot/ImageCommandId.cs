// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>Command IDs for the <see cref="SmpGroup.Image"/> group.</summary>
    internal enum ImageCommandId : byte
    {
        /// <summary>Image state: read = list all slots; write = test or confirm an image.</summary>
        State = 0,
        /// <summary>Chunked firmware image upload to a target slot.</summary>
        Upload = 1,
    }
}
