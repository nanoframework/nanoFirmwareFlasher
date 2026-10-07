// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when imgtool, required for MCUboot image signing and key management, can't be found.
    /// </summary>
    public class ImgtoolNotFoundException : McubootImageException
    {
        /// <summary>
        /// imgtool not found exception.
        /// </summary>
        public ImgtoolNotFoundException()
        {
        }

        /// <summary>
        /// imgtool not found exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public ImgtoolNotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// imgtool not found exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public ImgtoolNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
