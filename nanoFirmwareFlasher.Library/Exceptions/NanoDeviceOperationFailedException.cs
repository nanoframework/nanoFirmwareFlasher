//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Verification of DFU write failed.
    /// </summary>
    [Serializable]
    public class NanoDeviceOperationFailedException : Exception
    {
        /// <summary>
        /// NanoFramework Device Operation Exception.
        /// </summary>
        public NanoDeviceOperationFailedException()
        {
        }

        /// <summary>
        /// NanoFramework Device Operation Exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public NanoDeviceOperationFailedException(string message) : base(message)
        {
        }

        /// <summary>
        /// NanoFramework Device Operation Exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public NanoDeviceOperationFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// NanoFramework Device Operation Exception.
        /// </summary>
        /// <param name="info">Serialized information.</param>
        /// <param name="context">Streamed context.</param>
        protected NanoDeviceOperationFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
