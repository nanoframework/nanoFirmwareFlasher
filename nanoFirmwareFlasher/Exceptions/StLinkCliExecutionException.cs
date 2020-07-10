//
// Copyright (c) 2020 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing ST Link CLI command.
    /// </summary>
    [Serializable]
    internal class StLinkCliExecutionException : Exception
    {
        /// <summary>
        /// Error message from ST Link CLI.
        /// </summary>
        public string ExecutionError;

        public StLinkCliExecutionException()
        {
        }

        public StLinkCliExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        public StLinkCliExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected StLinkCliExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}