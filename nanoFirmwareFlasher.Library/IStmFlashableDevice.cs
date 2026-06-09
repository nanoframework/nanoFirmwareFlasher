//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Common interface for all STM32 flash-capable device classes (native and CLI-based).
    /// Allows manager dispatch logic to be consolidated.
    /// </summary>
    public interface IStmFlashableDevice
    {
        /// <summary>
        /// Gets whether a target device was successfully detected.
        /// </summary>
        bool DevicePresent { get; }

        /// <summary>
        /// Gets or sets whether to perform a mass erase before flashing.
        /// </summary>
        bool DoMassErase { get; set; }

        /// <summary>
        /// Gets or sets the verbosity level for output.
        /// </summary>
        VerbosityLevel Verbosity { get; set; }

        /// <summary>
        /// Flash HEX files to the connected device.
        /// </summary>
        ExitCodes FlashHexFiles(IList<string> files);

        /// <summary>
        /// Flash BIN files to the connected device at specified addresses.
        /// </summary>
        ExitCodes FlashBinFiles(IList<string> files, IList<string> addresses);
    }
}
