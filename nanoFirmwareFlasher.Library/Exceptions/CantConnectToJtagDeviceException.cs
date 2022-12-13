//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't open the specified JTAG device.
    /// </summary>
    [Serializable]
    public class CantConnectToJtagDeviceException : Exception
    {
        /// <summary>
        /// Cannot connect to JTAG device exception.
        /// </summary>
        public CantConnectToJtagDeviceException()
        {
        }

        /// <summary>
        /// Cannot connect to JTAG device exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public CantConnectToJtagDeviceException(string message) : base(message)
        {
        }

        /// <summary>
        /// Cannot connect to JTAG device exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display</param>
        public CantConnectToJtagDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Cannot connect to JTAG device exception.
        /// </summary>
        /// <param name="info">Serialized information.</param>
        /// <param name="context">Streamed context.</param>
        protected CantConnectToJtagDeviceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}