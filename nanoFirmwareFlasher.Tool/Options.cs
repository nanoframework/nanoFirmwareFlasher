// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Internal data bag describing a single operation to dispatch to a platform manager
    /// (<see cref="Esp32Manager"/>, <see cref="Stm32Manager"/>, <see cref="TIManager"/>,
    /// <see cref="SilabsManager"/>, <see cref="PicoManager"/>, <see cref="NanoDeviceManager"/>).
    /// This is no longer parsed directly from the command line: each verb's option class
    /// (<see cref="FlashOptions"/>, <see cref="DeployOptions"/>, etc.) is adapted into this
    /// shape by <see cref="VerbOptionsMapper"/>.
    /// </summary>
    public class Options
    {
        public string DfuDeviceId { get; set; }

        public IList<string> HexFile { get; set; }

        public IList<string> BinFile { get; set; }

        public string JtagDeviceId { get; set; }

        public bool NativeDfuUpdate { get; set; }

        public bool ListNativeDfuDevices { get; set; }

        public bool NativeSwdUpdate { get; set; }

        public bool ListNativeSwdDevices { get; set; }

        public bool NativeStLinkUpdate { get; set; }

        public bool ListNativeStLinkDevices { get; set; }

        public string SerialPort { get; set; }

        public int BaudRate { get; set; }

        public string Esp32FlashMode { get; set; }

        public int Esp32FlashFrequency { get; set; }

        /// <summary>
        /// Allowed values:
        /// 2
        /// 4
        /// 8
        /// 16
        /// </summary>
        public PartitionTableSize? Esp32PartitionTableSize { get; set; }

        public bool CheckPsRam { get; set; }

        public bool NoBackupConfig { get; set; }

        /// <summary>
        /// Path to persist the ESP32 configuration partition backup to (see <see cref="NoBackupConfig"/>).
        /// If <see langword="null"/> or empty, a temporary file is used and deleted after the configuration
        /// is restored into the new firmware.
        /// </summary>
        public string ConfigBackupPath { get; set; }

        public bool TIInstallXdsDrivers { get; set; }

        public bool ListJLinkDevices { get; set; }

        public string JLinkDeviceId { get; set; }

        public int? SetVcpBaudRate { get; set; }

        public bool NanoDevice { get; set; }

        public string ClrFile { get; set; }

        public string TargetName { get; set; }

        public SupportedPlatform? Platform { get; set; }

        /// <summary>
        /// Allowed values:
        /// q[uiet]
        /// m[inimal]
        /// n[ormal]
        /// d[etailed]
        /// diag[nostic]
        /// </summary>
        public string Verbosity { get; set; }

        public string BackupPath { get; set; }

        public string BackupFile { get; set; }

        public string FwVersion { get; set; }

        public bool Update { get; set; }

        public bool Deploy { get; set; }

        public bool Preview { get; set; }

        public string DeploymentImage { get; set; }

        public bool MassErase { get; set; }

        public bool Verify { get; set; }

        public IList<string> FlashAddress { get; set; }

        public bool ResetMcu { get; set; }

        /// <summary>
        /// When <see langword="true"/>, skips the sanity check for whether the requested
        /// target fits the connected device. This is a best effort validation and it's
        /// NOT guaranteed to be fail safe.
        /// </summary>
        public bool FitCheck { get; set; }

        public bool ListTargets { get; set; }

        public bool ListComPorts { get; set; }

        public bool ClearCache { get; set; }

        public bool ListDevices { get; set; }

        public bool DeviceDetails { get; set; }

        public bool IdentifyFirmware { get; set; }

        public string FileDeployment { get; set; }

        public string NetworkDeployment { get; set; }

        public string FwArchivePath { get; set; }

        public bool UpdateFwArchive { get; set; }

        public bool FromFwArchive { get; set; }

        public bool SuppressNanoFFVersionCheck { get; set; }

        public bool Uf2Deploy { get; set; }
    }
}

