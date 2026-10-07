// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exception thrown when an SMP command does not receive a response within the configured timeout.
    /// </summary>
    public class McumgrTimeoutException : Exception
    {
        /// <summary>
        /// SMP response timeout exception.
        /// </summary>
        public McumgrTimeoutException()
        {
        }

        /// <summary>
        /// SMP response timeout exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public McumgrTimeoutException(string message) : base(message)
        {
        }

        /// <summary>
        /// SMP response timeout exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public McumgrTimeoutException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
