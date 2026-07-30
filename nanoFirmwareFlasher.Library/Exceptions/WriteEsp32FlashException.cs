//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error writing to ESP32 flash.
    /// </summary>
    public class WriteEsp32FlashException : Exception
    {
        /// <summary>
        /// Error message from ESP32 serial operation.
        /// </summary>
        public string ExecutionError;

        /// <summary>
        /// Write the ESPP32 flash exception. 
        /// </summary>
        /// <param name="message">Message to display.</param>
        public WriteEsp32FlashException(string message) : base(message)
        {
            ExecutionError = message;
        }

        /// <summary>
        /// Write the ESPP32 flash exception. 
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public WriteEsp32FlashException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}