//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.FirmwareFlasher.UsbDfu
{
    /// <summary>
    /// Thrown when a USB control transfer times out at the transport layer
    /// (for example WinUSB returning ERROR_SEM_TIMEOUT).
    /// This is expected and transient while an STM32 DFU bootloader performs a
    /// long, synchronous flash operation (such as a mass erase) and stops
    /// servicing the control pipe. Callers may poll/retry until the operation
    /// completes.
    /// </summary>
    internal class DfuControlTimeoutException : DfuOperationFailedException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DfuControlTimeoutException"/> class.
        /// </summary>
        /// <param name="message">Message to display.</param>
        internal DfuControlTimeoutException(string message) : base(message)
        {
        }
    }
}
