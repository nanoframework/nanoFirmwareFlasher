//
// Copyright (c) 2019 The nanoFramework project contributors
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
    internal class EspToolExecutionException : Exception
    {
        /// <summary>
        /// Error message from esptool.
        /// </summary>
        public string ExecutionError;

        public EspToolExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        public EspToolExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected EspToolExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
