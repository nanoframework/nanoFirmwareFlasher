//
// Copyright (c) 2020 The nanoFramework project contributors
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
    internal class CantOpenDfuDeviceException : Exception
    {
        public uint ErrorCode { get; }

        public CantOpenDfuDeviceException(uint errorCode)
        {
            ErrorCode = errorCode;
        }

        public CantOpenDfuDeviceException(uint errorCode, string message) : base(message)
        {
        }

        public CantOpenDfuDeviceException(uint errorCode, string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CantOpenDfuDeviceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
