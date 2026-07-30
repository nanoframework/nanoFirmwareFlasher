//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't open the specified DFU device.
    /// </summary>
    public class CantConnectToDfuDeviceException : Exception
    {
        /// <summary>
        /// DFU device connection exception.
        /// </summary>
        public CantConnectToDfuDeviceException()
        {
        }

        /// <summary>
        /// DFU device connection exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public CantConnectToDfuDeviceException(string message) : base(message)
        {
        }

        /// <summary>
        /// DFU device connection exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public CantConnectToDfuDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}