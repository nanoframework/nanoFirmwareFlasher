// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>list</c> verb: list targets, devices, COM ports, or connected
    /// programming interfaces.
    /// </summary>
    [Verb("list", HelpText = "List targets, devices, COM ports, or connected programming interfaces.")]
    public class ListOptions : VerbOptionsBase
    {
        [Option(
            "targets",
            Required = false,
            Default = false,
            HelpText = "List the available targets and versions. platform and preview apply.")]
        public bool Targets { get; set; }

        [Option(
            "devices",
            Required = false,
            Default = false,
            HelpText = "List the .NET nanoFramework devices connected to the machine, including devices in MCUboot serial recovery.")]
        public bool Devices { get; set; }

        [Option(
            "ports",
            Required = false,
            Default = false,
            HelpText = "List all the COM ports on this machine.")]
        public bool Ports { get; set; }

        [Option(
            "dfu",
            Required = false,
            Default = false,
            HelpText = "List connected DFU devices.")]
        public bool Dfu { get; set; }

        [Option(
            "jtag",
            Required = false,
            Default = false,
            HelpText = "List connected STM32 JTAG/ST-LINK devices.")]
        public bool Jtag { get; set; }

        [Option(
            "jlink",
            Required = false,
            Default = false,
            HelpText = "List connected USB J-Link devices.")]
        public bool JLink { get; set; }

        [Option(
            "nativeswd",
            Required = false,
            Default = false,
            HelpText = "List connected CMSIS-DAP debug probes using native USB HID enumeration.")]
        public bool NativeSwd { get; set; }

        [Option(
            "images",
            Required = false,
            Default = false,
            HelpText = "List the images in the MCUboot primary and secondary slots via SMP. Requires mcuboot and serialport.")]
        public bool Images { get; set; }

        [Option(
            "mcuboot",
            Required = false,
            Default = false,
            HelpText = "Target device is running MCUboot. Required with images.")]
        public bool Mcuboot { get; set; }

        [Option(
            "serialport",
            Required = false,
            Default = null,
            HelpText = "Serial port where device is connected to. Required with images; with devices, limits the MCUboot probe to this port.")]
        public string SerialPort { get; set; }

        [Option(
            "platform",
            Required = false,
            Default = null,
            HelpText = "Target platform. Acceptable values are: esp32, stm32, cc13x2, efm32, rpi_pico.")]
        public SupportedPlatform? Platform { get; set; }

        [Option(
            "preview",
            Required = false,
            Default = false,
            HelpText = "Include targets from the preview repository that includes major changes or experimental features.")]
        public bool Preview { get; set; }

        [Option(
            "fromarchive",
            Required = false,
            Default = false,
            HelpText = "List targets from the firmware archive rather than from the online repository. Only applies to targets.")]
        public bool FromFwArchive { get; set; }

        [Option(
            "archivepath",
            Required = false,
            Default = null,
            HelpText = "Path of the directory where the firmware is archived. Required when fromarchive is specified.")]
        public string FwArchivePath { get; set; }

        /// <summary>
        /// Validates early constraints for the <c>list</c> verb.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        public static string Validate(ListOptions o)
        {
            string mutuallyExclusiveError = ValidateMutuallyExclusive(
                "list",
                requireOne: true,
                "targets, devices, ports, dfu, jtag, jlink, nativeswd or images",
                o.Targets, o.Devices, o.Ports, o.Dfu, o.Jtag, o.JLink, o.NativeSwd, o.Images);

            if (mutuallyExclusiveError != null)
            {
                return mutuallyExclusiveError;
            }

            if (o.Images && !o.Mcuboot)
            {
                return "images lists the MCUboot slots and requires mcuboot.";
            }

            if (o.Mcuboot && !o.Images)
            {
                return "mcuboot can only be used with images.";
            }

            if (o.Images && string.IsNullOrEmpty(o.SerialPort))
            {
                return "images requires serialport to specify the port for the SMP transport.";
            }

            if (!string.IsNullOrEmpty(o.SerialPort) && !o.Images && !o.Devices)
            {
                return "serialport can only be used with devices or images.";
            }

            if (o.FromFwArchive && !o.Targets)
            {
                return "fromarchive can only be used with targets.";
            }

            if (o.FromFwArchive && string.IsNullOrEmpty(o.FwArchivePath))
            {
                return "fromarchive requires archivepath to specify the firmware archive location.";
            }

            return null;
        }
    }
}
