using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't read the USB descriptor of the DFU device.
    /// </summary>
    [Serializable]
    internal class CantReadUsbDescriptorException : Exception
    {
        public uint ErrorCode { get; }

        public CantReadUsbDescriptorException(uint errorCode)
        {
            ErrorCode = errorCode;
        }

        public CantReadUsbDescriptorException(uint errorCode, string message) : base(message)
        {
        }

        public CantReadUsbDescriptorException(uint errorCode, string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CantReadUsbDescriptorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
