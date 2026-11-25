// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Backs up ESP32 flash without downloading or flashing firmware.
    /// </summary>
    [Verb("backup", HelpText = "Back up ESP32 partitions or the entire flash to a directory, without downloading firmware.")]
    public class BackupOptions : Esp32BackupOptionsBase
    {
    }
}
