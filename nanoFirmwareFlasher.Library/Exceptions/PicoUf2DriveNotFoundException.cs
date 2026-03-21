// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Pico UF2 drive not found. The device is not in BOOTSEL mode.
    /// </summary>
    [Serializable]
    public class PicoUf2DriveNotFoundException : Exception
    {
        /// <summary>
        /// Pico UF2 drive not found exception.
        /// </summary>
        public PicoUf2DriveNotFoundException()
        {
        }

        /// <summary>
        /// Pico UF2 drive not found exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public PicoUf2DriveNotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// Pico UF2 drive not found exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public PicoUf2DriveNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Pico UF2 drive not found exception.
        /// </summary>
        /// <param name="info">Serialized information.</param>
        /// <param name="context">Streamed context.</param>
        protected PicoUf2DriveNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
