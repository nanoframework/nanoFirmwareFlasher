//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing an esptool command.
    /// </summary>
    [Serializable]
    public class EspToolExecutionException : Exception
    {
        /// <summary>
        /// Error message from esptool.
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

        /// <summary>
        /// ESP32 tool execution exception.
        /// </summary>
        /// <param name="info">Serialized information.</param>
        /// <param name="context">Streamed context</param>
        protected EspToolExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
