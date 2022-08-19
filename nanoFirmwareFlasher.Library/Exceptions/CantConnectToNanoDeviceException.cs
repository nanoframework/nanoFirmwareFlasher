//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't open the specified .NET nanoFramework device.
    /// </summary>
    [Serializable]
    public class CantConnectToNanoDeviceException : Exception
    {
        public CantConnectToNanoDeviceException()
        {
        }

        public CantConnectToNanoDeviceException(string message) : base(message)
        {
        }

        public CantConnectToNanoDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CantConnectToNanoDeviceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
