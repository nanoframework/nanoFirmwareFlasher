// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when an SMP/mcumgr protocol error occurs (e.g. bad response code, CRC mismatch).
    /// </summary>
    public class McumgrProtocolException : Exception
    {
        /// <summary>SMP error code returned by the device.</summary>
        public int ErrorCode { get; }

        /// <summary>
        /// SMP protocol exception.
        /// </summary>
        public McumgrProtocolException()
        {
        }

        /// <summary>
        /// SMP protocol exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public McumgrProtocolException(string message) : base(message)
        {
        }

        /// <summary>
        /// SMP protocol exception with the device-reported error code.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="errorCode">SMP error code returned by the device.</param>
        public McumgrProtocolException(string message, int errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// SMP protocol exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public McumgrProtocolException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
