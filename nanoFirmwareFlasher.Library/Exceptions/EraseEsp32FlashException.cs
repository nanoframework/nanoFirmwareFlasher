//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error erasing ESP32 flash.
    /// </summary>
    [Serializable]
    public class EraseEsp32FlashException : Exception
    {
        /// <summary>
        /// Error message from esptool.
        /// </summary>
        public string ExecutionError;

        /// <summary>
        /// ESP32 tool erase exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public EraseEsp32FlashException(string message) : base(message)
        {
            ExecutionError = message;
        }

        /// <summary>
        /// ESP32 tool erase exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public EraseEsp32FlashException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// ESP32 tool erase exception.
        /// </summary>
        /// <param name="info">Serialized information</param>
        /// <param name="context">Streamed context</param>
        protected EraseEsp32FlashException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
