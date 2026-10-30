// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Options shared by standalone ESP32 backup and restore.
    /// </summary>
    public abstract class Esp32BackupOptionsBase : VerbOptionsBase
    {
        [Option("platform", Default = SupportedPlatform.esp32, HelpText = "Device platform. Only esp32 is supported; defaults to esp32.")]
        public SupportedPlatform Platform { get; set; } = SupportedPlatform.esp32;

        [Option("serialport", Required = true, HelpText = "Serial port where the ESP32 is connected.")]
        public string SerialPort { get; set; }

        [Option("path", Required = true, HelpText = "Backup directory containing manifest.json and binary images.")]
        public string Path { get; set; }

        [Option("partition", Min = 1, HelpText = "Partition selectors or actual partition labels, separated by spaces. Defaults to all; all cannot be combined with other selectors.")]
        public IList<string> Partitions { get; set; }

        [Option("wholeflash", HelpText = "Back up or restore the entire flash, including bootloader and partition table. Cannot be combined with partition; required to restore a whole-flash backup.")]
        public bool WholeFlash { get; set; }

        [Option("baud", Default = 1500000, HelpText = "Serial baud rate. Defaults to 1500000.")]
        public int BaudRate { get; set; } = 1500000;

        /// <summary>
        /// Maps omitted selectors to all partitions, or no selectors in whole-flash mode.
        /// </summary>
        public IEnumerable<string> GetPartitions() => WholeFlash
            ? Array.Empty<string>()
            : Partitions != null && Partitions.Count > 0 ? Partitions : new[] { "all" };

        public static string Validate(Esp32BackupOptionsBase options)
        {
            if (options.Platform != SupportedPlatform.esp32)
            {
                return "Standalone backup and restore only support platform esp32.";
            }

            if (string.IsNullOrWhiteSpace(options.SerialPort))
            {
                return "serialport is required.";
            }

            if (string.IsNullOrWhiteSpace(options.Path))
            {
                return "path is required.";
            }

            if (options.BaudRate <= 0)
            {
                return "baud must be positive.";
            }

            if (options.Partitions != null && options.Partitions.Count > 0)
            {
                if (options.WholeFlash)
                {
                    return "wholeflash and partition cannot be combined.";
                }

                if (options.Partitions.Any(string.IsNullOrWhiteSpace))
                {
                    return "partition selectors cannot be empty.";
                }

                if (options.Partitions.Count > 1
                    && options.Partitions.Any(p => string.Equals(p, "all", StringComparison.OrdinalIgnoreCase)))
                {
                    return "all cannot be combined with other partition selectors.";
                }
            }

            return null;
        }
    }
}
