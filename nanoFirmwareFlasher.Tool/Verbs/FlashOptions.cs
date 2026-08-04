// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options for the <c>flash</c> verb: flash firmware or custom binaries onto a target device.
    /// </summary>
    [Verb("flash", HelpText = "Flash firmware or custom binaries onto a target device.")]
    public class FlashOptions : VerbOptionsBase
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
            "dfu",
            Required = false,
            Default = false,
            HelpText = "Force USB DFU as the flashing interface (STM32 only). If omitted, along with jtag and nativeswd, the best available interface is auto-detected.")]
        public bool Dfu { get; set; }

        [Option(
            "jtag",
            Required = false,
            Default = false,
            HelpText = "Force JTAG/ST-LINK as the flashing interface (STM32 only). If omitted, along with dfu and nativeswd, the best available interface is auto-detected.")]
        public bool Jtag { get; set; }

        [Option(
            "nativeswd",
            Required = false,
            Default = false,
            HelpText = "Force a generic CMSIS-DAP probe as the flashing interface (STM32 only). If omitted, along with dfu and jtag, the best available interface is auto-detected.")]
        public bool NativeSwd { get; set; }

        [Option(
            "deviceid",
            Required = false,
            Default = null,
            HelpText = "ID of the device to update (DFU/JTAG/SWD probe, or J-Link probe for EFM32). If not specified the first connected matching device will be used.")]
        public string DeviceId { get; set; }

        [Option(
            "vcpbaud",
            Required = false,
            Default = null,
            HelpText = "Set baud rate of the J-Link Virtual COM Port (EFM32 only). If not specified it uses the default value for the wire protocol.")]
        public int? VcpBaudRate { get; set; }

        [Option(
            "serialport",
            Required = false,
            Default = null,
            HelpText = "Serial port where device is connected to.")]
        public string SerialPort { get; set; }

        [Option(
            "fwversion",
            Required = false,
            Default = null,
            HelpText = "Firmware version to flash the device with.")]
        public string FwVersion { get; set; }

        [Option(
            "preview",
            Required = false,
            Default = false,
            HelpText = "Download a firmware package from the preview repository that includes major changes or experimental features.")]
        public bool Preview { get; set; }

        [Option(
            "masserase",
            Required = false,
            Default = false,
            HelpText = "Mass erase the device flash before uploading the firmware.")]
        public bool MassErase { get; set; }

        [Option(
            "verify",
            Required = false,
            Default = false,
            HelpText = "Read back flash contents after programming and verify they match the source data.")]
        public bool Verify { get; set; }

        [Option(
            "reset",
            Required = false,
            Default = false,
            HelpText = "Perform reset on connected device after all other requested operations are successfully performed.")]
        public bool ResetMcu { get; set; }

        [Option(
            "nofitcheck",
            Required = false,
            Default = false,
            HelpText = "Skip execution of sanity check if the requested target fits the connected device. This is a best effort validation and it's NOT guaranteed to be fail safe.")]
        public bool NoFitCheck { get; set; }

        [Option(
            "image",
            Required = false,
            HelpText = "Local image file(s) to use instead of downloading from the online repository: a custom CLR binary (when used with target/platform or a bare serial port), or file(s) to flash directly to the device (STM32/Silabs). Raw binary by default; use hex if the file(s) are Intel HEX format.")]
        public IList<string> Image { get; set; }

        [Option(
            "hex",
            Required = false,
            Default = false,
            HelpText = "Treat image as Intel HEX file(s) (self-addressed) instead of raw binary. Only applies to direct STM32 JTAG-connected flashing.")]
        public bool Hex { get; set; }

        [Option(
            "address",
            Required = false,
            HelpText = "Address(es) where to flash the image file(s). Hexadecimal format (e.g. 0x08000000). Required when flashing a raw binary image directly (not needed with hex, or when image is a CLR override).")]
        public IList<string> FlashAddress { get; set; }

        [Option(
            "backup",
            Required = false,
            Default = null,
            HelpText = "Back up the device's entire current flash contents before flashing (ESP32 only). Takes an optional file path; if omitted, a randomly named file is generated. Omit this keyword entirely to skip the backup (this is the tool-wide default).")]
        public string Backup { get; set; }

        [Option(
            "restore",
            Required = false,
            Default = null,
            HelpText = "Also save a persistent copy of the configuration partition backup (ESP32 only) at this file path. The configuration partition is always automatically backed up before flashing and restored afterward regardless of this keyword; omit the path (or the keyword entirely) to keep the backup in a temporary file that's deleted once it's restored.")]
        public string ConfigBackupPath { get; set; }

        [Option(
            "checkpsram",
            Required = false,
            Default = false,
            HelpText = "Perform check for PSRAM in device.")]
        public bool CheckPsRam { get; set; }

        [Option(
            "fromarchive",
            Required = false,
            Default = false,
            HelpText = "Get the firmware from the firmware archive rather than from the online repository.")]
        public bool FromFwArchive { get; set; }

        [Option(
            "archivepath",
            Required = false,
            Default = null,
            HelpText = "Path of the directory where the firmware is archived. Required when fromarchive is specified.")]
        public string FwArchivePath { get; set; }

        [Option(
            "uf2deploy",
            Required = false,
            Default = false,
            HelpText = "Use UF2 mass storage to deploy the application instead of wire protocol. Requires the device to be in BOOTSEL mode.")]
        public bool Uf2Deploy { get; set; }

        [Option(
            "baud",
            Required = false,
            Default = 1500000,
            HelpText = "Baud rate to use for the serial port.")]
        public int BaudRate { get; set; }

        [Option(
            "flashmode",
            Required = false,
            Default = "dio",
            HelpText = "Flash mode to use.")]
        public string Esp32FlashMode { get; set; }

        [Option(
            "flashfreq",
            Required = false,
            Default = 40,
            HelpText = "Flash frequency to use [MHz].")]
        public int Esp32FlashFrequency { get; set; }

        [Option(
            "partitiontablesize",
            Required = false,
            Default = null,
            HelpText = "Partition table size to use. Valid sizes are: 2, 4, 8 and 16.")]
        public PartitionTableSize? Esp32PartitionTableSize { get; set; }

        /// <summary>
        /// Validates early constraints for the <c>flash</c> verb.
        /// </summary>
        /// <returns><see langword="null"/> if valid, or an error message describing the constraint violation.</returns>
        public static string Validate(FlashOptions o)
        {
            // image is only a raw-address flash when there's positive evidence of a direct
            // STM32/Silabs connection; otherwise it's a CLR override (target/platform-based
            // update, or a bare serial port for a generic nanoDevice), which needs no address.
            bool looksLikeClrOverride =
                !string.IsNullOrEmpty(o.TargetName) ||
                (o.Platform == null && !o.Dfu && !o.Jtag && !o.NativeSwd && !string.IsNullOrEmpty(o.SerialPort));

            if (o.Image != null && o.Image.Count > 0 && !o.Hex && !looksLikeClrOverride
                && (o.FlashAddress == null || o.FlashAddress.Count == 0))
            {
                return "image requires address to specify the flash address(es), unless hex is used.";
            }

            if (o.FromFwArchive && string.IsNullOrEmpty(o.FwArchivePath))
            {
                return "fromarchive requires archivepath to specify the firmware archive location.";
            }

            if ((o.Dfu ? 1 : 0) + (o.Jtag ? 1 : 0) + (o.NativeSwd ? 1 : 0) > 1)
            {
                return "Only one of dfu, jtag or nativeswd can be specified.";
            }

            return null;
        }
    }
}
