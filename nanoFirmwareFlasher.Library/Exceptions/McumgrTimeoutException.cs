// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when an SMP command does not receive a response within the configured timeout.
    /// </summary>
    [Serializable]
    public class McumgrTimeoutException : Exception
    {
        /// <inheritdoc/>
        public McumgrTimeoutException()
        {
        }

        /// <inheritdoc/>
        public McumgrTimeoutException(string message) : base(message)
        {
        }

        /// <inheritdoc/>
        public McumgrTimeoutException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
