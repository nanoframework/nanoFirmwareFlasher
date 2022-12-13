//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing TI Uniflash CLI command.
    /// </summary>
    [Serializable]
    public class UniflashCliExecutionException : Exception
    {
        /// <summary>
        /// Error message from UniFlash CLI.
        /// </summary>
        public string ExecutionError;

        /// <summary>
        /// UniFlash CLI Execution Exception.
        /// </summary>
        public UniflashCliExecutionException()
        {
        }

        /// <summary>
        /// UniFlash CLI Execution Exception.
        /// </summary>
        /// <param name="message">Message to display</param>
        public UniflashCliExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        /// <summary>
        /// UniFlash CLI Execution Exception.
        /// </summary>
        /// <param name="message">Message to display</param>
        /// <param name="innerException">The exception to display</param>
        public UniflashCliExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// UniFlash CLI Execution Exception.
        /// </summary>
        /// <param name="info">Serialized information</param>
        /// <param name="context">Streamed context</param>
        protected UniflashCliExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}