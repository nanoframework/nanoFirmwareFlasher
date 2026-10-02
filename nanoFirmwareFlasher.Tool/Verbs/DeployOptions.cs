// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>deploy</c> verb: deploy an application image, or a file/network
    /// deployment package, to a device that is already running nanoFramework.
    /// </summary>
    [Verb("deploy", HelpText = "Deploy an application image, or a file/network deployment package, to a device.")]
    public class DeployOptions : VerbOptionsBase
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

        [Option(
            "image",
            Required = false,
            Default = null,
            HelpText = "Path to deployment image file to be uploaded to device.")]
        public string DeploymentImage { get; set; }

        [Option(
            "address",
            Required = false,
            HelpText = "Address where to flash the deployment image. Hexadecimal format (e.g. 0x08040000). Required for STM32 targets; optional for ESP32/RP2040/RP2350 targets, which fall back to the firmware's default deployment partition address.")]
        public IList<string> Address { get; set; }

        [Option(
            "file",
            Required = false,
            Default = null,
            HelpText = "JSON file containing file deployment settings.")]
        public string FileDeployment { get; set; }

        [Option(
            "network",
            Required = false,
            Default = null,
            HelpText = "JSON file containing network deployment settings.")]
        public string NetworkDeployment { get; set; }

        [Option(
            "mcuboot",
            Required = false,
            Default = false,
            HelpText = "Target device is running MCUboot: the deployment image is uploaded to MCUboot Image 1 via the SMP serial transport. Requires serialport and image.")]
        public bool Mcuboot { get; set; }

        [Option(
            "signkey",
            Required = false,
            Default = null,
            HelpText = "Path to the PEM key used to sign the image with imgtool before uploading it (MCUboot only).")]
        public string SignKey { get; set; }

        [Option(
            "slotsize",
            Required = false,
            Default = null,
            HelpText = "MCUboot image slot size in bytes, used when signing (default 0x100000).")]
        public string SlotSize { get; set; }

        [Option(
            "headersize",
            Required = false,
            Default = null,
            HelpText = "MCUboot image header size in bytes, used when signing (default 0x200).")]
        public string HeaderSize { get; set; }

        [Option(
            "writealign",
            Required = false,
            Default = null,
            HelpText = "Flash write alignment in bytes, used when signing (default 4).")]
        public string WriteAlign { get; set; }

        [Option(
            "secondaryslot",
            Required = false,
            Default = false,
            HelpText = "Upload the image to the MCUboot secondary slot instead of the primary one, and mark it to be swapped in on next reset (MCUboot only).")]
        public bool SecondarySlot { get; set; }

        /// <summary>
        /// Validates early constraints for the <c>deploy</c> verb.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        public static string Validate(DeployOptions o)
        {
            string mcubootError = ValidateMcubootUpload(o.Mcuboot, o.SerialPort, o.SignKey, o.SlotSize, o.HeaderSize, o.WriteAlign, o.SecondarySlot);

            if (mcubootError != null)
            {
                return mcubootError;
            }

            if (o.Mcuboot)
            {
                if (!string.IsNullOrEmpty(o.FileDeployment)
                    || !string.IsNullOrEmpty(o.NetworkDeployment)
                    || (o.Address != null && o.Address.Count > 0))
                {
                    return "mcuboot can't be combined with file, network or address.";
                }

                if (string.IsNullOrEmpty(o.DeploymentImage))
                {
                    return "mcuboot requires image to specify the deployment image to upload.";
                }

                return null;
            }

            return ValidateMutuallyExclusive(
                "deploy",
                requireOne: true,
                "image, file or network",
                !string.IsNullOrEmpty(o.DeploymentImage),
                !string.IsNullOrEmpty(o.FileDeployment),
                !string.IsNullOrEmpty(o.NetworkDeployment));
        }
    }
}
