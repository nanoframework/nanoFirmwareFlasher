//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error executing SI Link command.
    /// </summary>
    public class SilinkExecutionException : Exception
    {
        /// <summary>
        /// Error message from SI Link.
        /// </summary>
        public string ExecutionError;

        /// <summary>
        /// SI Link Exception.
        /// </summary>
        public SilinkExecutionException()
        {
        }

        /// <summary>
        /// SI Link Exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public SilinkExecutionException(string message) : base(message)
        {
            ExecutionError = message;
        }

        /// <summary>
        /// SI Link Exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public SilinkExecutionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}