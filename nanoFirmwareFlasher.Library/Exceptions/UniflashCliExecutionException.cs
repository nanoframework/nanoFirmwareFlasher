//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing TI Uniflash CLI command.
    /// </summary>
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
        /// <param name="message">Message to display.</param>
        public UniflashCliExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        /// <summary>
        /// UniFlash CLI Execution Exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public UniflashCliExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}