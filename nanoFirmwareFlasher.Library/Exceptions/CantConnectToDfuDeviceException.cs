//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't open the specified DFU device.
    /// </summary>
    [Serializable]
    public class CantConnectToDfuDeviceException : Exception
    {
        public CantConnectToDfuDeviceException()
        {
        }

        public CantConnectToDfuDeviceException(string message) : base(message)
        {
        }

        public CantConnectToDfuDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CantConnectToDfuDeviceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
