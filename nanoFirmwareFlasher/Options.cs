//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommandLine;
using CommandLine.Text;
using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher
{
    public class Options
    {
        #region STM32 DFU options

        [Option(
            "dfufile", 
            Required = false,
            Default = null,
            HelpText = "DFU file to be flashed into the device.")]
        public string DfuFile { get; set; }

        [Option(
            "listdfu",
            Required = false,
            Default = false,
            HelpText = "List connected DFU devices.")]
        public bool ListDevicesInDfuMode { get; set; }

        [Option(
            "dfuid",
            Required = false,
            Default = null,
            HelpText = "ID of the DFU device to update. If not specified the first connected DFU device will be used.")]
        public string DfuDeviceId{ get; set; }

        [Option(
            "dfu",
            Required = false,
            Default = false,
            HelpText = "Use DFU to update the device.")]
        public bool DfuUpdate { get; set; }

        #endregion


        #region STM32 JTAG options

        [Option(
            "jtagid",
            Required = false,
            Default = null,
            HelpText = "ID of the JTAG device to update. If not specified the first connected JTAG device will be used.")]
        public string JtagDeviceId { get; set; }

        [Option(
            "listjtag",
            Required = false,
            Default = false,
            HelpText = "List connected STM32 JTAG devices.")]
        public bool ListJtagDevices { get; set; }

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
            "jtag",
            Required = false,
            Default = false,
            HelpText = "Use JTAG to update the device.")]
        public bool JtagUpdate { get; set; }

        #endregion


        #region ESP32 options

        [Option(
            "serialport",
            Required = false,
            Default = null,
            HelpText = "Serial port where device is connected to.")]
        public string SerialPort { get; set; }

        [Option(
            "baud",
            Required = false,
            Default = 921600,
            HelpText = "Baud rate to use for the serial port.")]
        public int BaudRate{ get; set; }

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

        /// <summary>
        /// Allowed values:
        /// 2
        /// 4
        /// 8
        /// 16
        /// </summary>
        [Option(
            "partitiontablesize",
            Required = false,
            Default = null,
            HelpText = "Partition table size to use. Valid sizes are: 2, 4, 8 and 16.")]
        public PartitionTableSize? Esp32PartitionTableSize { get; set; }
        #endregion


        # region TI options


        [Option(
            "installdrivers",
            Required = false,
            Default = false,
            HelpText = "Install XDS110 drivers.")]
        public bool TIInstallXdsDrivers { get; set; }

        #endregion


        #region common options

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
            HelpText = "Target platform. Acceptable values are: esp32, stm32, cc13x2.")]
        public string Platform { get; set; }

        /// <summary>
        /// Allowed values:
        /// q[uiet]
        /// m[inimal]
        /// n[ormal]
        /// d[etailed]
        /// diag[nostic]
        /// </summary>
        [Option(
            'v',
            "verbosity",
            Required = false,
            Default = "d",
            HelpText = "Sets the verbosity level of the command. Allowed values are q[uiet], m[inimal], n[ormal], d[etailed], and diag[nostic]. Not supported in every command; see specific command page to determine if this option is available.")]
        public string Verbosity { get; set; }

        [Option(
            "backuppath",
            Required = false,
            Default = null,
            HelpText = "Path where to store the backup file.")]
        public string BackupPath { get; set; }

        [Option(
            "backupfile",
            Required = false,
            Default = null,
            HelpText = "Backup file name. Just the file name without path. If the file name isn't provided one will be generated.")]
        public string BackupFile { get; set; }

        [Option(
            "fwversion",
            Required = false,
            Default = null,
            HelpText = "Firmware version to flash the device with.")]
        public string FwVersion { get; set; }

        [Option(
            "update",
            Required = false,
            Default = false,
            HelpText = "Update the device firmware using the other specified options.")]
        public bool Update { get; set; }

        [Option(
            "deploy",
            Required = false,
            Default = false,
            HelpText = "Flash a deployment image specified with --image.")]
        public bool Deploy { get; set; }

        [Option(
            "stable",
            Required = false,
            Default = false,
            HelpText = "Preview version. Will download the firmware package from the preview repository.")]
        public bool Preview { get; set; }

        [Option(
            "image",
            Required = false,
            Default = null,
            HelpText = "Path to deployment image file to be uploaded to device.")]
        public string DeploymentImage { get; set; }

        [Option(
            "masserase",
            Required = false,
            Default = false,
            HelpText = "Mass erase the device flash before uploading the firmware. If more than one file is specified to be flashed the mass erase will be performed before the first file is flashed.")]
        public bool MassErase { get; set; }

        [Option(
            "address",
            Required = false,
            HelpText = "Address(es) where to flash the BIN file(s). Hexadecimal format (e.g. 0x08000000). Required when specifying a BIN file with -binfile argument or flashing a deployment image with -deployment argument.")]
        public IList<string> FlashAddress { get; set; }

        [Option(
            "reset",
            Required = false,
            Default = false,
            HelpText = "Perform reset on connected device after all other requested operations are successfully performed.")]
        public bool ResetMcu { get; set; }

        #endregion


        [Usage(ApplicationAlias = "nanoff")]
        public static IEnumerable<Example> Examples =>
            new List<Example>
            {
                new("Update ESP32 device with latest available firmware (preview version)", new Options { TargetName = "ESP32_WROOM_32" , Update = true, Preview = true}),
                new("Update ESP32 device with latest available firmware (stable version)", new Options { TargetName = "ESP32_WROOM_32", Update = true }),
                new("Update ESP32 device with latest available firmware (stable version), device is connected to COM31", new Options { TargetName = "ESP32_WROOM_32", Update = true, SerialPort = "COM31" }),
                new("Update ESP32 device with custom firmware (local bin file)", new Options { TargetName = "ESP32_WROOM_32" , DeploymentImage = "<location of file>.bin"}),
                new("Update specific STM32 device (ST_STM32F769I_DISCOVERY) with latest available firmware (preview version), using JTAG interface", new Options { TargetName = "ST_STM32F769I_DISCOVERY" , Update = true, Preview = true, JtagUpdate = true}),
                new("Update specific STM32 device (NETDUINO3_WIFI) with latest available firmware (preview version), device is connected through DFU with Id 3380386D3134", new Options { TargetName = "NETDUINO3_WIFI",  Update = true, Preview = true, DfuDeviceId = "3380386D3134" }),
                new("List all STM32 devices connected through JTAG", new Options { Platform = "stm32", ListJtagDevices = true}),
            };
    }

    public enum VerbosityLevel
    {
        Quiet = 0,
        Minimal = 1,
        Normal = 2,
        Detailed = 3,
        Diagnostic = 4
    }

    public enum PartitionTableSize
    {
        _2 = 2,
        _4 = 4,
        _8 = 8,
        _16 = 16,
    }
}
