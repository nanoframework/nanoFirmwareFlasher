// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>details</c> verb: read details from a connected device.
    /// </summary>
    [Verb("details", HelpText = "Read details from a connected device.")]
    public class DetailsOptions : VerbOptionsBase
    {
        [Option(
            "target",
            Required = false,
            Default = null,
            HelpText = "Target name. This is the target name used in the GitHub and Cloudsmith repositories.")]
        public string TargetName { get; set; }

        [Option(
            "platform",
            Required = false,
            Default = null,
            HelpText = "Target platform. Acceptable values are: esp32, stm32, cc13x2, efm32, rpi_pico.")]
        public SupportedPlatform? Platform { get; set; }

        [Option(
            "serialport",
            Required = false,
            Default = null,
            HelpText = "Serial port where device is connected to.")]
        public string SerialPort { get; set; }
    }
}
