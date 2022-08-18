//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't open the specified J-Link device.
    /// </summary>
    [Serializable]
    public class CantConnectToJLinkDeviceException : Exception
    {
        public CantConnectToJLinkDeviceException()
        {
        }

        public CantConnectToJLinkDeviceException(string message) : base(message)
        {
        }

        public CantConnectToJLinkDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CantConnectToJLinkDeviceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}