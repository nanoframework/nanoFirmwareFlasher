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

        public EspToolExecutionException() : base()
        {

        }

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
