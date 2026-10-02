// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when imgtool, required for MCUboot image signing and key management, can't be found.
    /// </summary>
    [Serializable]
    public class ImgtoolNotFoundException : McubootImageException
    {
        /// <inheritdoc/>
        public ImgtoolNotFoundException()
        {
        }

        /// <inheritdoc/>
        public ImgtoolNotFoundException(string message) : base(message)
        {
        }

        /// <inheritdoc/>
        public ImgtoolNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
