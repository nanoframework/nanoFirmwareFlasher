//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error reading from ESP32 flash.
    /// </summary>
    [Serializable]
    public class ReadEsp32FlashException : Exception
    {
        /// <summary>
        /// Error message from esptool.
        /// </summary>
        public string ExecutionError;

        public ReadEsp32FlashException(string message) : base(message)
        {
            ExecutionError = message;
        }

        public ReadEsp32FlashException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ReadEsp32FlashException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
