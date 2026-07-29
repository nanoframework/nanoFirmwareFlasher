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
            "interface",
            Required = false,
            Default = null,
            HelpText = "Flashing interface to use. Acceptable values are: dfu, jtag, nativeswd. If omitted the best available interface is auto-detected.")]
        public FlashInterface? Interface { get; set; }

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
            "hexfile",
            Required = false,
            HelpText = "HEX file(s) to be flashed into the device. Only JTAG connected targets are supported.")]
        public IList<string> HexFile { get; set; }

        [Option(
            "binfile",
            Required = false,
            HelpText = "BIN file(s) to be flashed into the device.")]
        public IList<string> BinFile { get; set; }

        [Option(
            "address",
            Required = false,
            HelpText = "Address(es) where to flash the BIN file(s). Hexadecimal format (e.g. 0x08000000). Required when specifying a BIN file with the binfile keyword.")]
        public IList<string> FlashAddress { get; set; }

        [Option(
            "clrfile",
            Required = false,
            Default = null,
            HelpText = "Path to file with CLR image.")]
        public string ClrFile { get; set; }

        [Option(
            "backupflash",
            Required = false,
            Default = null,
            HelpText = "Back up the device's entire current flash contents to this file path before flashing (ESP32 only). Omit this keyword entirely to skip the backup (this is the tool-wide default).")]
        public string BackupFlash { get; set; }

        [Option(
            "restore",
            Required = false,
            Default = null,
            HelpText = "Path to also save a persistent copy of the configuration partition backup (ESP32 only). The configuration partition is always automatically backed up before flashing and restored afterward, regardless of this keyword; omitting it just means the backup is kept in a temporary file that's deleted once it's restored.")]
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
            if (o.BinFile != null && o.BinFile.Count > 0
                && (o.FlashAddress == null || o.FlashAddress.Count == 0))
            {
                return "binfile requires address to specify the flash address(es).";
            }

            if (o.FromFwArchive && string.IsNullOrEmpty(o.FwArchivePath))
            {
                return "fromarchive requires archivepath to specify the firmware archive location.";
            }

            return null;
        }
    }
}
