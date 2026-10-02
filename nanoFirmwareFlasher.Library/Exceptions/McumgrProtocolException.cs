// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when an SMP/mcumgr protocol error occurs (e.g. bad response code, CRC mismatch).
    /// </summary>
    [Serializable]
    public class McumgrProtocolException : Exception
    {
        /// <summary>SMP error code returned by the device.</summary>
        public int ErrorCode { get; }

        /// <inheritdoc/>
        public McumgrProtocolException()
        {
        }

        /// <inheritdoc/>
        public McumgrProtocolException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a protocol exception with the device-reported error code.
        /// </summary>
        public McumgrProtocolException(string message, int errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <inheritdoc/>
        public McumgrProtocolException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
