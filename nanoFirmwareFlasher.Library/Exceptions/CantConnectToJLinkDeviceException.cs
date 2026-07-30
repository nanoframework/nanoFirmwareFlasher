//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't open the specified J-Link device.
    /// </summary>
    public class CantConnectToJLinkDeviceException : Exception
    {
        /// <summary>
        /// Cannot connect to the J-Link device exception.
        /// </summary>
        public CantConnectToJLinkDeviceException()
        {
        }

        /// <summary>
        /// Cannot connect to the JLINK device exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public CantConnectToJLinkDeviceException(string message) : base(message)
        {
        }

        /// <summary>
        /// Cannot connect to the JLINK device exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public CantConnectToJLinkDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}