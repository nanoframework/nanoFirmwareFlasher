//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// No operations was performed during the process from the manager.
    /// </summary>
    public class NoOperationPerformedException : Exception
    {
        /// <summary>
        /// No operation performed exception.
        /// </summary>
        public NoOperationPerformedException()
        {
        }

        /// <summary>
        /// No operation performed exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public NoOperationPerformedException(string message) : base(message)
        {
        }

        /// <summary>
        /// No operation performed exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public NoOperationPerformedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}