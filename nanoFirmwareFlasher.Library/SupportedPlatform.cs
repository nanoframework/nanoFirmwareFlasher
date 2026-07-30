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
        nxp,

        /// <summary>
        /// Raspberry Pi Pico (RP2040/RP2350).
        /// </summary>
        rpi_pico
    }

    /// <summary>
    /// Extension methods for <see cref="SupportedPlatform"/>.
    /// </summary>
    public static class SupportedPlatformExtensions
    {
        /// <summary>
        /// Infers the <see cref="SupportedPlatform"/> from a target name prefix.
        /// </summary>
        /// <param name="targetName">The target name (e.g. "ST_STM32F769I_DISCOVERY", "ESP_WROVER_KIT").</param>
        /// <returns>The inferred platform, or <see langword="null"/> if no match.</returns>
        internal static SupportedPlatform? InferFromTargetName(string targetName)
        {
            if (string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            if (targetName.StartsWith("ESP")
                || targetName.StartsWith("M5")
                || targetName.StartsWith("FEATHER")
                || targetName.StartsWith("ESPKALUGA"))
            {
                return SupportedPlatform.esp32;
            }

            if (targetName.StartsWith("ST")
                || targetName.StartsWith("MBN_QUAIL")
                || targetName.StartsWith("NETDUINO3")
                || targetName.StartsWith("GHI")
                || targetName.StartsWith("IngenuityMicro")
                || targetName.StartsWith("WeAct")
                || targetName.StartsWith("ORGPAL")
                || targetName.StartsWith("Pyb")
                || targetName.StartsWith("NESHTEC_NESHNODE_V"))
            {
                return SupportedPlatform.stm32;
            }

            if (targetName.StartsWith("TI"))
            {
                return SupportedPlatform.ti_simplelink;
            }

            if (targetName.StartsWith("SL"))
            {
                return SupportedPlatform.efm32;
            }

            return null;
        }
    }
}
