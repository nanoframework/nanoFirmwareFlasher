//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Error communicating with STM32 UART bootloader.
    /// </summary>
    [Serializable]
    public class Stm32UartBootloaderException : Exception
    {
        /// <summary>
        /// STM32 UART bootloader communication exception.
        /// </summary>
        public Stm32UartBootloaderException()
        {
        }

        /// <summary>
        /// STM32 UART bootloader communication exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        public Stm32UartBootloaderException(string message) : base(message)
        {
        }

        /// <summary>
        /// STM32 UART bootloader communication exception.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="innerException">The exception to display.</param>
        public Stm32UartBootloaderException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// STM32 UART bootloader communication exception.
        /// </summary>
        /// <param name="info">Serialized information.</param>
        /// <param name="context">Streamed context.</param>
        protected Stm32UartBootloaderException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
