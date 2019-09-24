using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// The DFU file specified does not exist.
    /// </summary>
    [Serializable]
    internal class DfuFileDoesNotExistException : Exception
    {
        public DfuFileDoesNotExistException()
        {
        }

        public DfuFileDoesNotExistException(string message) : base(message)
        {
        }

        public DfuFileDoesNotExistException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DfuFileDoesNotExistException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
