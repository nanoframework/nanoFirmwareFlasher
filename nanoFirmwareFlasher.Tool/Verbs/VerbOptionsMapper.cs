// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Adapts the new per-verb option classes into the legacy flat <see cref="Options"/> bag,
    /// so the existing platform managers and <c>Program.RunOptionsAndReturnExitCodeAsync</c>
    /// dispatch logic can be reused unchanged.
    /// </summary>
    public static class VerbOptionsMapper
    {
        public static Options ToLegacyOptions(this FlashOptions o)
        {
            var legacy = new Options
            {
                Verbosity = o.Verbosity,
                SuppressNanoFFVersionCheck = o.SuppressNanoFFVersionCheck,
                TargetName = o.TargetName,
                Platform = o.Platform,
                SerialPort = o.SerialPort,
                FwVersion = o.FwVersion,
                Preview = o.Preview,
                Update = true,
                MassErase = o.MassErase,
                Verify = o.Verify,
                ResetMcu = o.ResetMcu,
                FitCheck = o.NoFitCheck,
                HexFile = o.HexFile ?? Array.Empty<string>(),
                BinFile = o.BinFile ?? Array.Empty<string>(),
                FlashAddress = o.FlashAddress ?? Array.Empty<string>(),
                ClrFile = o.ClrFile,
                CheckPsRam = o.CheckPsRam,
                FromFwArchive = o.FromFwArchive,
                FwArchivePath = o.FwArchivePath,
                Uf2Deploy = o.Uf2Deploy,
                BaudRate = o.BaudRate,
                Esp32FlashMode = o.Esp32FlashMode,
                Esp32FlashFrequency = o.Esp32FlashFrequency,
                Esp32PartitionTableSize = o.Esp32PartitionTableSize,
                // J-Link (EFM32) doesn't have an "interface" concept like STM32, so deviceid
                // is mapped here too - it's simply unused by other platforms' managers.
                JLinkDeviceId = o.DeviceId,
                SetVcpBaudRate = o.VcpBaudRate,
            };

            // "backupflash <path>" backs up the device's ENTIRE flash contents (ESP32 only),
            // matching today's --backuppath/--backupfile behavior exactly. Not given => no
            // backup at all (today's default). Splits the single path into directory + file name.
            if (!string.IsNullOrEmpty(o.BackupFlash))
            {
                string backupDirectory = Path.GetDirectoryName(o.BackupFlash);
                legacy.BackupPath = string.IsNullOrEmpty(backupDirectory) ? "." : backupDirectory;
                legacy.BackupFile = Path.GetFileName(o.BackupFlash);
            }

            bool noTargetInfo = string.IsNullOrEmpty(o.TargetName) && o.Platform is null;
            legacy.NanoDevice = noTargetInfo && !string.IsNullOrEmpty(o.SerialPort);

            // nativedfu folds into "dfu" and nativestlink folds into "jtag" (see proposal doc);
            // both use the native, no-external-tool implementations.
            switch (o.Interface)
            {
                case FlashInterface.Dfu:
                    legacy.NativeDfuUpdate = true;
                    legacy.DfuDeviceId = o.DeviceId;
                    break;

                case FlashInterface.Jtag:
                    legacy.NativeStLinkUpdate = true;
                    legacy.JtagDeviceId = o.DeviceId;
                    break;

                case FlashInterface.NativeSwd:
                    legacy.NativeSwdUpdate = true;
                    legacy.JtagDeviceId = o.DeviceId;
                    break;
            }

            return legacy;
        }

        public static Options ToLegacyOptions(this DeployOptions o)
        {
            var legacy = new Options
            {
                Verbosity = o.Verbosity,
                SuppressNanoFFVersionCheck = o.SuppressNanoFFVersionCheck,
                TargetName = o.TargetName,
                Platform = o.Platform,
                SerialPort = o.SerialPort,
                DeploymentImage = o.DeploymentImage,
                FileDeployment = o.FileDeployment,
                NetworkDeployment = o.NetworkDeployment,
                HexFile = Array.Empty<string>(),
                BinFile = Array.Empty<string>(),
                FlashAddress = Array.Empty<string>(),
            };

            legacy.Deploy = !string.IsNullOrEmpty(o.DeploymentImage);

            bool noTargetInfo = string.IsNullOrEmpty(o.TargetName) && o.Platform is null;
            legacy.NanoDevice = noTargetInfo && legacy.Deploy && !string.IsNullOrEmpty(o.SerialPort);

            return legacy;
        }

        public static Options ToLegacyOptions(this ListOptions o)
        {
            var legacy = new Options
            {
                Verbosity = o.Verbosity,
                SuppressNanoFFVersionCheck = o.SuppressNanoFFVersionCheck,
                Platform = o.Platform,
                Preview = o.Preview,
                FromFwArchive = o.FromFwArchive,
                FwArchivePath = o.FwArchivePath,
                HexFile = Array.Empty<string>(),
                BinFile = Array.Empty<string>(),
                FlashAddress = Array.Empty<string>(),
                ListTargets = o.Targets,
                ListDevices = o.Devices,
                ListComPorts = o.Ports,
                ListNativeDfuDevices = o.Dfu,
                ListNativeStLinkDevices = o.Jtag,
                ListJLinkDevices = o.JLink,
                ListNativeSwdDevices = o.NativeSwd,
            };

            // the legacy platform auto-detection only looks at the *external tool* list
            // flags, not the native ones, so resolve the platform here instead.
            if (legacy.Platform is null && (o.Dfu || o.Jtag || o.NativeSwd))
            {
                legacy.Platform = SupportedPlatform.stm32;
            }
            else if (legacy.Platform is null && o.JLink)
            {
                legacy.Platform = SupportedPlatform.efm32;
            }

            return legacy;
        }

        public static Options ToLegacyOptions(this DetailsOptions o)
        {
            var legacy = new Options
            {
                Verbosity = o.Verbosity,
                SuppressNanoFFVersionCheck = o.SuppressNanoFFVersionCheck,
                TargetName = o.TargetName,
                Platform = o.Platform,
                SerialPort = o.SerialPort,
                DeviceDetails = true,
                HexFile = Array.Empty<string>(),
                BinFile = Array.Empty<string>(),
                FlashAddress = Array.Empty<string>(),
            };

            bool noTargetInfo = string.IsNullOrEmpty(o.TargetName) && o.Platform is null;
            legacy.NanoDevice = noTargetInfo && !string.IsNullOrEmpty(o.SerialPort);

            return legacy;
        }

        public static Options ToLegacyOptions(this IdentifyOptions o)
        {
            var legacy = new Options
            {
                Verbosity = o.Verbosity,
                SuppressNanoFFVersionCheck = o.SuppressNanoFFVersionCheck,
                Platform = o.Platform,
                SerialPort = o.SerialPort,
                IdentifyFirmware = true,
                HexFile = Array.Empty<string>(),
                BinFile = Array.Empty<string>(),
                FlashAddress = Array.Empty<string>(),
            };

            if (o.Platform is null && !string.IsNullOrEmpty(o.SerialPort))
            {
                // the generic nanoDevice path only surfaces IdentifyFirmware from within
                // its Update branch, so ask it to run an update-check without deploying.
                legacy.NanoDevice = true;
                legacy.Update = true;
            }

            return legacy;
        }

        public static Options ToLegacyOptions(this CacheOptions o)
        {
            return new Options
            {
                Verbosity = o.Verbosity,
                SuppressNanoFFVersionCheck = o.SuppressNanoFFVersionCheck,
                Platform = o.Platform,
                TargetName = o.TargetName,
                FwVersion = o.FwVersion,
                Preview = o.Preview,
                FwArchivePath = o.FwArchivePath,
                ClearCache = o.Clear,
                UpdateFwArchive = o.Download,
                HexFile = Array.Empty<string>(),
                BinFile = Array.Empty<string>(),
                FlashAddress = Array.Empty<string>(),
            };
        }
    }
}
