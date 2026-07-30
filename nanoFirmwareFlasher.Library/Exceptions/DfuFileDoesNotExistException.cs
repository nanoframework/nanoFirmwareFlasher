//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// The DFU file specified does not exist.
    /// </summary>
    public class DfuFileDoesNotExistException : Exception
    {
        /// <summary>
        /// DFU file does not exist exception.
        /// </summary>
        public DfuFileDoesNotExistException()
        {
        }

        /// <summary>
        /// DFU file does not exist exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public DfuFileDoesNotExistException(string message) : base(message)
        {
        }

        /// <summary>
        /// DFU file does not exist exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public DfuFileDoesNotExistException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}