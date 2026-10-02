// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when a MCUboot image operation fails (signing, validation, etc.).
    /// </summary>
    [Serializable]
    public class McubootImageException : Exception
    {
        /// <inheritdoc/>
        public McubootImageException()
        {
        }

        /// <inheritdoc/>
        public McubootImageException(string message) : base(message)
        {
        }

        /// <inheritdoc/>
        public McubootImageException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
