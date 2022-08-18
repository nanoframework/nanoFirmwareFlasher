//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error writing to ESP32 flash.
    /// </summary>
    [Serializable]
    public class WriteEsp32FlashException : Exception
    {
        /// <summary>
        /// Error message from esptool.
        /// </summary>
        public string ExecutionError;

        public WriteEsp32FlashException(string message) : base(message)
        {
            ExecutionError = message;
        }

        public WriteEsp32FlashException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected WriteEsp32FlashException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
