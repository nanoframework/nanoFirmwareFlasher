//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Supported Platform.
    /// </summary>
    public enum SupportedPlatform
    {
        /// <summary>
        /// ESP32.
        /// </summary>
        esp32 = 0,
        /// <summary>
        /// STM32.
        /// </summary>
        stm32 = 1,
        /// <summary>
        /// TI Simplelink.
        /// </summary>
        ti_simplelink = 2,

        /// <summary>
        /// Silabs EFM32 Gecko.
        /// </summary>
        efm32,

        /// <summary>
        /// NXP.
        /// </summary>
        nxp
    }
}
