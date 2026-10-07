// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when a MCUboot image operation fails (signing, validation, etc.).
    /// </summary>
    public class McubootImageException : Exception
    {
        /// <summary>
        /// MCUboot image operation exception.
        /// </summary>
        public McubootImageException()
        {
        }

        /// <summary>
        /// MCUboot image operation exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public McubootImageException(string message) : base(message)
        {
        }

        /// <summary>
        /// MCUboot image operation exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public McubootImageException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
