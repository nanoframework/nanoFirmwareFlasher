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
    internal class CantConnectToJtagDeviceException : Exception
    {
        public CantConnectToJtagDeviceException()
        {
        }

        public CantConnectToJtagDeviceException(string message) : base(message)
        {
        }

        public CantConnectToJtagDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CantConnectToJtagDeviceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}