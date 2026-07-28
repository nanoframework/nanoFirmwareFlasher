// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
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
            HelpText = "List the .NET nanoFramework devices connected to the machine.")]
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
            int count = new[] { o.Targets, o.Devices, o.Ports, o.Dfu, o.Jtag, o.JLink, o.NativeSwd }.Count(b => b);

            if (count == 0)
            {
                return "list requires one of targets, devices, ports, dfu, jtag, jlink or nativeswd.";
            }

            if (count > 1)
            {
                return "Only one of targets, devices, ports, dfu, jtag, jlink or nativeswd can be specified at a time.";
            }

            return null;
        }
    }
}
