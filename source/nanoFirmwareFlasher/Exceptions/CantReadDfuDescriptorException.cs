using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Couldn't read the DFU descriptor of the connected DFU device.
    /// </summary>
    [Serializable]
    internal class CantReadDfuDescriptorException : Exception
    {
        public uint ErrorCode { get; }

        public CantReadDfuDescriptorException(uint errorCode)
        {
            ErrorCode = errorCode;
        }

        public CantReadDfuDescriptorException(uint errorCode, string message) : base(message)
        {
        }

        public CantReadDfuDescriptorException(uint errorCode, string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CantReadDfuDescriptorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
