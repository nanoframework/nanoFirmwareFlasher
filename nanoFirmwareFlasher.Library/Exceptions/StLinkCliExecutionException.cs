//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing STM32 Programmer CLI command.
    /// </summary>
    [Serializable]
    public class StLinkCliExecutionException : Exception
    {
        /// <summary>
        /// Error message from STM32 Programmer CLI.
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