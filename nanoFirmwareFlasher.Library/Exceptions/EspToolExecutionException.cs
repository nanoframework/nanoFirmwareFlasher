//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing an ESP32 serial protocol command.
    /// </summary>
    public class EspToolExecutionException : Exception
    {
        /// <summary>
        /// Error message from ESP32 serial operation.
        /// </summary>
        public string ExecutionError;

        /// <summary>
        /// ESP32 tool execution exception.
        /// </summary>
        public EspToolExecutionException() : base()
        {

        }

        /// <summary>
        /// ESP32 tool execution exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public EspToolExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        /// <summary>
        /// ESP32 tool execution exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public EspToolExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}