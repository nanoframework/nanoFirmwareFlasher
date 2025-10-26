// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace nanoFramework.Tools.FirmwareFlasher
{
    public class Options
    {
        #region STM32 DFU options

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
        public string DfuDeviceId { get; set; }

        [Option(
            "dfu",
            Required = false,
            Default = false,
            HelpText = "Use DFU to update the device.")]
        public bool DfuUpdate { get; set; }

        [Option(
            "installdfudrivers",
            Required = false,
            Default = false,
            HelpText = "Install STM32 DFU drivers.")]
        public bool InstallDfuDrivers { get; set; }

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


        [Option(
            "installjtagdrivers",
            Required = false,
            Default = false,
            HelpText = "Install STM32 JTAG drivers.")]
        public bool InstallJtagDrivers { get; set; }
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

        [Option(
            "checkpsram",
            Required = false,
            Default = false,
            HelpText = "Perform check for PSRAM in device.")]
        public bool CheckPsRam { get; set; }

        [Option(
            "nobackupconfig",
            Required = false,
            Default = false,
            HelpText = "Skip backup of configuration partition.")]
        public bool NoBackupConfig { get; set; }

        #endregion


        #region TI options


        [Option(
            "installxdsdrivers",
            Required = false,
            Default = false,
            HelpText = "Install XDS110 drivers.")]
        public bool TIInstallXdsDrivers { get; set; }

        #endregion


        #region Segger J-Link options

        [Option(
            "listjlink",
            Required = false,
            Default = false,
            HelpText = "List connected USB J-Link devices.")]
        public bool ListJLinkDevices { get; set; }

        [Option(
            "jlinkid",
            Required = false,
            Default = null,
            HelpText = "ID of the J-Link device to update. If not specified the first connected J-Link device will be used.")]
        public string JLinkDeviceId { get; set; }

        [Option(
            "setvcpbr",
            Required = false,
            HelpText = "Set baud rate of J-Link Virtual COM Port. If a value is not specified it will use the default value for Wire Protocol.")]
        public int? SetVcpBaudRate { get; set; }

        #endregion

        #region nano device options

        [Option(
            "nanodevice",
            Required = false,
            Default = false,
            HelpText = "Operations are to be performed to a nanoFramework device.")]
        public bool NanoDevice { get; set; }

        #endregion

        #region common options

        [Option(
            "clrfile",
            Required = false,
            Default = null,
            HelpText = "Path to file with CLR image.")]
        public string ClrFile { get; set; }

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
            HelpText = "Target platform. Acceptable values are: esp32, stm32, cc13x2, efm32.")]
        public SupportedPlatform? Platform { get; set; }

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
            Default = "n",
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
            "preview",
            Required = false,
            Default = false,
            HelpText = "Download a firmware package from the preview repository that includes major changes or experimental features.")]
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
            HelpText = "Address(es) where to flash the BIN file(s). Hexadecimal format (e.g. 0x08000000). Required when specifying a BIN file with --binfile argument or flashing a deployment image with --deploy argument.")]
        public IList<string> FlashAddress { get; set; }

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
            HelpText = "Skip execution of sanity check if the requested target fits the connected device. This is a best effort validation and it's NOT guaranted to be fail safe.")]
        public bool FitCheck { get; set; }

        [Option(
            "listtargets",
            Required = false,
            Default = false,
            HelpText = "List the available targets and versions. --platform and --preview options apply.")]
        public bool ListTargets { get; set; }

        [Option(
            "listports",
            Required = false,
            Default = false,
            HelpText = "List the all the COM ports on this machine.")]
        public bool ListComPorts { get; set; }

        [Option(
            "clearcache",
            Required = false,
            Default = false,
            HelpText = "Clear the cache folder with firmware images.")]
        public bool ClearCache { get; set; }

        [Option(
            "listdevices",
            Required = false,
            Default = false,
            HelpText = "List the .NET nanoFramework devices connected to the machine.")]
        public bool ListDevices { get; set; }

        [Option(
            "devicedetails",
            Required = false,
            Default = false,
            HelpText = "Reads details from connected device.")]
        public bool DeviceDetails { get; set; }

        [Option(
            "identifyfirmware",
            Required = false,
            Default = false,
            HelpText = "Show which firmware to use for a device without deploying anything.")]
        public bool IdentifyFirmware { get; set; }

        [Option(
            "filedeployment",
            Required = false,
            Default = null,
            HelpText = "JSON file containing file deployment settings.")]
        public string FileDeployment { get; set; }

        [Option(
            "networkdeployment",
            Required = false,
            Default = null,
            HelpText = "JSON file containing network deployment settings.")]
        public string NetworkDeployment { get; set; }

        [Option(
            "archivepath",
            Required = false,
            Default = null,
            HelpText = "Path of the directory where the firmware is archived.")]
        public string FwArchivePath { get; set; }

        [Option(
            "updatearchive",
            Required = false,
            Default = false,
            HelpText = "Copy the firmware from the online repository to the firmware archive directory; do not update the firmware on a connected device.")]
        public bool UpdateFwArchive { get; set; }

        [Option(
            "fromarchive",
            Required = false,
            Default = false,
            HelpText = "Get the firmware from the firmware archive rather than from the online repository.")]
        public bool FromFwArchive { get; set; }

        [Option(
            "suppressnanoffversioncheck",
            Required = false,
            Default = false,
            HelpText = $"Do not check whether a new version of {_APPLICATIONALIAS} is available.")]
        public bool SuppressNanoFFVersionCheck { get; set; }
        #endregion


        [Usage(ApplicationAlias = _APPLICATIONALIAS)]
        public static IEnumerable<Example> Examples =>
            [
                new("- Update ESP32 WROVER Kit device with latest available firmware", new Options { TargetName = "ESP_WROVER_KIT", Update = true }),
                new("- Update specific STM32 device (ST_STM32F769I_DISCOVERY) with latest available firmware, using JTAG interface", new Options { TargetName = "ST_STM32F769I_DISCOVERY" , Update = true, JtagUpdate = true}),
                new("- Update ESP32 device with latest available firmware (stable version), device is connected to COM31", new Options { Platform = SupportedPlatform.esp32, Update = true, SerialPort = "COM31" }),
                new("- Update specific ESP32 device with custom firmware (local bin file)", new Options { TargetName = "ESP_WROVER_KIT" , DeploymentImage = "<location of file>.bin"}),
                new("- Update specific Silabs device (Giant Gecko EVK) with latest available firmware", new Options { TargetName = "SL_STK3701A", Update = true }),
                new("- List all STM32 devices connected through JTAG", new Options { Platform = SupportedPlatform.stm32, ListJtagDevices = true}),
                new("- Install STM32 JTAG drivers", new Options { InstallJtagDrivers = true}),
                new("- List all available STM32 targets", new Options { ListTargets = true, Platform =  SupportedPlatform.stm32 }),
                new("- List all available COM ports", new Options { ListComPorts = true }),
            ];
        private const string _APPLICATIONALIAS = "nanoff";
    }
}
