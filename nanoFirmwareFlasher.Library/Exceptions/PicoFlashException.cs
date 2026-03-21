// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error flashing a Raspberry Pi Pico device.
    /// </summary>
    [Serializable]
    public class PicoFlashException : Exception
    {
        /// <summary>
        /// Pico flash exception.
        /// </summary>
        public PicoFlashException()
        {
        }

        /// <summary>
        /// Pico flash exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public PicoFlashException(string message) : base(message)
        {
        }

        /// <summary>
        /// Pico flash exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public PicoFlashException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Pico flash exception.
        /// </summary>
        /// <param name="info">Serialized information.</param>
        /// <param name="context">Streamed context.</param>
        protected PicoFlashException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
