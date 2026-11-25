// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Restores an ESP32 backup without downloading firmware.
    /// </summary>
    [Verb("restore", HelpText = "Restore ESP32 partitions from a backup directory; whole-flash backups require wholeflash.")]
    public class RestoreOptions : Esp32BackupOptionsBase
    {
    }
}
