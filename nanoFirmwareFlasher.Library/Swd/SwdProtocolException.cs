//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    /// <summary>
    /// Error communicating via CMSIS-DAP SWD protocol.
    /// </summary>
    public class SwdProtocolException : Exception
    {
        /// <summary>
        /// SWD protocol communication exception.
        /// </summary>
        public SwdProtocolException()
        {
        }

        /// <summary>
        /// SWD protocol communication exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public SwdProtocolException(string message) : base(message)
        {
        }

        /// <summary>
        /// SWD protocol communication exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public SwdProtocolException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}