using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Verification of DFU write failed.
    /// </summary>
    [Serializable]
    internal class DfuVerificationFailedException : Exception
    {
        public DfuVerificationFailedException()
        {
        }

        public DfuVerificationFailedException(string message) : base(message)
        {
        }

        public DfuVerificationFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DfuVerificationFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
