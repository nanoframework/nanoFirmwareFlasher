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

        /// <summary>
        /// STM32 Programmer CLI Exception.
        /// </summary>
        public StLinkCliExecutionException()
        {
        }

        /// <summary>
        /// STM32 Programmer CLI Exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public StLinkCliExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        /// <summary>
        /// STM32 Programmer CLI Exception.
        /// </summary>
        /// <param name="message">Message to display</param>
        /// <param name="innerException">The exception to display</param>
        public StLinkCliExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// STM32 Programmer CLI Exception.
        /// </summary>
        /// <param name="info">Serialized information</param>
        /// <param name="context">Streamed context</param>
        protected StLinkCliExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}