using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// The connected DFU device is running a non supported version of the DFU protocol.
    /// </summary>
    [Serializable]
    internal class BadDfuProtocolVersionException : Exception
    {
        public ushort DfuVersion { get; }

        public BadDfuProtocolVersionException()
        {
        }

        public BadDfuProtocolVersionException(ushort bcdDFUVersion)
        {
            DfuVersion = bcdDFUVersion;
        }

        public BadDfuProtocolVersionException(string message) : base(message)
        {
        }

        public BadDfuProtocolVersionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected BadDfuProtocolVersionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
