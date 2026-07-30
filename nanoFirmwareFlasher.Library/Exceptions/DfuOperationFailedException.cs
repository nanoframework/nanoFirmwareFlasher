//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Verification of DFU write failed.
    /// </summary>
    public class DfuOperationFailedException : Exception
    {
        /// <summary>
        /// DFU operation failed exception.
        /// </summary>
        public DfuOperationFailedException()
        {
        }

        /// <summary>
        /// DFU operation failed exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public DfuOperationFailedException(string message) : base(message)
        {
        }

        /// <summary>
        /// DFU operation failed exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public DfuOperationFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}