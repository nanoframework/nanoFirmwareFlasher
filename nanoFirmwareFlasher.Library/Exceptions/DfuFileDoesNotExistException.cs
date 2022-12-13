//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// The DFU file specified does not exist.
    /// </summary>
    [Serializable]
    public class DfuFileDoesNotExistException : Exception
    {
        /// <summary>
        /// DFU file does not exist exception.
        /// </summary>
        public DfuFileDoesNotExistException()
        {
        }

        /// <summary>
        /// DFU file does not exist exception.
        /// </summary>
        /// <param name="message"></param>
        public DfuFileDoesNotExistException(string message) : base(message)
        {
        }

        /// <summary>
        /// DFU file does not exist exception.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public DfuFileDoesNotExistException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// DFU file does not exist exception.
        /// </summary>
        /// <param name="info">Serialized information.</param>
        /// <param name="context">Streamed context.</param>
        protected DfuFileDoesNotExistException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
