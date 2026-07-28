// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>drivers</c> verb: show install instructions for the drivers
    /// required by a given flashing interface.
    /// </summary>
    [Verb("drivers", HelpText = "Show driver install instructions for a flashing interface.")]
    public class DriversOptions : VerbOptionsBase
    {
        [Option(
            "dfu",
            Required = false,
            Default = false,
            HelpText = "Show instructions to install STM32 DFU drivers.")]
        public bool Dfu { get; set; }

        [Option(
            "jtag",
            Required = false,
            Default = false,
            HelpText = "Show instructions to install STM32 JTAG drivers.")]
        public bool Jtag { get; set; }

        [Option(
            "xds",
            Required = false,
            Default = false,
            HelpText = "Install XDS110 drivers.")]
        public bool Xds { get; set; }

        /// <summary>
        /// Validates early constraints for the <c>drivers</c> verb.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        public static string Validate(DriversOptions o)
        {
            int count = (o.Dfu ? 1 : 0) + (o.Jtag ? 1 : 0) + (o.Xds ? 1 : 0);

            if (count == 0)
            {
                return "drivers requires one of dfu, jtag or xds.";
            }

            if (count > 1)
            {
                return "Only one of dfu, jtag or xds can be specified at a time.";
            }

            return null;
        }
    }
}
