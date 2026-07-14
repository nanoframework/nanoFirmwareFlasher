//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't open the specified .NET nanoFramework device.
    /// </summary>
    public class CantConnectToNanoDeviceException : Exception
    {
        /// <summary>
        /// Cannot connect to device exception.
        /// </summary>
        public CantConnectToNanoDeviceException()
        {
        }

        /// <summary>
        /// Cannot connect to device exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public CantConnectToNanoDeviceException(string message) : base(message)
        {
        }

        /// <summary>
        /// Cannot connect to device exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public CantConnectToNanoDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}