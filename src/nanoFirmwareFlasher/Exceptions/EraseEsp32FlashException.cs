//
// Copyright (c) 2019 The nanoFramework project contributors
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
    internal class EraseEsp32FlashException : Exception
    {
        /// <summary>
        /// Error message from esptool.
        /// </summary>
        public string ExecutionError;

        public EraseEsp32FlashException(string message) : base(message)
        {
            ExecutionError = message;
        }

        public EraseEsp32FlashException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected EraseEsp32FlashException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
